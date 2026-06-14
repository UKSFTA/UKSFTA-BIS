using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIS.Core.Config;
using BIS.PBO;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.PBO.Deobfuscator
{
    public class RVMATReferenceUpdater : IReferenceUpdater
    {
        public byte[]? UpdateReferences(IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
        {
            var ext = Path.GetExtension(fileEntry.FileName);
            if (!string.Equals(ext, ".rvmat", StringComparison.OrdinalIgnoreCase))
                return null;

            ParamFile config;
            try
            {
                using var stream = fileEntry.OpenRead();
                config = new ParamFile(stream);
            }
            catch
            {
                return null;
            }

            bool modified = ReplacePaths(config.Root, pathMap);
            if (!modified)
                return null;

            var text = SerializeToConfigText(config);
            return Encoding.UTF8.GetBytes(text);
        }

        private static bool ReplacePaths(ParamClass cls, Dictionary<string, string> pathMap)
        {
            bool modified = false;

            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass nested:
                        if (ReplacePaths(nested, pathMap))
                            modified = true;
                        break;

                    case ParamValue pv:
                        if (pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression)
                        {
                            var val = pv.Value.Value as string;
                            if (val != null)
                            {
                                var normalized = val.Replace('\\', '/');
                                if (TryResolvePath(normalized, pathMap, out string newPath))
                                {
                                    pv.Value.SetValue(newPath);
                                    modified = true;
                                }
                            }
                        }
                        break;

                    case ParamArray pa:
                        if (ReplaceInArray(pa.Array.Entries, pathMap))
                            modified = true;
                        break;

                    case ParamArraySpec pas:
                        if (ReplaceInArray(pas.Array.Entries, pathMap))
                            modified = true;
                        break;
                }
            }

            return modified;
        }

        private static bool ReplaceInArray(List<RawValue> entries, Dictionary<string, string> pathMap)
        {
            bool modified = false;
            foreach (var rv in entries)
            {
                if (rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression)
                {
                    var val = rv.Value as string;
                    if (val != null)
                    {
                        var normalized = val.Replace('\\', '/');
                        if (TryResolvePath(normalized, pathMap, out string newPath))
                        {
                            rv.SetValue(newPath);
                            modified = true;
                        }
                    }
                }
            }
            return modified;
        }

        private static bool TryResolvePath(string contentPath, Dictionary<string, string> pathMap, out string? resolved)
        {
            if (pathMap.TryGetValue(contentPath, out resolved))
                return true;

            var contentSlash = contentPath.LastIndexOf('/');
            var contentDir = contentSlash >= 0 ? contentPath.Substring(0, contentSlash) : "";
            var contentFile = contentSlash >= 0 ? contentPath.Substring(contentSlash + 1) : contentPath;

            foreach (var kvp in pathMap)
            {
                var key = kvp.Key;
                var keySlash = key.LastIndexOf('/');
                var keyDir = keySlash >= 0 ? key.Substring(0, keySlash) : "";
                var keyFile = keySlash >= 0 ? key.Substring(keySlash + 1) : key;

                if (!contentFile.EndsWith(keyFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!contentDir.EndsWith(keyDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved = kvp.Value;
                return true;
            }

            resolved = null;
            return false;
        }

        private static string SerializeToConfigText(ParamFile config)
        {
            var sb = new StringBuilder();
            foreach (var entry in config.Root.Entries)
            {
                SerializeEntry(sb, entry, 0);
            }
            return sb.ToString();
        }

        private static void SerializeEntry(StringBuilder sb, ParamEntry entry, int indent)
        {
            var ind = new string('\t', indent);
            switch (entry)
            {
                case ParamClass cls:
                    var basePart = string.IsNullOrEmpty(cls.BaseClassName) ? "" : $" : {cls.BaseClassName}";
                    sb.AppendLine($"{ind}class {cls.Name}{basePart}");
                    sb.AppendLine($"{ind}{{");
                    SerializeClassBody(sb, cls, indent + 1);
                    sb.AppendLine($"{ind}}};");
                    break;

                case ParamValue pv:
                    SerializeValueLine(sb, pv, indent);
                    break;

                case ParamArray pa:
                    sb.AppendLine($"{ind}{pa.Name}[] = {{ {string.Join(", ", SerializeRawValues(pa.Array.Entries))} }};");
                    break;

                case ParamArraySpec pas:
                    sb.AppendLine($"{ind}{pas.Name}[] = {{ {string.Join(", ", SerializeRawValues(pas.Array.Entries))} }};");
                    break;

                case ParamExternClass pec:
                    sb.AppendLine($"{ind}class {pec.Name};");
                    break;

                case ParamDeleteClass pdc:
                    sb.AppendLine($"{ind}delete {pdc.Name};");
                    break;
            }
        }

        private static void SerializeClassBody(StringBuilder sb, ParamClass cls, int indent)
        {
            foreach (var entry in cls.Entries)
            {
                SerializeEntry(sb, entry, indent);
            }
        }

        private static void SerializeValueLine(StringBuilder sb, ParamValue pv, int indent)
        {
            var ind = new string('\t', indent);
            if (pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression)
                sb.AppendLine($"{ind}{pv.Name} = \"{EscapeString(pv.Value.Value as string)}\";");
            else if (pv.Value.Type == ConfigValueType.Float)
                sb.AppendLine($"{ind}{pv.Name} = {pv.Value.Value};");
            else if (pv.Value.Type == ConfigValueType.Int)
                sb.AppendLine($"{ind}{pv.Name} = {pv.Value.Value};");
            else if (pv.Value.Type == ConfigValueType.Int64)
                sb.AppendLine($"{ind}{pv.Name} = {pv.Value.Value};");
        }

        private static string EscapeString(string? s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string[] SerializeRawValues(List<RawValue> values)
        {
            var result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                var rv = values[i];
                if (rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression)
                    result[i] = $"\"{EscapeString(rv.Value as string)}\"";
                else if (rv.Type == ConfigValueType.Float)
                    result[i] = rv.Value?.ToString() ?? "0";
                else if (rv.Type == ConfigValueType.Int)
                    result[i] = rv.Value?.ToString() ?? "0";
                else if (rv.Type == ConfigValueType.Int64)
                    result[i] = rv.Value?.ToString() ?? "0";
                else
                    result[i] = rv.Value?.ToString() ?? "null";
            }
            return result;
        }
    }
}
