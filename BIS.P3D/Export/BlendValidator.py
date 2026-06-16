"""
BlendValidator - Automated .blend file quality inspection.

Usage:
    blender --background --python BlendValidator.py -- /path/to/blend/dir [--verbose]

Scans all .blend files in the given directory, opens each in headless Blender,
and validates:
  - Mesh objects have non-empty, well-formed names
  - LOD collection hierarchy exists (LODs > view/shadow_volume/geometry > *_R0, *_VP, etc.)
  - Proxies collection exists (may be empty)
  - Textures are loaded and correctly assigned
  - No stale collections (Camera, Light, Cube) are present
  - Armature/rig setup when applicable

Returns exit code 0 if all pass, 1 if any fail.
"""

import sys
import os
import json
import traceback
from pathlib import Path


# ─── Validation rules ───────────────────────────────────────────────


def check_mesh_naming(objects, results):
    """Validate mesh object naming conventions."""
    issues = []
    for obj in objects:
        if obj.type != 'MESH':
            continue
        name = obj.name
        if not name or name.startswith('.'):
            issues.append(f"  BAD NAME: {name} (empty or starts with dot)")
        elif '.' in name and name.rsplit('.', 1)[1].isdigit():
            issues.append(f"  STALE SUFFIX: {name} (has .001/.002 auto-rename)")
    results['naming'] = {
        'pass': len(issues) == 0,
        'issues': issues,
        'count': sum(1 for o in objects if o.type == 'MESH'),
    }


def check_lod_collections(objects, results):
    """Validate LOD collection hierarchy."""
    coll_names = {c.name for c in bpy.data.collections}
    issues = []

    # Master LODs collection must exist
    if 'LODs' not in coll_names:
        issues.append("MISSING: Master LODs collection")
    else:
        lods = bpy.data.collections['LODs']
        sub_names = {c.name for c in lods.children}
        # Should have at least one of view/shadow_volume/geometry
        type_colls = sub_names & {'view', 'shadow_volume', 'geometry', 'point_cloud', 'misc', 'physx', 'wreck'}
        if not type_colls:
            issues.append(f"MISSING: No type sub-collections under LODs (found: {sub_names})")
        else:
            # Check for LOD-specific sub-collections (e.g. view_R0)
            for tc in type_colls:
                parent = bpy.data.collections.get(tc)
                if parent and parent.children:
                    lod_subs = [c.name for c in parent.children]
                    tag_found = any(
                        any(c.name.endswith(tag) for tag in ['_R0', '_VP', '_SV', '_GEO', '_FG', '_PHY', '_WRK', '_LC', '_MEM'])
                        for c in parent.children
                    )
                    if not tag_found:
                        issues.append(f"MISSING: No LOD-tagged sub-collections under {tc} (found: {lod_subs})")

    results['lod_collections'] = {
        'pass': len(issues) == 0,
        'issues': issues,
        'collections': list(coll_names),
    }


def check_proxies(objects, results):
    """Validate Proxies collection exists."""
    coll_names = {c.name for c in bpy.data.collections}
    if 'Proxies' not in coll_names:
        results['proxies'] = {'pass': False, 'issues': ["MISSING: Proxies collection"]}
    else:
        proxy_coll = bpy.data.collections['Proxies']
        proxy_count = sum(1 for o in proxy_coll.objects if o.type == 'MESH')
        results['proxies'] = {
            'pass': True,
            'issues': [],
            'count': proxy_count,
        }


def check_textures(objects, results):
    """Check that textures are loaded and assigned."""
    tex_count = len(bpy.data.images)
    mesh_with_materials = sum(1 for o in objects if o.type == 'MESH' and o.material_slots)
    textured = sum(
        1 for o in objects if o.type == 'MESH'
        and any(
            slot.material and slot.material.node_tree
            and any(n.type == 'TEX_IMAGE' for n in slot.material.node_tree.nodes)
            for slot in o.material_slots
        )
    )
    results['textures'] = {
        'pass': tex_count > 0,
        'issues': [] if tex_count > 0 else ["WARNING: No images loaded (may be headless rendering)"],
        'images': tex_count,
        'meshes_with_material_slots': mesh_with_materials,
        'meshes_with_textured_nodes': textured,
    }


def check_scene_cleanup(objects, results):
    """Check no stale startup objects remain."""
    stale = [o.name for o in objects if o.type in {'CAMERA', 'LIGHT'} or (o.type == 'MESH' and o.name == 'Cube')]
    results['scene_cleanup'] = {
        'pass': len(stale) == 0,
        'issues': [] if not stale else [f"STALE: {', '.join(stale)}"],
    }


def check_rig(objects, results):
    """Check armature/rig if present."""
    armatures = [o for o in objects if o.type == 'ARMATURE']
    if armatures:
        rig_coll = bpy.data.collections.get('Rig')
        issues = []
        if rig_coll is None:
            issues.append("MISSING: Rig collection (armature present)")
        bone_count = sum(len(a.data.bones) for a in armatures)
        rig_coll = 'Rig' in {c.name for c in bpy.data.collections}
        results['rig'] = {
            'pass': rig_coll,
            'issues': issues,
            'armatures': len(armatures),
            'bones': bone_count,
        }
    else:
        results['rig'] = {'pass': True, 'issues': [], 'armatures': 0}


# ─── Main validation ────────────────────────────────────────────────


def validate_blend(blend_path, verbose=False):
    """Open a .blend file and run all validation checks."""
    results = {
        'file': os.path.basename(blend_path),
        'path': blend_path,
        'pass': True,
        'checks': {},
    }

    try:
        # Load the .blend
        bpy.ops.wm.open_mainfile(filepath=str(blend_path))
        objects = list(bpy.data.objects)

        if verbose:
            print(f"\n  Objects ({len(objects)}):")
            for o in objects:
                print(f"    {o.name} ({o.type})")
            print(f"  Collections ({len(bpy.data.collections)}):")
            for c in bpy.data.collections:
                print(f"    {c.name} ({len(c.objects)} objects, {len(c.children)} children)")

        # Run checks
        check_mesh_naming(objects, results['checks'])
        check_lod_collections(objects, results['checks'])
        check_proxies(objects, results['checks'])
        check_textures(objects, results['checks'])
        check_scene_cleanup(objects, results['checks'])
        check_rig(objects, results['checks'])

        # Overall pass/fail
        for check_name, check_data in results['checks'].items():
            if not check_data.get('pass', True):
                results['pass'] = False

    except Exception as e:
        results['pass'] = False
        results['error'] = str(e)
        if verbose:
            traceback.print_exc()

    return results


def main():
    import bpy  # noqa: F811 — imported at module level in Blender context

    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if not args:
        print("Usage: blender --background --python BlendValidator.py -- /path/to/blend/dir [--verbose]")
        sys.exit(1)

    blend_dir = Path(args[0])
    verbose = '--verbose' in args

    if not blend_dir.is_dir():
        print(f"ERROR: Directory not found: {blend_dir}")
        sys.exit(1)

    blend_files = sorted(blend_dir.glob('*.blend'))
    if not blend_files:
        print(f"NO BLENDS: No .blend files found in {blend_dir}")
        sys.exit(0)

    print(f"[BlendValidator] Validating {len(blend_files)} .blend file(s) in {blend_dir}")
    print()

    all_pass = True
    summary = {'passed': 0, 'failed': 0, 'total': len(blend_files), 'results': []}

    for i, blend_path in enumerate(blend_files, 1):
        results = validate_blend(blend_path, verbose=verbose)
        summary['results'].append(results)

        status = 'PASS' if results['pass'] else 'FAIL'
        print(f"  [{i}/{len(blend_files)}] {status} {results['file']}")

        if not results['pass']:
            all_pass = False
            for check_name, check_data in results['checks'].items():
                for issue in check_data.get('issues', []):
                    if issue:
                        print(f"         {issue}")
            if 'error' in results:
                print(f"         ERROR: {results['error']}")

        if results['pass']:
            summary['passed'] += 1
        else:
            summary['failed'] += 1

        # Print per-file metrics
        c = results['checks']
        naming = c.get('naming', {}).get('count', 0)
        lods = len(c.get('lod_collections', {}).get('collections', []))
        tex = c.get('textures', {}).get('images', 0)
        proxies = c.get('proxies', {}).get('count', 0)
        arm = c.get('rig', {}).get('armatures', 0)
        bones = c.get('rig', {}).get('bones', 0)
        details = f"{naming} meshes, {lods} collections, {tex} textures"
        if arm:
            details += f", {arm} armatures ({bones} bones)"
        if proxies:
            details += f", {proxies} proxies"
        print(f"         {details}")
        print()

    print(f"[BlendValidator] Summary: {summary['passed']}/{summary['total']} passed"
          + (f", {summary['failed']} failed" if summary['failed'] else ""))
    sys.exit(0 if all_pass else 1)


if __name__ == '__main__':
    main()
