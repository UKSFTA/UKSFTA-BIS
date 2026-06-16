using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BIS.P3D.Export
{
    /// <summary>
    /// Generates Blender Python scripts that:
    /// 1. Import a P3D model via a3ob
    /// 2. Apply texture/material path remapping from a JSON config
    /// 3. Re-export the modified P3D
    /// </summary>
    public static class RetextureExport
    {
        /// <summary>
        /// Generates a Blender Python script for re-texturing a P3D model.
        /// The script imports the model, applies the remap, and re-exports.
        /// Returns the path to the generated script.
        /// </summary>
        public static string GenerateRetextureScript(string modelPath, string remapPath, string outputPath)
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

            writer.WriteLine("# Enable a3ob addon");
            writer.WriteLine("bpy.ops.preferences.addon_enable(module=\"bl_ext.blender_org.Arma3ObjectBuilder\")");
            writer.WriteLine();

            string pyModelPath = modelPath.Replace("\\", "/");
            string pyOutputPath = outputPath.Replace("\\", "/");

            writer.WriteLine($"model_path = r\"{pyModelPath}\"");
            writer.WriteLine($"output_path = r\"{pyOutputPath}\"");
            writer.WriteLine();

            // Embed the remap JSON directly in the script (avoids file dependency at runtime)
            writer.WriteLine("# Texture/material remap configuration");
            writer.WriteLine($"remap = {remapJson}");
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

            writer.WriteLine("    # ─── Apply texture remapping ───");
            writer.WriteLine("    # Walk all P3D materials and remap their texture/material paths.");
            writer.WriteLine("    tex_count = 0");
            writer.WriteLine("    mat_count = 0");
            writer.WriteLine("    for mat in bpy.data.materials:");
            writer.WriteLine("        if mat.name.startswith('P3D:'):");
            writer.WriteLine("            props = mat.a3ob_properties_material");

            // Texture path remapping
            writer.WriteLine("            # Remap texture path");
            writer.WriteLine("            old_tex = props.texture_path");
            writer.WriteLine("            if old_tex and old_tex in remap.get('textures', {}):");
            writer.WriteLine("                new_tex = remap['textures'][old_tex]");
            writer.WriteLine("                print(f\"[BIS]   Remap tex: {{old_tex}} -> {{new_tex}}\", flush=True)");
            writer.WriteLine("                props.texture_path = new_tex");
            writer.WriteLine("                tex_count += 1");

            // Material path remapping
            writer.WriteLine("            # Remap material (rvmat) path");
            writer.WriteLine("            old_mat = props.material_path");
            writer.WriteLine("            if old_mat and old_mat in remap.get('materials', {}):");
            writer.WriteLine("                new_mat = remap['materials'][old_mat]");
            writer.WriteLine("                print(f\"[BIS]   Remap mat: {{old_mat}} -> {{new_mat}}\", flush=True)");
            writer.WriteLine("                props.material_path = new_mat");
            writer.WriteLine("                mat_count += 1");

            // Color type remapping
            writer.WriteLine("            # Remap color type");
            writer.WriteLine("            old_color = props.color_type");
            writer.WriteLine("            if old_color and old_color in remap.get('color_types', {}):");
            writer.WriteLine("                new_color = remap['color_types'][old_color]");
            writer.WriteLine("                print(f\"[BIS]   Remap color: {{old_color}} -> {{new_color}}\", flush=True)");
            writer.WriteLine("                props.color_type = new_color");

            writer.WriteLine();
            writer.WriteLine("    print(f\"[BIS] Remapped {{tex_count}} texture(s), {{mat_count}} material(s)\", flush=True)");
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
