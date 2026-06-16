using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIS.Core.Config;
using BIS.Core.Streams;
using BIS.P3D.ODOL;

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
        /// Checks convention paths first (_skeleton/model.cfg), then falls back
        /// to scanning all subdirectories.
        /// </summary>
        public static string? FindModelCfg(string extractedDir)
        {
            // Convention: _skeleton/model.cfg (user-placed, parallels _blender/_textures)
            var skeletonDir = Path.Combine(extractedDir, "_skeleton");
            if (Directory.Exists(skeletonDir))
            {
                var cfgInSkeleton = Path.Combine(skeletonDir, "model.cfg");
                if (File.Exists(cfgInSkeleton))
                    return cfgInSkeleton;

                foreach (var f in Directory.GetFiles(skeletonDir, "*.cfg", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(f);
                    if (name.Equals("model.cfg", StringComparison.OrdinalIgnoreCase))
                        return f;
                }
            }

            // Fallback: scan all subdirectories
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
        private static bool HasEmptyViewLod(string p3dPath)
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

        private static string SanitizeP3D(string p3dPath)
        {
            try
            {
                using var stream = File.OpenRead(p3dPath);
                var p3d = new P3D(stream);
                if (!p3d.LODs.Any(lod => lod.VertexCount == 0))
                    return p3dPath;

                string dir = Path.GetDirectoryName(p3dPath) ?? ".";
                string name = Path.GetFileNameWithoutExtension(p3dPath);
                string sanitizedPath = Path.Combine(dir, $"{name}.sanitized.p3d");

                if (p3d.IsMLODFormat)
                {
                    var cleanLods = p3d.MLOD.Lods
                        .Where(lod => lod.Points.Length > 0)
                        .ToArray();
                    var sanitized = new BIS.P3D.MLOD.MLOD(cleanLods);
                    sanitized.WriteToFile(sanitizedPath, allowOverwriting: true);
                }
                else if (p3d.IsODOLFormat)
                {
                    var cleanLods = p3d.ODOL.Lods
                        .Where(lod => lod.VertexCount > 0)
                        .ToArray();
                    p3d.ODOL.Lods = cleanLods;
                    p3d.Write(new BinaryWriterEx(File.Create(sanitizedPath)));
                }
                else
                {
                    return p3dPath;
                }

                Console.WriteLine($"  Sanitized {name}: removed empty LODs -> {sanitizedPath}");
                return sanitizedPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: Failed to sanitize {p3dPath}: {ex.Message}. Using original.");
                return p3dPath;
            }
        }

        /// <summary>
        /// Builds a Python dict literal mapping rvmat paths to named selection names
        /// from the P3D model file. This gives us proper naming like "radio" instead of "misc"
        /// for objects that correspond to named selections.
        /// Returns "None" if no mapping can be built (graceful fallback).
        /// </summary>
        private static string BuildSelectionMapLiteral(string p3dPath)
        {
            try
            {
                using var stream = File.OpenRead(p3dPath);
                var p3d = new P3D(stream);

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                if (p3d.IsMLODFormat)
                {
                    // ILevelOfDetail.NamedSelections has a Select((_, i) => lod.Faces[i]) bug in MLOD
                    var mlod = p3d.MLOD;
                    if (mlod?.Lods == null) { Console.WriteLine($"[BIS sel_map] MLOD has no LODs"); return "None"; }
                    Console.WriteLine($"[BIS sel_map] MLOD format, {mlod.Lods.Length} LODs");

                    foreach (var lod in mlod.Lods.OrderByDescending(l => l.Resolution))
                    {
                        Console.WriteLine($"[BIS sel_map]   LOD res={lod.Resolution}, faces={lod.Faces?.Length ?? 0}");

                        int allTaggs = lod.Taggs?.Count ?? 0;
                        var selTaggs = lod.Taggs?.OfType<MLOD.NamedSelectionTagg>().ToList();
                        Console.WriteLine($"[BIS sel_map]     Total taggs: {allTaggs}, NamedSelection: {selTaggs?.Count ?? 0}");
                        if (selTaggs == null || selTaggs.Count == 0)
                        {
                            if (lod.Taggs != null)
                            {
                                foreach (var t in lod.Taggs.Take(20))
                                    Console.WriteLine($"[BIS sel_map]       Tagg: type={t.GetType().Name}, name='{t.Name}'");
                            }
                            continue;
                        }
                        Console.WriteLine($"[BIS sel_map]     {selTaggs.Count} NamedSelection taggs");

                        foreach (var nst in selTaggs)
                        {
                            if (string.IsNullOrEmpty(nst.Name)) continue;

                            // byte array is membership bitmap: index=face idx, value=0=not selected, non-0=selected
                            int nonZeroFaces = nst.Faces?.Count(b => b != 0) ?? 0;
                            int nonZeroPoints = nst.Points?.Count(b => b != 0) ?? 0;
                            Console.WriteLine($"[BIS sel_map]     NS '{nst.Name}': non-zero faces={nonZeroFaces}, points={nonZeroPoints}, faceArrLen={nst.Faces?.Length ?? 0}, pointArrLen={nst.Points?.Length ?? 0}");

                            var faceIndices = nst.Faces?.Select((b, i) => new { b, i })
                                .Where(x => x.b != 0).Select(x => x.i).ToList();
                            if (faceIndices == null || faceIndices.Count == 0)
                            {
                                continue;
                            }

                            var materials = faceIndices
                                .Where(i => i >= 0 && i < lod.Faces.Length)
                                .Select(i => lod.Faces[i].Material)
                                .Where(m => !string.IsNullOrEmpty(m))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (materials.Count == 1)
                            {
                                string key = materials[0].Replace("\\", "/").ToLowerInvariant();
                                Console.WriteLine($"[BIS sel_map]     NS '{nst.Name}' -> '{key}'");
                                if (!map.ContainsKey(key) || nst.Name.Length > map[key].Length)
                                    map[key] = nst.Name;
                            }
                            else
                            {
                                Console.WriteLine($"[BIS sel_map]     NS '{nst.Name}': multi-material ({materials.Count}) or no material");
                            }
                        }
                    }
                }
                else if (p3d.IsODOLFormat)
                {
                    Console.WriteLine($"[BIS sel_map] ODOL format");
                    foreach (var lod in p3d.LODs.OrderByDescending(l => l.Resolution))
                    {
                        if (lod.Resolution < 1e8) continue;
                        var selections = lod.NamedSelections?.ToList();
                        if (selections == null || selections.Count == 0) continue;
                        Console.WriteLine($"[BIS sel_map]   LOD res={lod.Resolution}: {selections.Count} selections");

                        foreach (var ns in selections)
                        {
                            if (string.IsNullOrEmpty(ns.Name)) continue;
                            string? material = ns.Material;
                            if (string.IsNullOrEmpty(material)) continue;
                            string key = material.Replace("\\", "/").ToLowerInvariant();
                            if (!map.ContainsKey(key) || ns.Name.Length > map[key].Length)
                                map[key] = ns.Name;
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[BIS sel_map] Unknown P3D format");
                    return "None";
                }

                Console.WriteLine($"[BIS sel_map] Total entries: {map.Count}");
                if (map.Count == 0) return "None";

                var sb = new StringBuilder();
                sb.Append('{');
                bool first = true;
                foreach (var kv in map)
                {
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append($"r\"{kv.Key}\": \"{kv.Value}\"");
                }
                sb.Append('}');
                Console.WriteLine($"[BIS sel_map] Result: {sb}");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIS sel_map] Exception: {ex.Message}");
                return "None";
            }
        }

        /// <summary>
        /// Parses a config.cpp file and builds a Python dict literal mapping model filenames
        /// to their displayName, hiddenSelections, and baseClassName.
        /// Returns "None" if config.cpp can't be found or parsed (graceful fallback).
        /// </summary>
        private static string BuildConfigDataLiteral(string configCppPath)
        {
            try
            {
                if (!File.Exists(configCppPath))
                {
                    Console.WriteLine("[BIS cfg] config.cpp not found");
                    return "None";
                }

                var parser = new ConfigParser();
                var config = parser.ParseFile(configCppPath);
                var cfgVehicles = config.Root.GetClass("CfgVehicles");

                if (cfgVehicles == null)
                {
                    Console.WriteLine("[BIS cfg] CfgVehicles not found in config.cpp");
                    return "None";
                }

                var modelMap = new Dictionary<string, (string displayName, string baseClass, string[] hiddenSelections)>(StringComparer.OrdinalIgnoreCase);
                int classCount = 0, matchedCount = 0;

                foreach (var entry in cfgVehicles.Entries)
                {
                    if (entry is ParamClass cls && !string.IsNullOrEmpty(cls.Name))
                    {
                        classCount++;
                        string? model = cls.GetValue<string>("model", null);
                        if (string.IsNullOrEmpty(model)) continue;

                        // Model paths are typically "model/filename.p3d" or "filename.p3d"
                        string modelName = Path.GetFileNameWithoutExtension(model.Replace('\\', '/'));
                        if (string.IsNullOrEmpty(modelName)) continue;

                        string displayName = cls.GetValue<string>("displayName", modelName);
                        var hsArray = cls.GetArray<string>("hiddenSelections");
                        string[] hiddenSelections = hsArray ?? Array.Empty<string>();
                        string baseClass = cls.BaseClassName ?? "";

                        // Keep the entry with the shortest model name match
                        // (some classes have model="j_avs" vs model="model/jsoar_avs_mc_blk.p3d")
                        if (!modelMap.ContainsKey(modelName) || model.Contains("model/"))
                            modelMap[modelName] = (displayName, baseClass, hiddenSelections);
                        matchedCount++;
                    }
                }

                Console.WriteLine($"[BIS cfg] Scanned {classCount} CfgVehicles classes, matched {modelMap.Count} unique models");

                if (modelMap.Count == 0) return "None";

                var sb = new StringBuilder();
                sb.Append('{');
                bool first = true;
                foreach (var kv in modelMap)
                {
                    if (!first) sb.Append(", ");
                    first = false;

                    string dn = kv.Value.displayName.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    string bc = kv.Value.baseClass.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    var hs = kv.Value.hiddenSelections;

                    sb.Append($"\"{kv.Key}\": {{\"dn\": \"{dn}\", \"bc\": \"{bc}\", \"hs\": [");
                    for (int i = 0; i < hs.Length; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append($"\"{hs[i].Replace("\\", "\\\\").Replace("\"", "\\\"")}\"");
                    }
                    sb.Append("]}");
                }
                sb.Append('}');

                Console.WriteLine($"[BIS cfg] Config data built for {modelMap.Count} models");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BIS cfg] Failed to parse config.cpp: {ex.Message}");
                return "None";
            }
        }

        /// <summary>
        /// Builds a texture dictionary in C# as a Python dict literal string.
        /// Scans the extracted dir for PAAs (primary, via armaio) and PNGs (fallback).
        /// PAA entries take priority over PNGs for same-named files.
        /// Returns: string containing the Python dict literal with all image entries.
        /// </summary>
        private static string BuildImageDictLiteral(string extractedDir, string? texturesDir)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;

            foreach (var f in Directory.GetFiles(extractedDir, "*.png", SearchOption.AllDirectories))
            {
                string key = Path.GetFileName(f).ToLowerInvariant();
                if (!dict.ContainsKey(key))
                {
                    dict[key] = f;
                    count++;
                }
                string relKey = GetRelativePath(extractedDir, f).Replace('\\', '/').ToLowerInvariant();
                if (!dict.ContainsKey(relKey))
                    dict[relKey] = f;
            }

            if (texturesDir != null && Directory.Exists(texturesDir))
            {
                foreach (var f in Directory.GetFiles(texturesDir, "*.png", SearchOption.AllDirectories))
                {
                    string key = Path.GetFileName(f).ToLowerInvariant();
                    if (!dict.ContainsKey(key))
                        dict[key] = f;
                    string relKey = GetRelativePath(texturesDir, f).Replace('\\', '/').ToLowerInvariant();
                    if (!dict.ContainsKey(relKey))
                        dict[relKey] = f;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("image_dict = {");
            foreach (var kv in dict)
            {
                // Use raw string r"..." for paths (handles backslashes on Windows)
                string pyPath = kv.Value.Replace("\\", "/");
                sb.AppendLine($"    \"{kv.Key}\": r\"{pyPath}\",");
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
            writer.WriteLine("    without_ext = os.path.splitext(tex_ref_clean)[0]");
            writer.WriteLine("    if rvmat_dir:");
            writer.WriteLine("        full_key = f\"{rvmat_dir}/{without_ext}.png\".lower()");
            writer.WriteLine("        if full_key in image_dict:");
            writer.WriteLine("            return image_dict[full_key]");
            writer.WriteLine("    basename = os.path.basename(without_ext) + '.png'");
            writer.WriteLine("    if basename.lower() in image_dict:");
            writer.WriteLine("        return image_dict[basename.lower()]");
            writer.WriteLine("    return None");
            writer.WriteLine();

            writer.WriteLine("def load_image(path):");
            writer.WriteLine("    img_name = os.path.basename(path)");
            writer.WriteLine("    old = bpy.data.images.get(img_name)");
            writer.WriteLine("    if old:");
            writer.WriteLine("        old.name = f\"__old_{img_name}\"");
            writer.WriteLine("    try:");
            writer.WriteLine("        img = bpy.data.images.load(path)");
            writer.WriteLine("        if not img.has_data:");
            writer.WriteLine("            img.pack()");
            writer.WriteLine("        print(f\"[BIS]   Loaded {img_name}\", flush=True)");
            writer.WriteLine("        return img");
            writer.WriteLine("    except Exception as e:");
            writer.WriteLine("        print(f\"[BIS]   Failed to load {path}: {e}\", flush=True)");
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
            writer.WriteLine("    tex_path = resolve_tex(color_ref, rvmat_dir)");
            writer.WriteLine("    if not tex_path:");
            writer.WriteLine("        print(f\"[BIS]   Texture not found: {color_ref}\", flush=True)");
            writer.WriteLine("        return");
            writer.WriteLine();
            writer.WriteLine("    img = load_image(tex_path)");
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
            writer.WriteLine("def import_and_save_model(p3d_path, model_name, output_dir, model_cfg_path, sel_map=None, cfg_data=None):");
            writer.WriteLine("    # Clear everything from previous model (objects, images, collections)");
            writer.WriteLine("    bpy.ops.object.select_all(action='SELECT')");
            writer.WriteLine("    bpy.ops.object.delete()");
            writer.WriteLine("    for img in list(bpy.data.images):");
            writer.WriteLine("        bpy.data.images.remove(img)");
            writer.WriteLine("    # Remove stale collections (keep master scene collection)");
            writer.WriteLine("    master = bpy.context.scene.collection");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll != master and coll.name != 'Collection':");
            writer.WriteLine("            bpy.data.collections.remove(coll, do_unlink=True)");
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
            writer.WriteLine("    # A3OB v2.5 imports with groupby='TYPE', creating collections");
            writer.WriteLine("    # by type group: \"Visuals\", \"Shadows\", \"Geometries\", etc.");
            writer.WriteLine("    # Map type groups to standard LOD names and parent under LODs master.");
            writer.WriteLine("    lod_type_names = {");
            writer.WriteLine("        'Visuals': 'view',");
            writer.WriteLine("        'Shadows': 'shadow_volume',");
            writer.WriteLine("        'Geometries': 'geometry',");
            writer.WriteLine("        'Point clouds': 'point_cloud',");
            writer.WriteLine("        'PhysX': 'physx',");
            writer.WriteLine("        'Wreck': 'wreck',");
            writer.WriteLine("        'Misc': 'misc',");
            writer.WriteLine("    }");
            writer.WriteLine("    lods_master = bpy.data.collections.get('LODs')");
            writer.WriteLine("    if lods_master is None:");
            writer.WriteLine("        lods_master = bpy.data.collections.new('LODs')");
            writer.WriteLine("        bpy.context.scene.collection.children.link(lods_master)");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll.name in lod_type_names:");
            writer.WriteLine("            coll.name = lod_type_names[coll.name]");
            writer.WriteLine("            for parent in bpy.data.collections:");
            writer.WriteLine("                if parent.name != 'LODs' and parent.children.get(coll.name) is not None:");
            writer.WriteLine("                    parent.children.unlink(coll)");
            writer.WriteLine("                    break");
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
            writer.WriteLine();
            writer.WriteLine("    # Separate mesh by material to create per-component objects");
            writer.WriteLine("    # (process each mesh individually to avoid view-layer issues)");
            writer.WriteLine("    print(f\"[BIS] Separating by material...\", flush=True)");
            writer.WriteLine("    for obj in list(bpy.data.objects):");
            writer.WriteLine("        if obj.type != 'MESH' or len(obj.material_slots) <= 1:");
            writer.WriteLine("            continue");
            writer.WriteLine("        try:");
            writer.WriteLine("            bpy.ops.object.select_all(action='DESELECT')");
            writer.WriteLine("            obj.hide_set(False)");
            writer.WriteLine("            obj.select_set(True)");
            writer.WriteLine("            bpy.context.view_layer.objects.active = obj");
            writer.WriteLine("            bpy.ops.object.mode_set(mode='EDIT')");
            writer.WriteLine("            bpy.ops.mesh.select_all(action='SELECT')");
            writer.WriteLine("            bpy.ops.mesh.separate(type='MATERIAL')");
            writer.WriteLine("            bpy.ops.object.mode_set(mode='OBJECT')");
            writer.WriteLine("        except Exception as e:");
            writer.WriteLine("            print(f\"[BIS]   Skip separate for {obj.name}: {e}\", flush=True)");
            writer.WriteLine();
            writer.WriteLine("    # ─── Naming helpers ───");
            writer.WriteLine("    import re as _re");
            writer.WriteLine("    _brand_prefixes = ['jsoar_', 'adams_', 'avs_', 'sma_', 'cwr_', 'rhs_', 'cup_', 'ace_', 'cba_', 'ifa_', 'uns_', 'vg_', 'tfa_', 'uksf_', 'niarms_', 'vsm_', 'mec_', 'task_']");
            writer.WriteLine("    def _strip_brand(n):");
            writer.WriteLine("        low = n.lower()");
            writer.WriteLine("        for p in _brand_prefixes:");
            writer.WriteLine("            if low.startswith(p):");
            writer.WriteLine("                return n[len(p):]");
            writer.WriteLine("        return n");
            writer.WriteLine("    def _contract_variant(part):");
            writer.WriteLine("        return _re.sub(r'_(?=\\d)', '', part)");
            writer.WriteLine("    raw_variant = _strip_brand(model_name)");
            writer.WriteLine("    variant = _contract_variant(raw_variant)");
            writer.WriteLine();
            writer.WriteLine("    # ─── Config class enrichment ───");
            writer.WriteLine("    # Build a human-readable type prefix from the config.cpp base class.");
            writer.WriteLine("    # E.g., H_HelmetB -> Helmet, V_CarrierRig -> Vest, B_AssaultPack -> Backpack");
            writer.WriteLine("    _class_type_map = {");
            writer.WriteLine("        'h_': 'Helmet', 'helmet': 'Helmet',");
            writer.WriteLine("        'v_': 'Vest', 'vest': 'Vest',");
            writer.WriteLine("        'u_': 'Uniform', 'uniform': 'Uniform',");
            writer.WriteLine("        'b_': 'Backpack', 'backpack': 'Backpack', 'bag': 'Backpack',");
            writer.WriteLine("        'itemcore': 'Item', 'item': 'Item',");
            writer.WriteLine("        'weapon': 'Weapon', 'rifle': 'Rifle', 'launcher': 'Launcher',");
            writer.WriteLine("        'optic': 'Optic', 'attachment': 'Attachment',");
            writer.WriteLine("        'ammo': 'Ammo', 'magazine': 'Magazine',");
            writer.WriteLine("        'food': 'Food', 'drink': 'Drink', 'medical': 'Medical',");
            writer.WriteLine("    }");
            writer.WriteLine("    def _class_prefix(bc):");
            writer.WriteLine("        if not bc:");
            writer.WriteLine("            return ''");
            writer.WriteLine("        bc_low = bc.lower().replace('-', '_').replace(' ', '_')");
            writer.WriteLine("        for prefix, label in _class_type_map.items():");
            writer.WriteLine("            if bc_low.startswith(prefix) or prefix in bc_low:");
            writer.WriteLine("                return label");
            writer.WriteLine("        # Fallback: first capitalized segment");
            writer.WriteLine("        parts = bc.split('_')");
            writer.WriteLine("        if parts:");
            writer.WriteLine("            return parts[0].capitalize()");
            writer.WriteLine("        return ''");
            writer.WriteLine("    cfg_label = ''");
            writer.WriteLine("    if cfg_data:");
            writer.WriteLine("        model_key = os.path.splitext(os.path.basename(p3d_path))[0].lower()");
            writer.WriteLine("        cfg_entry = cfg_data.get(model_key)");
            writer.WriteLine("        if cfg_entry:");
            writer.WriteLine("            cfg_label = _class_prefix(cfg_entry.get('bc', ''))");
            writer.WriteLine("            print(f\"[BIS]   Config: {cfg_entry.get('dn', model_key)} ({cfg_entry.get('bc', '?')})\", flush=True)");
            writer.WriteLine("            if cfg_label and cfg_label.lower() not in variant.lower():");
            writer.WriteLine("                variant = f'{cfg_label}_{variant}'");
            writer.WriteLine();
            writer.WriteLine("    # ─── LOD type helper ───");
            writer.WriteLine("    _lod_tags = {");
            writer.WriteLine("        'View - Pilot': 'VP',");
            writer.WriteLine("        'View - Cargo': 'VC',");
            writer.WriteLine("        'View - Commander': 'VCOM',");
            writer.WriteLine("        'View - Gunner': 'VG',");
            writer.WriteLine("        'View - Pilot - Interior': 'VPI',");
            writer.WriteLine("        'Resolution 0': 'R0',");
            writer.WriteLine("        'Resolution 1': 'R1',");
            writer.WriteLine("        'Resolution 2': 'R2',");
            writer.WriteLine("        'Resolution 3': 'R3',");
            writer.WriteLine("        'ShadowVolume': 'SV',");
            writer.WriteLine("        'ShadowVolumeNonSelf': 'SVN',");
            writer.WriteLine("        'Geometry': 'GEO',");
            writer.WriteLine("        'GeometryInterior': 'GEOI',");
            writer.WriteLine("        'GeometryInteriorCollision': 'GEOIC',");
            writer.WriteLine("        'HitPoints': 'HP',");
            writer.WriteLine("        'PhysX': 'PHY',");
            writer.WriteLine("        'FireGeometry': 'FG',");
            writer.WriteLine("        'LandContact': 'LC',");
            writer.WriteLine("        'Roadway': 'RW',");
            writer.WriteLine("        'Wreck': 'WRK',");
            writer.WriteLine("        # Numeric A3OB LOD enum identifiers (actual values from a3ob_properties_object.lod)");
            writer.WriteLine("        '0': 'R0',");
            writer.WriteLine("        '1': 'VG',");
            writer.WriteLine("        '2': 'VP',");
            writer.WriteLine("        '3': 'VC',");
            writer.WriteLine("        '4': 'SV',");
            writer.WriteLine("        '5': 'EDIT',");
            writer.WriteLine("        '6': 'GEO',");
            writer.WriteLine("        '7': 'GEOB',");
            writer.WriteLine("        '8': 'GEOP',");
            writer.WriteLine("        '9': 'MEM',");
            writer.WriteLine("        '10': 'LC',");
            writer.WriteLine("        '11': 'RW',");
            writer.WriteLine("        '12': 'PATH',");
            writer.WriteLine("        '13': 'HP',");
            writer.WriteLine("        '14': 'VWG',");
            writer.WriteLine("        '15': 'FG',");
            writer.WriteLine("        '16': 'VCG',");
            writer.WriteLine("        '17': 'VCFG',");
            writer.WriteLine("        '18': 'VCOM',");
            writer.WriteLine("        '19': 'VCOMG',");
            writer.WriteLine("        '20': 'VCOMFG',");
            writer.WriteLine("        '21': 'VPG',");
            writer.WriteLine("        '22': 'VPFG',");
            writer.WriteLine("        '23': 'VGG',");
            writer.WriteLine("        '24': 'VGFG',");
            writer.WriteLine("        '25': 'SUB',");
            writer.WriteLine("        '26': 'SVC',");
            writer.WriteLine("        '27': 'SVP',");
            writer.WriteLine("        '28': 'SVG',");
            writer.WriteLine("        '29': 'WRK',");
            writer.WriteLine("        '30': 'UG',");
            writer.WriteLine("        '31': 'GL',");
            writer.WriteLine("        '32': 'NAV',");
            writer.WriteLine("    }");
            writer.WriteLine("    # Map A3OB LOD enum values to parent collection name (view/shadow_volume/geometry)");
            writer.WriteLine("    _lod_groups = {");
            writer.WriteLine("        '0': 'view', '1': 'view', '2': 'view', '3': 'view', '18': 'view',");
            writer.WriteLine("        '4': 'shadow_volume', '26': 'shadow_volume', '27': 'shadow_volume', '28': 'shadow_volume',");
            writer.WriteLine("        '6': 'geometry', '7': 'geometry', '8': 'geometry', '14': 'geometry',");
            writer.WriteLine("        '15': 'geometry', '16': 'geometry', '17': 'geometry', '19': 'geometry',");
            writer.WriteLine("        '20': 'geometry', '21': 'geometry', '22': 'geometry', '23': 'geometry',");
            writer.WriteLine("        '24': 'geometry', '30': 'geometry',");
            writer.WriteLine("    }");
            writer.WriteLine("    def _lod_tag(obj):");
            writer.WriteLine("        try:");
            writer.WriteLine("            lod_name = str(obj.a3ob_properties_object.lod)");
            writer.WriteLine("        except:");
            writer.WriteLine("            lod_name = None");
            writer.WriteLine("        if not lod_name:");
            writer.WriteLine("            return ''");
            writer.WriteLine("        # Priority 1: A3OB lod name -> compact tag (e.g. '0' -> 'R0', '2' -> 'VP')");
            writer.WriteLine("        tag = _lod_tags.get(lod_name)");
            writer.WriteLine("        if tag:");
            writer.WriteLine("            return f'_{tag}'");
            writer.WriteLine("        # Priority 2: parent collection name as fallback");
            writer.WriteLine("        for coll in obj.users_collection:");
            writer.WriteLine("            cname = coll.name");
            writer.WriteLine("            if cname in ('view','shadow_volume','geometry'):");
            writer.WriteLine("                return f'_{cname}'");
            writer.WriteLine("        # Priority 3: shorten the raw lod name");
            writer.WriteLine("        short = lod_name.replace(' ', '').replace('-','')[:8]");
            writer.WriteLine("        if short:");
            writer.WriteLine("            return f'_{short}'");
            writer.WriteLine("        return ''");
            writer.WriteLine("    def _lod_group(lod_name):");
            writer.WriteLine("        \"\"\"Map A3OB LOD enum value to parent collection name.\"\"\"");
            writer.WriteLine("        if not lod_name:");
            writer.WriteLine("            return None");
            writer.WriteLine("        return _lod_groups.get(str(lod_name))");
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
            writer.WriteLine("        try:");
            writer.WriteLine("            mat = obj.material_slots[0].material");
            writer.WriteLine("            is_proxy = False");
            writer.WriteLine();
            writer.WriteLine("            if mat.name.startswith('P3D: '):");
            writer.WriteLine("                if 'no material' in mat.name.lower():");
            writer.WriteLine("                    is_proxy = True");
            writer.WriteLine("                elif ' :: ' in mat.name:");
            writer.WriteLine("                    parts = mat.name.split(' :: ', 1)");
            writer.WriteLine("                    color_ref = parts[0].replace('P3D: ', '').strip()");
            writer.WriteLine("                    tex_name = os.path.splitext(ntpath.basename(color_ref))[0]");
            writer.WriteLine();
            writer.WriteLine("                    # Check for proxy keywords in texture name");
            writer.WriteLine("                    tex_lower = tex_name.lower()");
            writer.WriteLine("                    if any(kw in tex_lower for kw in ['proxy', 'pilot']):");
            writer.WriteLine("                        is_proxy = True");
            writer.WriteLine();
            writer.WriteLine("                    if not is_proxy:");
                        writer.WriteLine("                        # Priority 1: Named selection mapping (from P3D selections)");
                        writer.WriteLine("                        try:");
                        writer.WriteLine("                            rv_path = mat.a3ob_properties_material.material_path");
                        writer.WriteLine("                            if rv_path and sel_map:");
                        writer.WriteLine("                                rv_key = rv_path.replace('\\\\', '/').lower()");
                        writer.WriteLine("                                sel_name = sel_map.get(rv_key)");
                        writer.WriteLine("                                # Fallback: filename-only match (P3D may store just filename)");
                        writer.WriteLine("                                if not sel_name:");
                        writer.WriteLine("                                    import os as _os");
                        writer.WriteLine("                                    rv_base = _os.path.basename(rv_key).lower()");
                        writer.WriteLine("                                    for _k, _v in sel_map.items():");
                        writer.WriteLine("                                        if _k.endswith('/' + rv_base) or _k == rv_base:");
                        writer.WriteLine("                                            sel_name = _v");
                        writer.WriteLine("                                            break");
                        writer.WriteLine("                                if sel_name:");
                        writer.WriteLine("                                    obj.name = f'{sel_name}_{variant}{_lod_tag(obj)}'");
                        writer.WriteLine("                                    print(f\"[BIS]   Named '{obj.name}' from selection ({sel_name})\", flush=True)");
                        writer.WriteLine("                                    continue");
                        writer.WriteLine("                        except:");
                        writer.WriteLine("                            pass");
            writer.WriteLine("                        # Priority 2: RVMAT directory (e.g., data\\mags\\mags.rvmat -> mags)");
            writer.WriteLine("                        try:");
            writer.WriteLine("                            rv_path = mat.a3ob_properties_material.material_path");
            writer.WriteLine("                            if rv_path:");
            writer.WriteLine("                                # ntpath imported at top of script");
            writer.WriteLine("                                rv_dir = ntpath.dirname(rv_path).replace('\\\\', '/')");
            writer.WriteLine("                                rv_dir_name = rv_dir.split('/')[-1]");
            writer.WriteLine("                                if rv_dir_name and rv_dir_name != 'data' and rv_dir_name != 'textures' and rv_dir_name != _strip_brand(model_name) and rv_dir_name != 'model' and rv_dir_name != 'models':");
            writer.WriteLine("                                    clean = rv_dir_name.replace(' ', '_')");
            writer.WriteLine("                                    part_name = _strip_brand(clean)");
            writer.WriteLine("                                    obj.name = f'{part_name}_{variant}{_lod_tag(obj)}'");
            writer.WriteLine("                                    print(f\"[BIS]   Named '{obj.name}' from rvmat dir ({rv_dir_name})\", flush=True)");
            writer.WriteLine("                                    continue");
            writer.WriteLine("                        except:");
            writer.WriteLine("                            pass");
            writer.WriteLine("                        # Priority 3: procedural texture (e.g., #(argb,...)) - use rvmat filename or index");
            writer.WriteLine("                        try:");
            writer.WriteLine("                            if tex_name.startswith('#('):");
            writer.WriteLine("                                rv_path = mat.a3ob_properties_material.material_path");
            writer.WriteLine("                                if rv_path:");
            writer.WriteLine("                                    # ntpath imported at top of script");
            writer.WriteLine("                                    rv_base = ntpath.basename(rv_path)");
            writer.WriteLine("                                    rv_name = os.path.splitext(rv_base)[0]");
            writer.WriteLine("                                    if rv_name and rv_name != 'model' and rv_name != _strip_brand(model_name):");
            writer.WriteLine("                                        clean = _strip_brand(rv_name)");
            writer.WriteLine("                                        obj.name = f'{clean}_{variant}{_lod_tag(obj)}'");
            writer.WriteLine("                                        print(f\"[BIS]   Named '{obj.name}' from procedural tex ({tex_name[:30]}...)\", flush=True)");
            writer.WriteLine("                                        continue");
            writer.WriteLine("                                # Last resort: material index");
            writer.WriteLine("                                from bpy import data as _data");
            writer.WriteLine("                                mat_idx = list(_data.materials).index(mat)");
            writer.WriteLine("                                obj.name = f'color_{mat_idx}_{variant}{_lod_tag(obj)}'");
            writer.WriteLine("                                print(f\"[BIS]   Named '{obj.name}' from procedural tex index ({tex_name[:30]}...)\", flush=True)");
            writer.WriteLine("                                continue");
            writer.WriteLine("                        except:");
            writer.WriteLine("                            pass");
            writer.WriteLine("                        # Fallback: texture name");
            writer.WriteLine("                        try:");
            writer.WriteLine("                            clean = tex_name");
            writer.WriteLine("                            if not clean:");
            writer.WriteLine("                                # Empty texture - fall back to rvmat filename");
            writer.WriteLine("                                try:");
            writer.WriteLine("                                    rv_path = mat.a3ob_properties_material.material_path");
            writer.WriteLine("                                    if rv_path:");
            writer.WriteLine("                                        clean = os.path.splitext(ntpath.basename(rv_path))[0]");
            writer.WriteLine("                                except:");
            writer.WriteLine("                                    pass");
            writer.WriteLine("                            for sfx in ['_co', '_nohq', '_nopx', '_ca', '_gs', '_mco', '_s', '_as']:");
            writer.WriteLine("                                if clean.endswith(sfx):");
            writer.WriteLine("                                    clean = clean[:-len(sfx)]");
            writer.WriteLine("                                    break");
            writer.WriteLine("                            if '_' in clean:");
            writer.WriteLine("                                parts = clean.split('_', 1)");
            writer.WriteLine("                                if parts[0].isdigit() or len(parts[0]) <= 3:");
            writer.WriteLine("                                    clean = parts[1]");
            writer.WriteLine("                            # Strip Blender auto-suffix");
            writer.WriteLine("                            base = obj.name");
            writer.WriteLine("                            if '.' in base and base.rsplit('.', 1)[1].isdigit():");
            writer.WriteLine("                                base = base.rsplit('.', 1)[0]");
            writer.WriteLine("                            obj.name = f'{clean}_{variant}{_lod_tag(obj)}'");
            writer.WriteLine("                            print(f\"[BIS]   Named '{obj.name}' from texture fallback\", flush=True)");
            writer.WriteLine("                        except Exception as _ne:");
            writer.WriteLine("                            print(f\"[BIS]   Could not name {obj.name}: {_ne}\", flush=True)");
            writer.WriteLine("            else:");
            writer.WriteLine("                # Non-P3D materials are typically proxies/helpers");
            writer.WriteLine("                is_proxy = True");
            writer.WriteLine();
            writer.WriteLine("            if is_proxy:");
            writer.WriteLine("                for coll in bpy.data.collections:");
            writer.WriteLine("                    if obj.name in coll.objects:");
            writer.WriteLine("                        coll.objects.unlink(obj)");
            writer.WriteLine("                proxy_coll.objects.link(obj)");
            writer.WriteLine("        except Exception as _loop_err:");
            writer.WriteLine("            print(f\"[BIS]   Error processing {obj.name}: {_loop_err}\", flush=True)");
            writer.WriteLine();
            writer.WriteLine("    obj_count = len([o for o in bpy.data.objects if o.type == 'MESH'])");
            writer.WriteLine("    proxy_count = len([o for o in proxy_coll.objects if o.type == 'MESH'])");
            writer.WriteLine("    print(f'[BIS] Organized {obj_count} objects ({proxy_count} proxies in Proxies)', flush=True)");
            writer.WriteLine();
            writer.WriteLine("    # ─── LOD sub-collections ───");
            writer.WriteLine("    # Organize mesh objects into LOD-specific sub-collections");
            writer.WriteLine("    # e.g. view_R0, view_VP for Resolution 0 and View - Pilot");
            writer.WriteLine("    # Uses A3OB LOD enum value to determine parent group, since material");
            writer.WriteLine("    # separation may have moved objects out of their type collections.");
            writer.WriteLine("    lod_colls = {}");
            writer.WriteLine("    for obj in list(bpy.data.objects):");
            writer.WriteLine("        if obj.type != 'MESH':");
            writer.WriteLine("            continue");
            writer.WriteLine("        if obj.name in proxy_coll.objects:");
            writer.WriteLine("            continue");
            writer.WriteLine("        lod_tag = _lod_tag(obj)");
            writer.WriteLine("        if not lod_tag:");
            writer.WriteLine("            continue");
            writer.WriteLine("        # Determine parent group from A3OB LOD enum value");
            writer.WriteLine("        try:");
            writer.WriteLine("            obj_lod = obj.a3ob_properties_object.lod");
            writer.WriteLine("        except:");
            writer.WriteLine("            obj_lod = None");
            writer.WriteLine("        parent_name = _lod_group(obj_lod)");
            writer.WriteLine("        if parent_name is None:");
            writer.WriteLine("            # Fallback: use object's current collection name");
            writer.WriteLine("            for coll in obj.users_collection:");
            writer.WriteLine("                cname = coll.name");
            writer.WriteLine("                if cname in ('view','shadow_volume','geometry'):");
            writer.WriteLine("                    parent_name = cname");
            writer.WriteLine("                    break");
            writer.WriteLine("        if parent_name is None:");
            writer.WriteLine("            continue");
            writer.WriteLine("        # Find or create the parent collection");
            writer.WriteLine("        parent_coll = bpy.data.collections.get(parent_name)");
            writer.WriteLine("        if parent_coll is None:");
            writer.WriteLine("            parent_coll = bpy.data.collections.new(parent_name)");
            writer.WriteLine("            bpy.context.scene.collection.children.link(parent_coll)");
            writer.WriteLine("        sub_name = parent_name + lod_tag");
            writer.WriteLine("        if sub_name not in lod_colls:");
            writer.WriteLine("            sub = bpy.data.collections.new(sub_name)");
            writer.WriteLine("            parent_coll.children.link(sub)");
            writer.WriteLine("            lod_colls[sub_name] = sub");
            writer.WriteLine("        # Move object to LOD sub-collection");
            writer.WriteLine("        # Unlink from parent collection (leave scene collection intact)");
            writer.WriteLine("        for coll in list(obj.users_collection):");
            writer.WriteLine("            if coll.name == parent_name:");
            writer.WriteLine("                coll.objects.unlink(obj)");
            writer.WriteLine("                break");
            writer.WriteLine("        lod_colls[sub_name].objects.link(obj)");
            writer.WriteLine("    if lod_colls:");
            writer.WriteLine("        names = sorted(lod_colls.keys())");
            writer.WriteLine("        print(f'[BIS] LOD collections: {names}', flush=True)");
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
                writer.WriteLine("    # Rig setup: collection, bone collections, armature modifier, weight cleanup");
                writer.WriteLine("    armature = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)");
                writer.WriteLine("    if armature:");
                writer.WriteLine("        # --- Dedicated Rig collection ---");
                writer.WriteLine("        rig_coll = bpy.data.collections.get('Rig')");
                writer.WriteLine("        if rig_coll is None:");
                writer.WriteLine("            rig_coll = bpy.data.collections.new('Rig')");
                writer.WriteLine("            bpy.context.scene.collection.children.link(rig_coll)");
                writer.WriteLine("        for coll in list(bpy.data.collections):");
                writer.WriteLine("            if armature.name in coll.objects:");
                writer.WriteLine("                coll.objects.unlink(armature)");
                writer.WriteLine("        rig_coll.objects.link(armature)");
                writer.WriteLine();
                writer.WriteLine("        # --- Bone collections from skeleton ---");
                writer.WriteLine("        bone_coll = armature.data.collections.get('Skeleton')");
                writer.WriteLine("        if bone_coll is None:");
                writer.WriteLine("            bone_coll = armature.data.collections.new('Skeleton')");
                writer.WriteLine("        for bone in armature.data.bones:");
                writer.WriteLine("            if bone.name not in bone_coll:");
                writer.WriteLine("                bone_coll.assign(bone)");
                writer.WriteLine();
                writer.WriteLine("        # --- Armature modifier on meshes with matching vertex groups ---");
                writer.WriteLine("        bone_names = set(b.name for b in armature.data.bones)");
                writer.WriteLine("        modifier_count = 0");
                writer.WriteLine("        for obj in bpy.data.objects:");
                writer.WriteLine("            if obj.type != 'MESH' or not obj.vertex_groups:");
                writer.WriteLine("                continue");
                writer.WriteLine("            vg_names = set(vg.name for vg in obj.vertex_groups)");
                writer.WriteLine("            if not vg_names.intersection(bone_names):");
                writer.WriteLine("                continue");
                writer.WriteLine("            mod = obj.modifiers.new(name='Armature', type='ARMATURE')");
                writer.WriteLine("            mod.object = armature");
                writer.WriteLine("            obj.parent = armature");
                writer.WriteLine("            modifier_count += 1");
                writer.WriteLine("            # Move to Rig collection too");
                writer.WriteLine("            for coll in list(bpy.data.collections):");
                writer.WriteLine("                if obj.name in coll.objects and coll.name not in ('Rig', 'LODs', 'Proxies'):");
                writer.WriteLine("                    coll.objects.unlink(obj)");
                writer.WriteLine("            if obj.name not in rig_coll.objects:");
                writer.WriteLine("                rig_coll.objects.link(obj)");
                writer.WriteLine("        print(f'[BIS] Armature: {modifier_count} meshes skinned, {len(armature.data.bones)} bones', flush=True)");
                writer.WriteLine();
                writer.WriteLine("        # --- Weight painting cleanup via A3OB ---");
                writer.WriteLine("        try:");
                writer.WriteLine("            bpy.ops.arma3objectbuilder.rigging_general_cleanup()");
                writer.WriteLine("            print('[BIS] Weight painting cleanup done', flush=True)");
                writer.WriteLine("        except Exception:");
                writer.WriteLine("            try:");
                writer.WriteLine("                bpy.ops.a3ob.rigging_general_cleanup()");
                writer.WriteLine("                print('[BIS] Weight painting cleanup done (a3ob)', flush=True)");
                writer.WriteLine("            except Exception:");
                writer.WriteLine("                print('[BIS] Weight painting cleanup skipped (operator not found)', flush=True)");
                writer.WriteLine("    else:");
                writer.WriteLine("        print('[BIS] No armature found for rig setup', flush=True)");
                writer.WriteLine();
            }
            writer.WriteLine("    # ─── Scene cleanup & setup ───");
            writer.WriteLine("    # Delete empty LOD collections left after separation");
            writer.WriteLine("    # (but keep collections that have children, e.g. view->view_R0)");
            writer.WriteLine("    for coll in list(bpy.data.collections):");
            writer.WriteLine("        if coll.name not in ('LODs', 'Proxies') and len(coll.objects) == 0 and len(coll.children) == 0:");
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
            string outputDir, string imageDictLiteral, string? modelCfgPath, string? configDataLiteral = null)
        {
            string batchName = $"batch_{models[0].ModelName}_{models[^1].ModelName}";
            string scriptPath = Path.Combine(outputDir, $"import_{batchName}.py");

            using var writer = new StreamWriter(scriptPath);
            writer.WriteLine("import bpy");
writer.WriteLine("import os");
writer.WriteLine("import ntpath");
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
                string selMap = BuildSelectionMapLiteral(p3dPath);
                string cfgData = configDataLiteral ?? "None";
                writer.WriteLine($"import_and_save_model(r\"{pyP3DPath}\", \"{modelName}\", r\"{pyOutDir}\", {(modelCfgPath != null ? "r\"" + modelCfgPath.Replace("\\", "/") + "\"" : "None")}, {selMap}, {cfgData})");
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

            // Sanitize models with empty View-Pilot LODs that cause A3OB to hang
            foreach (var p3dPath in allP3Ds)
            {
                string modelName = Path.GetFileNameWithoutExtension(p3dPath);
                string importPath = HasEmptyViewLod(p3dPath) ? SanitizeP3D(p3dPath) : p3dPath;
                validModels.Add((modelName, importPath));
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

            // Build config data from config.cpp (displayName, baseClass, hiddenSelections)
            string configDataLiteral = "None";
            string configCppPath = Path.Combine(extractedDir, "config.cpp");
            if (!File.Exists(configCppPath))
                configCppPath = Path.Combine(extractedDir, "config.bin");
            if (File.Exists(configCppPath))
                configDataLiteral = BuildConfigDataLiteral(configCppPath);

            // Generate one batch script per batch
            var batchScripts = new List<string>();
            foreach (var batch in batches)
            {
                // Convert to expected tuple format for GenerateBatchScript
                var batchTuples = batch.Select(m => (m.Name, m.Path)).ToList();
                string scriptPath = GenerateBatchScript(batchTuples, outputDir, imageDictLiteral, modelCfgPath, configDataLiteral);
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
        public static string GenerateSingleModelScript(string p3dPath, string extractRoot, string outputDir, string? texturesDir = null, string? modelCfgPath = null)
        {
            Directory.CreateDirectory(outputDir);

            string modelName = Path.GetFileNameWithoutExtension(p3dPath);
            string imageDictLiteral = BuildImageDictLiteral(extractRoot, texturesDir);
            string? modelCfg = modelCfgPath ?? FindModelCfg(extractRoot);

            // Build config data from config.cpp (displayName, baseClass, hiddenSelections)
            string configDataLiteral = "None";
            string configCppPath = Path.Combine(extractRoot, "config.cpp");
            if (!File.Exists(configCppPath))
                configCppPath = Path.Combine(extractRoot, "config.bin");
            if (File.Exists(configCppPath))
                configDataLiteral = BuildConfigDataLiteral(configCppPath);

            var models = new List<(string Name, string Path)>
            {
                (modelName, Path.GetFullPath(p3dPath))
            };

            return GenerateBatchScript(models, outputDir, imageDictLiteral, modelCfg, configDataLiteral);
        }
    }
}
