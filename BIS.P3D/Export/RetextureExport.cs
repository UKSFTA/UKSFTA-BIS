using System;
using System.IO;

namespace BIS.P3D.Export
{
    public static class RetextureExport
    {
        /// <summary>
        /// Generates a Blender Python script for re-texturing a P3D model.
        /// The script imports the model, applies the remap (supports exact,
        /// prefix, and suffix pattern matching), and re-exports.
        /// </summary>
        /// <param name="modelPath">Path to source .p3d</param>
        /// <param name="remapPath">Path to JSON remap config</param>
        /// <param name="outputPath">Output .p3d path</param>
        /// <param name="textureDir">Optional path to load PAA textures from (uses armaio)</param>
        /// <param name="lodFilter">Optional LOD type filter: "view", "shadow", "geometry", or null for all</param>
        /// <returns>Path to the generated script</returns>
        public static string GenerateRetextureScript(
            string modelPath, string remapPath, string outputPath, string? textureDir = null, string? lodFilter = null)
        {
            string remapJson = File.ReadAllText(remapPath);
            string scriptDir = Path.GetDirectoryName(outputPath) ?? ".";
            string scriptName = $"retex_{Path.GetFileNameWithoutExtension(modelPath)}.py";
            string scriptPath = Path.Combine(scriptDir, scriptName);

            using var writer = new StreamWriter(scriptPath);
            writer.WriteLine("import bpy");
            writer.WriteLine("import json");
            writer.WriteLine("import os");
            writer.WriteLine("import sys");
            writer.WriteLine("import traceback");
            writer.WriteLine();

            // Optional armaio PAA loading
            if (textureDir != null)
            {
                string pyTexDir = textureDir.Replace("\\", "/");
                writer.WriteLine("# Load PAA textures via armaio");
                writer.WriteLine("try:");
                writer.WriteLine("    import armaio");
                writer.WriteLine("    load_paa = True");
                writer.WriteLine("except ImportError:");
                writer.WriteLine("    print('[BIS] armaio not available, PAA textures will be missing', flush=True)");
                writer.WriteLine("    load_paa = False");
                writer.WriteLine($"tex_dir = r\"{pyTexDir}\"");
                writer.WriteLine();
            }

            writer.WriteLine("# Enable a3ob addon");
            writer.WriteLine("bpy.ops.preferences.addon_enable(module=\"bl_ext.blender_org.Arma3ObjectBuilder\")");
            writer.WriteLine();

            string pyModelPath = modelPath.Replace("\\", "/");
            string pyOutputPath = outputPath.Replace("\\", "/");

            writer.WriteLine($"model_path = r\"{pyModelPath}\"");
            writer.WriteLine($"output_path = r\"{pyOutputPath}\"");
            writer.WriteLine();

            writer.WriteLine("# Texture/material remap configuration");
            writer.WriteLine($"remap = {remapJson}");
            writer.WriteLine();

            writer.WriteLine("def _remap_path(path, table, prefix_map, suffix_map):");
            writer.WriteLine("    \"\"\"Remap path using exact, prefix, then suffix matching.\"\"\"");
            writer.WriteLine("    if not path:");
            writer.WriteLine("        return None");
            writer.WriteLine("    norm = path.replace('\\\\', '/')");
            writer.WriteLine();
            writer.WriteLine("    # 1) Exact match");
            writer.WriteLine("    if table and norm in table:");
            writer.WriteLine("        return table[norm]");
            writer.WriteLine();
            writer.WriteLine("    # 2) Prefix match (longest first)");
            writer.WriteLine("    if prefix_map:");
            writer.WriteLine("        sorted_pfx = sorted(prefix_map.keys(), key=len, reverse=True)");
            writer.WriteLine("        for pfx in sorted_pfx:");
            writer.WriteLine("            pfx_norm = pfx.replace('\\\\', '/')");
            writer.WriteLine("            if norm.startswith(pfx_norm):");
            writer.WriteLine("                suffix = norm[len(pfx_norm):]");
            writer.WriteLine("                return prefix_map[pfx].replace('\\\\', '/') + suffix");
            writer.WriteLine();
            writer.WriteLine("    # 3) Suffix match (longest first)");
            writer.WriteLine("    if suffix_map:");
            writer.WriteLine("        sorted_sfx = sorted(suffix_map.keys(), key=len, reverse=True)");
            writer.WriteLine("        for sfx in sorted_sfx:");
            writer.WriteLine("            sfx_norm = sfx.replace('\\\\', '/')");
            writer.WriteLine("            if norm.endswith(sfx_norm):");
            writer.WriteLine("                head = norm[:-len(sfx_norm)]");
            writer.WriteLine("                return head + suffix_map[sfx].replace('\\\\', '/')");
            writer.WriteLine();
            writer.WriteLine("    return None");
            writer.WriteLine();

            writer.WriteLine("def _load_paa_textures(tex_dir, materials):");
            writer.WriteLine("    \"\"\"Load PAA textures from tex_dir into Blender images via armaio.\"\"\"");
            writer.WriteLine("    if not os.path.isdir(tex_dir):");
            writer.WriteLine("        print(f'[BIS] Texture dir not found: {tex_dir}', flush=True)");
            writer.WriteLine("        return 0");
            writer.WriteLine("    if not load_paa:");
            writer.WriteLine("        return 0");
            writer.WriteLine("    from armaio.paa.pillow import open_paa_image");
            writer.WriteLine("    from PIL import Image");
            writer.WriteLine("    import numpy as np");
            writer.WriteLine("    count = 0");
            writer.WriteLine("    seen = set()");
            writer.WriteLine("    for mat in materials:");
            writer.WriteLine("        if not mat.name.startswith('P3D:'):");
            writer.WriteLine("            continue");
            writer.WriteLine("        tex = mat.a3ob_properties_material.texture_path");
            writer.WriteLine("        if not tex or tex in seen:");
            writer.WriteLine("            continue");
            writer.WriteLine("        seen.add(tex)");
            writer.WriteLine("        tex_name = os.path.basename(tex)");
            writer.WriteLine("        tex_path = os.path.join(tex_dir, tex_name)");
            writer.WriteLine("        if not os.path.isfile(tex_path) and not tex_name.endswith('.paa'):");
            writer.WriteLine("            tex_path = tex_path + '.paa'");
            writer.WriteLine("        if not os.path.isfile(tex_path):");
            writer.WriteLine("            continue");
            writer.WriteLine("        try:");
            writer.WriteLine("            pil_img = open_paa_image(tex_path)");
            writer.WriteLine("            img = bpy.data.images.new(tex_name, pil_img.width, pil_img.height, alpha=True)");
            writer.WriteLine("            img.filepath = tex_path");
            writer.WriteLine("            pixels = np.array(pil_img.convert('RGBA'), dtype=np.float32) / 255.0");
            writer.WriteLine("            img.pixels = pixels.ravel()");
            writer.WriteLine("            img.update()");
            writer.WriteLine("            count += 1");
            writer.WriteLine("        except Exception as e:");
            writer.WriteLine("            print(f'[BIS]   Failed to load {tex_path}: {e}', flush=True)");
            writer.WriteLine("    return count");
            writer.WriteLine();

            writer.WriteLine("try:");
            writer.WriteLine("    # Import model");
            writer.WriteLine("    print(f\"[BIS] Importing {{os.path.basename(model_path)}}...\", flush=True)");
            writer.WriteLine("    bpy.ops.a3ob.import_p3d(filepath=model_path,");
            writer.WriteLine("        absolute_paths=True, enclose=True, groupby='TYPE',");
            writer.WriteLine("        additional_data={{'NORMALS','PROPS','MASS','SELECTIONS','UV','MATERIALS'}},");
            writer.WriteLine("        proxy_action='SEPARATE', translate_selections=True)");
            writer.WriteLine("    print(\"[BIS] Import complete\", flush=True)");
            writer.WriteLine();

            // Collect P3D materials for processing
            writer.WriteLine("    # ─── Collect all P3D materials ───");
            writer.WriteLine("    p3d_mats = [m for m in bpy.data.materials if m.name.startswith('P3D:')]");
            writer.WriteLine("    print(f\"[BIS] Found {{len(p3d_mats)}} P3D materials\", flush=True)");
            writer.WriteLine();

            // LOD filter: restrict remapping to specific LOD type
            if (!string.IsNullOrEmpty(lodFilter))
            {
                string lodFilterUpper = lodFilter.ToUpperInvariant();
                writer.WriteLine($"    # Filter materials by LOD type: {lodFilterUpper}");
                writer.WriteLine("    # Build material-to-LOD map from scene objects");
                writer.WriteLine("    mat_lod = {}");
                writer.WriteLine("    for obj in bpy.data.objects:");
                writer.WriteLine("        if not getattr(obj, 'a3ob_properties_object', None):");
                writer.WriteLine("            continue");
                writer.WriteLine("        if not obj.a3ob_properties_object.is_a3_lod:");
                writer.WriteLine("            continue");
                writer.WriteLine("        lod_type = obj.a3ob_properties_object.lod");
                writer.WriteLine("        for slot in getattr(obj, 'material_slots', []):");
                writer.WriteLine("            if slot.material and slot.material.name.startswith('P3D:'):");
                writer.WriteLine("                if slot.material.name not in mat_lod:");
                writer.WriteLine("                    mat_lod[slot.material.name] = lod_type");
                writer.WriteLine("    lod_filter = \"{lodFilterUpper}\"");
                writer.WriteLine("    before = len(p3d_mats)");
                writer.WriteLine("    p3d_mats = [m for m in p3d_mats if mat_lod.get(m.name, '') == lod_filter]");
                writer.WriteLine("    after = len(p3d_mats)");
                writer.WriteLine("    print(f\"[BIS] LOD filter [{lodFilterUpper}]: {{before}} -> {{after}} materials\", flush=True)");
                writer.WriteLine();
            }

            // Load PAA textures if textureDir provided
            if (textureDir != null)
            {
                writer.WriteLine("    # ─── Load PAA textures via armaio ───");
                writer.WriteLine("    loaded = _load_paa_textures(tex_dir, p3d_mats)");
                writer.WriteLine("    print(f\"[BIS] Loaded {{loaded}} PAA textures\", flush=True)");
                writer.WriteLine();
            }

            // Apply remapping
            writer.WriteLine("    # ─── Apply texture/material remapping ───");
            writer.WriteLine("    tex_exact = remap.get('textures', {{}})");
            writer.WriteLine("    tex_prefix = remap.get('texture_prefixes', {{}})");
            writer.WriteLine("    tex_suffix = remap.get('texture_suffixes', {{}})");
            writer.WriteLine("    mat_exact = remap.get('materials', {{}})");
            writer.WriteLine("    mat_prefix = remap.get('material_prefixes', {{}})");
            writer.WriteLine("    mat_suffix = remap.get('material_suffixes', {{}})");
            writer.WriteLine("    col_map = remap.get('color_types', {{}})");
            writer.WriteLine();
            writer.WriteLine("    tex_count = 0");
            writer.WriteLine("    mat_count = 0");
            writer.WriteLine("    col_count = 0");
            writer.WriteLine("    for mat in p3d_mats:");
            writer.WriteLine("        props = mat.a3ob_properties_material");
            writer.WriteLine();
            writer.WriteLine("        # Remap texture path");
            writer.WriteLine("        new_tex = _remap_path(props.texture_path, tex_exact, tex_prefix, tex_suffix)");
            writer.WriteLine("        if new_tex is not None and new_tex != props.texture_path:");
            writer.WriteLine("            print(f\"[BIS]   Tex: {{props.texture_path}} -> {{new_tex}}\", flush=True)");
            writer.WriteLine("            props.texture_path = new_tex");
            writer.WriteLine("            tex_count += 1");
            writer.WriteLine();
            writer.WriteLine("        # Remap material (rvmat) path");
            writer.WriteLine("        new_mat = _remap_path(props.material_path, mat_exact, mat_prefix, mat_suffix)");
            writer.WriteLine("        if new_mat is not None and new_mat != props.material_path:");
            writer.WriteLine("            print(f\"[BIS]   Mat: {{props.material_path}} -> {{new_mat}}\", flush=True)");
            writer.WriteLine("            props.material_path = new_mat");
            writer.WriteLine("            mat_count += 1");
            writer.WriteLine();
            writer.WriteLine("        # Remap color type");
            writer.WriteLine("        old_col = props.color_type");
            writer.WriteLine("        if old_col and old_col in col_map:");
            writer.WriteLine("            new_col = col_map[old_col]");
            writer.WriteLine("            print(f\"[BIS]   Color: {{old_col}} -> {{new_col}}\", flush=True)");
            writer.WriteLine("            props.color_type = new_col");
            writer.WriteLine("            col_count += 1");
            writer.WriteLine();
            writer.WriteLine("    parts = []");
            writer.WriteLine("    if tex_count: parts.append(f'{{tex_count}} tex')");
            writer.WriteLine("    if mat_count: parts.append(f'{{mat_count}} mat')");
            writer.WriteLine("    if col_count: parts.append(f'{{col_count}} color')");
            writer.WriteLine("    print(f\"[BIS] Remapped {{', '.join(parts) or 'nothing'}}\", flush=True)");
            writer.WriteLine();

            // Re-export
            writer.WriteLine("    # Export modified model");
            writer.WriteLine("    print(f\"[BIS] Exporting to {{output_path}}...\", flush=True)");
            writer.WriteLine("    bpy.ops.a3ob.export_p3d(filepath=output_path,");
            writer.WriteLine("        relative_paths=True, preserve_normals=True,");
            writer.WriteLine("        validate_meshes=False, use_selection=False,");
            writer.WriteLine("        visible_only=True, apply_transforms=True,");
            writer.WriteLine("        apply_modifiers=True, sort_sections=True,");
            writer.WriteLine("        lod_collisions='FAIL', force_lowercase=True,");
            writer.WriteLine("        generate_components=True)");
            writer.WriteLine("    print(f\"[BIS] Exported {{os.path.basename(output_path)}}\", flush=True)");
            writer.WriteLine();

            writer.WriteLine("except Exception as e:");
            writer.WriteLine("    print(f\"[BIS] Failed: {{e}}\", flush=True)");
            writer.WriteLine("    traceback.print_exc()");
            writer.WriteLine("    sys.exit(1)");
            writer.WriteLine();

            return scriptPath;
        }
    }
}
