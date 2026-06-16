using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.P3D.Export
{
    public static class BlenderExport
    {
        /// <summary>
        /// Finds all .paa files in extractedDir and converts them to .png in outputDir,
        /// preserving the relative directory structure.
        /// Returns the number of textures successfully converted.
        /// </summary>
        public static int ExportAll(string extractedDir, string outputDir)
        {
            var paaFiles = Directory.GetFiles(extractedDir, "*.paa", SearchOption.AllDirectories);
            Directory.CreateDirectory(outputDir);

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            int count = 0;

            Parallel.ForEach(paaFiles, parallelOptions, paaPath =>
            {
                try
                {
                    string relPath = GetRelativePath(extractedDir, paaPath);
                    string relDir = Path.GetDirectoryName(relPath);
                    string pngName = Path.ChangeExtension(Path.GetFileName(paaPath), ".png");
                    string pngDir = string.IsNullOrEmpty(relDir) ? outputDir : Path.Combine(outputDir, relDir);
                    string pngPath = Path.Combine(pngDir, pngName);

                    Directory.CreateDirectory(pngDir);

                    using var paaStream = File.OpenRead(paaPath);
                    using var pngStream = File.Create(pngPath);
                    PaaToPngConverter.ConvertToPng(paaStream, pngStream);
                    System.Threading.Interlocked.Increment(ref count);
                }
                catch
                {
                    // Skip textures that fail conversion
                }
            });

            return count;
        }

        /// <summary>
        /// Computes a relative path from a base directory to a target path.
        /// </summary>
        private static string GetRelativePath(string baseDir, string targetPath)
        {
            if (!baseDir.EndsWith(Path.DirectorySeparatorChar.ToString()))
                baseDir += Path.DirectorySeparatorChar;

            var baseUri = new Uri(baseDir);
            var targetUri = new Uri(targetPath);
            var relativeUri = baseUri.MakeRelativeUri(targetUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            return relativePath.Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Finds a model.cfg skeleton definition file in the extracted directory.
        /// Returns the full path if found, or null if not found.
        /// </summary>
        public static string FindModelCfg(string extractedDir)
        {
            var cfgFiles = Directory.GetFiles(extractedDir, "model.cfg", SearchOption.AllDirectories);
            if (cfgFiles.Length > 0)
                return cfgFiles[0];

            foreach (var f in Directory.GetFiles(extractedDir, "*.cfg", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(f);
                if (name.Equals("model.cfg", StringComparison.OrdinalIgnoreCase))
                    return f;
            }

            return null;
        }

        /// <summary>
        /// Checks if a P3D file has empty LODs that cause the addon to hang.
        /// </summary>
        private static bool HasEmptyLods(string p3dPath)
        {
            try
            {
                using var stream = File.OpenRead(p3dPath);
                var p3d = new P3D(stream);
                return p3d.LODs.Any(lod => lod.VertexCount == 0 && lod.Resolution == 1100.0f);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Builds a texture dictionary in C# as a Python dict literal string.
        /// Scans the extracted dir and optional textures dir (PNG pre-conversions).
        /// Returns: string containing the Python dict literal with all image entries.
        /// </summary>
        private static string BuildImageDictLiteral(string extractedDir, string? texturesDir)
        {
            var dict = new Dictionary<string, (string path, string type)>(StringComparer.OrdinalIgnoreCase);
            int count = 0;

            // Scan extracted dir for PAAs
            foreach (var f in Directory.GetFiles(extractedDir, "*.paa", SearchOption.AllDirectories))
            {
                string key = Path.GetFileName(f).ToLowerInvariant();
                // Basename key (first wins — fallback for textures without directory context)
                if (!dict.ContainsKey(key))
                {
                    dict[key] = (f, "PAA");
                    count++;
                }
                // Full relative path key (always unique — used for rvmat-directed resolution)
                string relKey = GetRelativePath(extractedDir, f).Replace('\\', '/').ToLowerInvariant();
                if (!dict.ContainsKey(relKey))
                {
                    dict[relKey] = (f, "PAA");
                }
            }

            // Scan extracted dir for PNGs
            foreach (var f in Directory.GetFiles(extractedDir, "*.png", SearchOption.AllDirectories))
            {
                string key = Path.GetFileName(f).ToLowerInvariant();
                // Basename key (first wins)
                if (!dict.ContainsKey(key))
                {
                    dict[key] = (f, "PNG");
                    count++;
                }
                // Full relative path key (always unique)
                string relKey = GetRelativePath(extractedDir, f).Replace('\\', '/').ToLowerInvariant();
                if (!dict.ContainsKey(relKey))
                {
                    dict[relKey] = (f, "PNG");
                }
            }

            // Scan texture dir for PNGs (PNG prefs, may overwrite PAA entries)
            if (texturesDir != null && Directory.Exists(texturesDir))
            {
                foreach (var f in Directory.GetFiles(texturesDir, "*.png", SearchOption.AllDirectories))
                {
                    string key = Path.GetFileName(f).ToLowerInvariant();
                    dict[key] = (f, "PNG");
                    // Full relative path key (overwrite for textures dir)
                    string relKey = GetRelativePath(texturesDir, f).Replace('\\', '/').ToLowerInvariant();
                    dict[relKey] = (f, "PNG");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("image_dict = {");
            foreach (var kv in dict)
            {
                // Use raw string r"..." for paths (handles backslashes on Windows)
                string pyPath = kv.Value.path.Replace("\\", "/");
                sb.AppendLine($"    \"{kv.Key}\": (r\"{pyPath}\", \"{kv.Value.type}\"),");
            }
            sb.AppendLine("}");

            Console.WriteLine($"  Built image_dict with {dict.Count} entries ({count} from extracted dir)");
            return sb.ToString();
        }

        /// <summary>
        /// Writes the shared helper functions used by all batch scripts.
        /// </summary>
        private static void WriteHelperFunctions(StreamWriter writer, string? modelCfgPath)
        {
            writer.WriteLine("def resolve_tex(tex_ref, rvmat_dir=None):");
            writer.WriteLine("    tex_ref_clean = tex_ref.replace('\\\\', '/')");
            writer.WriteLine("    # Strip existing extension first, then try PNG then PAA");
            writer.WriteLine("    without_ext = tex_ref_clean[:-4] if tex_ref_clean.lower().endswith(('.png', '.paa')) else tex_ref_clean");
            writer.WriteLine("    # First: use rvmat directory context to resolve the correct texture");
            writer.WriteLine("    # (different directories can have same-named files with different content)");
            writer.WriteLine("    if rvmat_dir:");
            writer.WriteLine("        for ext in ['.png', '.paa']:");
            writer.WriteLine("            full_key = f\"{rvmat_dir}/{without_ext}{ext}\".lower()");
            writer.WriteLine("            if full_key in image_dict:");
            writer.WriteLine("                return image_dict[full_key]");
            writer.WriteLine("    # Fallback: basename lookup");
            writer.WriteLine("    for ext in ['.png', '.paa']:");
            writer.WriteLine("        key = without_ext + ext");
            writer.WriteLine("        basename = os.path.basename(key).lower()");
            writer.WriteLine("        if basename in image_dict:");
            writer.WriteLine("            return image_dict[basename]");
            writer.WriteLine("    return None");
            writer.WriteLine();

            writer.WriteLine("def load_image(path, source_type):");
            writer.WriteLine("    if source_type == 'PNG':");
            writer.WriteLine("        try:");
            writer.WriteLine("            from PIL import Image");
            writer.WriteLine("            import numpy as np");
            writer.WriteLine("            pil_img = Image.open(path).convert('RGBA')");
            writer.WriteLine("            width, height = pil_img.size");
            writer.WriteLine("            img_name = os.path.basename(path)");
            writer.WriteLine("            # Rename stale image first to avoid basename collision");
            writer.WriteLine("            # (different directories can have same-named textures with different content)");
            writer.WriteLine("            old = bpy.data.images.get(img_name)");
            writer.WriteLine("            if old:");
            writer.WriteLine("                old.name = f\"__old_{img_name}\"");
            writer.WriteLine("            pixels = np.array(pil_img).astype(float) / 255.0");
            writer.WriteLine("            # Flip rows: Blender expects bottom-to-top, PIL gives top-to-bottom");
            writer.WriteLine("            pixels = np.flip(pixels, axis=0)");
            writer.WriteLine("            img = bpy.data.images.new(img_name, width=width, height=height, alpha=True)");
            writer.WriteLine("            img.pixels = pixels.flatten().tolist()");
            writer.WriteLine("            img.pack()");
            writer.WriteLine("            print(f\"[BIS]   Packed {img_name} via PIL ({width}x{height})\", flush=True)");
            writer.WriteLine("            return img");
            writer.WriteLine("        except Exception as e:");
            writer.WriteLine("            print(f\"[BIS]   PIL load failed for {path}: {e}\", flush=True)");
            writer.WriteLine("            img = bpy.data.images.load(path)");
            writer.WriteLine("            print(f\"[BIS]   Fallback loaded {img.name}, has_data={img.has_data}, size={img.size[0]}x{img.size[1]}\", flush=True)");
            writer.WriteLine("            if not img.has_data:");
            writer.WriteLine("                img.pack()");
            writer.WriteLine("                print(f\"[BIS]   Packed {img.name}\", flush=True)");
            writer.WriteLine("            return img");
            writer.WriteLine("    # PAA loading: rename stale image first to avoid basename collision");
            writer.WriteLine("    # (different directories can have same-named PAAs with different content)");
            writer.WriteLine("    try:");
            writer.WriteLine("        img_name = os.path.basename(path)");
            writer.WriteLine("        old_img = bpy.data.images.get(img_name)");
            writer.WriteLine("        if old_img:");
            writer.WriteLine("            old_img.name = f\"__old_{img_name}\"");
            writer.WriteLine("        bpy.ops.a3ob.import_paa(filepath=path)");
            writer.WriteLine("        img = bpy.data.images.get(img_name)");
            writer.WriteLine("        if img and hasattr(img, 'filepath_raw'):");
            writer.WriteLine("            img.filepath_raw = path");
            writer.WriteLine("        return img");
            writer.WriteLine("    except Exception:");
            writer.WriteLine("        return None");
            writer.WriteLine();

            writer.WriteLine("def assign_texture(material, color_ref):");
            writer.WriteLine("    if not material.node_tree:");
            writer.WriteLine("        print(f\"[BIS]   No node tree in: {material.name}\", flush=True)");
            writer.WriteLine("        return");
            writer.WriteLine("    bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)");
            writer.WriteLine("    if not bsdf:");
            writer.WriteLine("        print(f\"[BIS]   No Principled BSDF in: {material.name}\", flush=True)");
            writer.WriteLine("        return");
            writer.WriteLine();
            writer.WriteLine("    # Derive rvmat directory for correct texture resolution");
            writer.WriteLine("    # (different directories can have same-named textures with different content)");
            writer.WriteLine("    rvmat_dir = None");
            writer.WriteLine("    try:");
            writer.WriteLine("        rv = material.a3ob_properties_material.material_path");
            writer.WriteLine("        if rv:");
            writer.WriteLine("            rv = rv.replace('\\\\', '/')");
            writer.WriteLine("            rvmat_dir = os.path.dirname(rv)");
            writer.WriteLine("    except:");
            writer.WriteLine("        pass");
            writer.WriteLine();
            writer.WriteLine("    result = resolve_tex(color_ref, rvmat_dir)");
            writer.WriteLine("    if not result:");
            writer.WriteLine("        print(f\"[BIS]   Texture not found: {color_ref}\", flush=True)");
            writer.WriteLine("        return");
            writer.WriteLine("    tex_path, tex_type = result");
            writer.WriteLine();
            writer.WriteLine("    img = load_image(tex_path, tex_type)");
            writer.WriteLine("    if not img:");
            writer.WriteLine("        print(f\"[BIS]   Failed to load: {tex_path}\", flush=True)");
            writer.WriteLine("        return");
            writer.WriteLine();
            writer.WriteLine("    nodes = material.node_tree.nodes");
            writer.WriteLine("    links = material.node_tree.links");
            writer.WriteLine("    # Explicitly clear existing Base Color links (headless Blender may not auto-disconnect)");
            writer.WriteLine("    for old_link in list(bsdf.inputs['Base Color'].links):");
            writer.WriteLine("        links.remove(old_link)");
            writer.WriteLine("    tex_node = nodes.new('ShaderNodeTexImage')");
            writer.WriteLine("    tex_node.image = img");
            writer.WriteLine("    tex_node.location = (-600, 400)");
            writer.WriteLine("    links.new(tex_node.outputs['Color'], bsdf.inputs['Base Color'])");
            writer.WriteLine("    print(f\"[BIS]   Textured {material.name}\", flush=True)");
            writer.WriteLine();

            // No custom lighting — using default scene lighting
            writer.WriteLine();

            // Per-model import + PBR + save block
            writer.WriteLine("def import_and_save_model(p3d_path, model_name, output_dir, model_cfg_path):");
            writer.WriteLine("    # Clear everything from previous model");
            writer.WriteLine("    bpy.ops.object.select_all(action='SELECT')");
            writer.WriteLine("    bpy.ops.object.delete()");
            writer.WriteLine("    # Clear images to free memory");
            writer.WriteLine("    for img in list(bpy.data.images):");
            writer.WriteLine("        bpy.data.images.remove(img)");
            writer.WriteLine();
            writer.WriteLine("    try:");
            writer.WriteLine("        bpy.ops.a3ob.import_p3d(filepath=p3d_path,");
            writer.WriteLine("            absolute_paths=True, enclose=True, groupby='TYPE',");
            writer.WriteLine("            additional_data={'NORMALS','PROPS','MASS','SELECTIONS','UV','MATERIALS'},");
            writer.WriteLine("            proxy_action='SEPARATE', translate_selections=True)");
            writer.WriteLine("        print(f\"[BIS] Imported {model_name}\", flush=True)");
            writer.WriteLine("    except Exception as e:");
            writer.WriteLine("        print(f\"[BIS] Failed {model_name}: {e}\", flush=True)");
            writer.WriteLine("        traceback.print_exc()");
            writer.WriteLine("        return");
            writer.WriteLine();
            writer.WriteLine("    # ─── Organize LOD collections ───");
            writer.WriteLine("    # A3OB imports with groupby='TYPE', creating collections named by LOD resolution");
            writer.WriteLine("    # Map these to human-readable Arma standard names and parent under LODs master");
            writer.WriteLine("    lod_map = {");
            writer.WriteLine("        '0.000': 'shadow_volume',");
            writer.WriteLine("        '1.000': 'view_pilot',");
            writer.WriteLine("        '2.000': 'geometry',");
            writer.WriteLine("        '3.000': 'view_cargo',");
            writer.WriteLine("        '4.000': 'view_commander',");
            writer.WriteLine("        '5.000': 'view_gunner',");
            writer.WriteLine("        '11.000': 'physx',");
            writer.WriteLine("        '12.000': 'wreck',");
            writer.WriteLine("    }");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll.name in lod_map:");
            writer.WriteLine("            coll.name = lod_map[coll.name]");
            writer.WriteLine("    # Create or find master LODs collection");
            writer.WriteLine("    lods_master = bpy.data.collections.get('LODs')");
            writer.WriteLine("    if lods_master is None:");
            writer.WriteLine("        lods_master = bpy.data.collections.new('LODs')");
            writer.WriteLine("        bpy.context.scene.collection.children.link(lods_master)");
            writer.WriteLine("    lod_names = set(lod_map.values())");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll.name in lod_names and coll.name != 'LODs':");
            writer.WriteLine("            for parent in list(coll.users_collection):");
            writer.WriteLine("                if parent.name != 'LODs':");
            writer.WriteLine("                    parent.children.unlink(coll)");
            writer.WriteLine("            lods_master.children.link(coll)");
            writer.WriteLine();
            writer.WriteLine("    # Assign diffuse texture to P3D materials");
            writer.WriteLine("    print(f\"[BIS] Assigning textures for {model_name}...\", flush=True)");
            writer.WriteLine("    tex_count = 0");
            writer.WriteLine("    for mat in bpy.data.materials:");
            writer.WriteLine("        if mat.name.startswith('P3D: ') and ' :: ' in mat.name:");
            writer.WriteLine("            parts = mat.name.split(' :: ', 1)");
            writer.WriteLine("            color_ref = parts[0].replace('P3D: ', '').strip()");
            writer.WriteLine("            assign_texture(mat, color_ref)");
            writer.WriteLine("            # Expose rvmat path as a standard Blender custom property");
            writer.WriteLine("            try:");
            writer.WriteLine("                rv = mat.a3ob_properties_material.material_path");
            writer.WriteLine("                mat[\"rvmat_path\"] = rv");
            writer.WriteLine("                print(f\"[BIS]   rvmat: {rv}\", flush=True)");
            writer.WriteLine("            except:");
            writer.WriteLine("                pass");
            writer.WriteLine("            tex_count += 1");
            writer.WriteLine("    print(f\"[BIS] Textured {tex_count} material(s)\", flush=True)");
            writer.WriteLine();
            writer.WriteLine("    # Separate mesh by material to create per-component objects");
            writer.WriteLine("    print(f\"[BIS] Separating by material...\", flush=True)");
            writer.WriteLine("    bpy.ops.object.select_all(action='DESELECT')");
            writer.WriteLine("    for obj in bpy.data.objects:");
            writer.WriteLine("        if obj.type == 'MESH':");
            writer.WriteLine("            obj.select_set(True)");
            writer.WriteLine("    if bpy.context.selected_objects:");
            writer.WriteLine("        bpy.context.view_layer.objects.active = bpy.context.selected_objects[0]");
            writer.WriteLine("        bpy.ops.object.mode_set(mode='EDIT')");
            writer.WriteLine("        bpy.ops.mesh.select_all(action='SELECT')");
            writer.WriteLine("        bpy.ops.mesh.separate(type='MATERIAL')");
            writer.WriteLine("        bpy.ops.object.mode_set(mode='OBJECT')");
            writer.WriteLine();
            writer.WriteLine("    # Rename objects based on material and move proxies to separate collection");
            writer.WriteLine("    proxy_coll = next((c for c in bpy.data.collections if c.name == 'Proxies'), None)");
            writer.WriteLine("    if proxy_coll is None:");
            writer.WriteLine("        proxy_coll = bpy.data.collections.new('Proxies')");
            writer.WriteLine("        bpy.context.scene.collection.children.link(proxy_coll)");
            writer.WriteLine();
            writer.WriteLine("    for obj in list(bpy.data.objects):");
            writer.WriteLine("        if obj.type != 'MESH':");
            writer.WriteLine("            continue");
            writer.WriteLine("        if not obj.material_slots or not obj.material_slots[0].material:");
            writer.WriteLine("            continue");
            writer.WriteLine();
            writer.WriteLine("        mat = obj.material_slots[0].material");
            writer.WriteLine("        is_proxy = False");
            writer.WriteLine();
            writer.WriteLine("        if mat.name.startswith('P3D: '):");
            writer.WriteLine("            if 'no material' in mat.name.lower():");
            writer.WriteLine("                is_proxy = True");
            writer.WriteLine("            elif ' :: ' in mat.name:");
            writer.WriteLine("                parts = mat.name.split(' :: ', 1)");
            writer.WriteLine("                color_ref = parts[0].replace('P3D: ', '').strip()");
            writer.WriteLine("                tex_name = os.path.splitext(os.path.basename(color_ref))[0]");
            writer.WriteLine();
            writer.WriteLine("                # Check for proxy keywords in texture name");
            writer.WriteLine("                tex_lower = tex_name.lower()");
            writer.WriteLine("                if any(kw in tex_lower for kw in ['proxy', 'pilot']):");
            writer.WriteLine("                    is_proxy = True");
            writer.WriteLine();
            writer.WriteLine("                if not is_proxy:");
            writer.WriteLine("                    # Clean common Arma texture suffixes for readable naming");
            writer.WriteLine("                    clean = tex_name");
            writer.WriteLine("                    for sfx in ['_co', '_nohq', '_nopx', '_ca', '_gs', '_mco', '_s', '_as']:");
            writer.WriteLine("                        if clean.endswith(sfx):");
            writer.WriteLine("                            clean = clean[:-len(sfx)]");
            writer.WriteLine("                            break");
            writer.WriteLine();
            writer.WriteLine("                    # Strip obfuscated prefix (leading numbers / short codes)");
            writer.WriteLine("                    if '_' in clean:");
            writer.WriteLine("                        parts = clean.split('_', 1)");
            writer.WriteLine("                        if parts[0].isdigit() or len(parts[0]) <= 3:");
            writer.WriteLine("                            clean = parts[1]");
            writer.WriteLine();
            writer.WriteLine("                    # Strip Blender auto-suffix (e.g. '.001')");
            writer.WriteLine("                    base = obj.name");
            writer.WriteLine("                    if '.' in base and base.rsplit('.', 1)[1].isdigit():");
            writer.WriteLine("                        base = base.rsplit('.', 1)[0]");
            writer.WriteLine();
            writer.WriteLine("                    # Name: {model_name}_{component} — clean, scoped by model");
            writer.WriteLine("                    obj.name = f'{model_name}_{clean}'");
            writer.WriteLine("        else:");
            writer.WriteLine("            # Non-P3D materials are typically proxies/helpers");
            writer.WriteLine("            is_proxy = True");
            writer.WriteLine();
            writer.WriteLine("        if is_proxy:");
            writer.WriteLine("            # Unlink from all current collections and link to Proxies");
            writer.WriteLine("            for coll in list(obj.users_collection):");
            writer.WriteLine("                coll.objects.unlink(obj)");
            writer.WriteLine("            proxy_coll.objects.link(obj)");
            writer.WriteLine();
            writer.WriteLine("    obj_count = len([o for o in bpy.data.objects if o.type == 'MESH'])");
            writer.WriteLine("    proxy_count = len([o for o in proxy_coll.objects if o.type == 'MESH'])");
            writer.WriteLine("    print(f'[BIS] Organized {obj_count} objects ({proxy_count} proxies in Proxies)', flush=True)");
            if (modelCfgPath != null)
            {
                writer.WriteLine("    # Skeleton / armature");
                writer.WriteLine("    try:");
                writer.WriteLine($"        bpy.ops.a3ob.import_mcfg(filepath=r\"{modelCfgPath}\")");
                writer.WriteLine("        print(\"[BIS] Skeleton loaded from model.cfg\", flush=True)");
                writer.WriteLine("        bpy.ops.a3ob.import_armature(filepath=p3d_path, skeleton_index=0)");
                writer.WriteLine("        print(\"[BIS] Armature imported\", flush=True)");
                writer.WriteLine("    except Exception as e:");
                writer.WriteLine("        print(f\"[BIS] Skeleton/armature import skipped: {e}\", flush=True)");
                writer.WriteLine();
            }
            writer.WriteLine("    # ─── Scene cleanup & setup ───");
            writer.WriteLine("    # Delete empty LOD collections left after separation");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll.name not in ('LODs', 'Proxies') and len(coll.objects) == 0:");
            writer.WriteLine("            bpy.data.collections.remove(coll)");
            writer.WriteLine("    # Delete default Blender startup objects if present");
            writer.WriteLine("    for obj in list(bpy.data.objects):");
            writer.WriteLine("        if obj.type in {'CAMERA', 'LIGHT'} or (obj.type == 'MESH' and obj.name == 'Cube'):");
            writer.WriteLine("            bpy.data.objects.remove(obj, do_unlink=True)");
            writer.WriteLine("    # Set scene units to METRIC (1 unit = 1 meter, Arma 3 standard)");
            writer.WriteLine("    bpy.context.scene.unit_settings.system = 'METRIC'");
            writer.WriteLine("    bpy.context.scene.unit_settings.scale_length = 1.0");
            writer.WriteLine("    # Set viewport to Material Preview for texture display (persists in .blend)");
            writer.WriteLine("    for screen in bpy.data.screens:");
            writer.WriteLine("        for area in screen.areas:");
            writer.WriteLine("            if area.type == 'VIEW_3D':");
            writer.WriteLine("                area.spaces[0].shading.type = 'MATERIAL'");
            writer.WriteLine();
            writer.WriteLine("    # Save individual .blend file");
            writer.WriteLine("    blend_path = os.path.join(output_dir, f\"{model_name}.blend\")");
            writer.WriteLine("    bpy.ops.wm.save_as_mainfile(filepath=blend_path)");
            writer.WriteLine("    print(f\"[BIS] Saved {model_name}.blend\", flush=True)");
            writer.WriteLine();
        }

        /// <summary>
        /// Generates a single batch Blender Python script that imports multiple models sequentially.
        /// </summary>
        private static string GenerateBatchScript(List<(string ModelName, string P3DPath)> models,
            string outputDir, string imageDictLiteral, string? modelCfgPath)
        {
            string batchName = $"batch_{models[0].ModelName}_{models[^1].ModelName}";
            string scriptPath = Path.Combine(outputDir, $"import_{batchName}.py");

            using var writer = new StreamWriter(scriptPath);
            writer.WriteLine("import bpy");
            writer.WriteLine("import os");
            writer.WriteLine("import sys");
            writer.WriteLine("import traceback");
            writer.WriteLine();

            writer.WriteLine("# Enable addon (once per batch)");
            writer.WriteLine("bpy.ops.preferences.addon_enable(module=\"bl_ext.blender_org.Arma3ObjectBuilder\")");
            writer.WriteLine();

            writer.WriteLine("# Pre-computed texture dictionary (built by C#, no filesystem walk needed)");
            writer.WriteLine(imageDictLiteral);
            writer.WriteLine();

            // Write all shared helper functions
            WriteHelperFunctions(writer, modelCfgPath);

            // For each model in the batch, call the import function
            foreach (var (modelName, p3dPath) in models)
            {
                string pyP3DPath = p3dPath.Replace("\\", "/");
                string pyOutDir = outputDir.Replace("\\", "/");
                writer.WriteLine($"import_and_save_model(r\"{pyP3DPath}\", \"{modelName}\", r\"{pyOutDir}\", { (modelCfgPath != null ? "r\"" + modelCfgPath.Replace("\\", "/") + "\"" : "None") })");
            }

            return scriptPath;
        }

        /// <summary>
        /// Generates batch scripts, each importing a subset of P3D models sequentially in one Blender process.
        /// Returns list of batch script paths.
        /// </summary>
        /// <param name="extractedDir">Directory containing extracted PBO files</param>
        /// <param name="outputDir">Directory for .blend output</param>
        /// <param name="texturesDir">Optional PNG texture directory (pre-converted from PAA)</param>
        /// <param name="batchCount">Number of batches (also the concurrency level for Blender processes)</param>
        /// <param name="modelCfgPath">Optional model.cfg path for skeleton/armature import</param>
        public static List<string> GenerateAllBatchScripts(string extractedDir, string outputDir,
            string? texturesDir, int batchCount, string? modelCfgPath = null)
        {
            Directory.CreateDirectory(outputDir);

            var allP3Ds = Directory.GetFiles(extractedDir, "*.p3d", SearchOption.AllDirectories);
            var validModels = new List<(string Name, string Path)>();  // Renamed to avoid member name conflict

            // Filter out models with empty LODs (addon hangs)
            foreach (var p3dPath in allP3Ds)
            {
                string modelName = Path.GetFileNameWithoutExtension(p3dPath);
                if (HasEmptyLods(p3dPath))
                {
                    Console.WriteLine($"  Skipping {modelName}: has empty view LOD (addon hangs)");
                    continue;
                }
                validModels.Add((modelName, p3dPath));
            }

            if (validModels.Count == 0)
            {
                Console.WriteLine("  No valid .p3d files found. Skipping Blender export.");
                return new List<string>();
            }

            // Build image dict once (shared across all batch scripts)
            string imageDictLiteral = BuildImageDictLiteral(extractedDir, texturesDir);

            // Split into batches (round-robin distribution for balanced workload)
            var batches = new List<List<(string Name, string Path)>>();
            for (int i = 0; i < batchCount; i++)
                batches.Add(new List<(string Name, string Path)>());

            for (int i = 0; i < validModels.Count; i++)
                batches[i % batchCount].Add(validModels[i]);

            // Remove empty batches
            batches.RemoveAll(b => b.Count == 0);

            // Resolve model.cfg path
            if (modelCfgPath == null)
                modelCfgPath = FindModelCfg(extractedDir);

            // Generate one batch script per batch
            var batchScripts = new List<string>();
            foreach (var batch in batches)
            {
                // Convert to expected tuple format for GenerateBatchScript
                var batchTuples = batch.Select(m => (m.Name, m.Path)).ToList();
                string scriptPath = GenerateBatchScript(batchTuples, outputDir, imageDictLiteral, modelCfgPath);
                batchScripts.Add(scriptPath);
                Console.WriteLine($"  Generated batch script: {Path.GetFileName(scriptPath)} ({batch.Count} models)");
            }

            Console.WriteLine($"  Generated {batchScripts.Count} batch script(s) for {validModels.Count} model(s)");
            return batchScripts;
        }

        /// <summary>
        /// Generates a single-model batch script for a standalone .p3d file.
        /// Textures are scanned from extractRoot (which should contain both the model
        /// and its data/ texture directories).
        /// </summary>
        /// <param name="p3dPath">Full path to the .p3d file to export</param>
        /// <param name="extractRoot">Root directory containing textures (and optionally model.cfg)</param>
        /// <param name="outputDir">Directory for the generated .blend file</param>
        /// <param name="texturesDir">Optional PNG texture directory (pre-converted from PAA)</param>
        /// <returns>Path to the generated batch Python script</returns>
        public static string GenerateSingleModelScript(string p3dPath, string extractRoot, string outputDir, string? texturesDir = null)
        {
            Directory.CreateDirectory(outputDir);

            string modelName = Path.GetFileNameWithoutExtension(p3dPath);
            string imageDictLiteral = BuildImageDictLiteral(extractRoot, texturesDir);
            string? modelCfg = FindModelCfg(extractRoot);

            var models = new List<(string Name, string Path)>
            {
                (modelName, Path.GetFullPath(p3dPath))
            };

            return GenerateBatchScript(models, outputDir, imageDictLiteral, modelCfg);
        }
    }
}
