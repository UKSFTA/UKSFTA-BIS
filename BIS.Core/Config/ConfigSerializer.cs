#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.Core.Config
{
    public static class ConfigSerializer
    {
        public static string SerializeToConfigText(ParamFile config)
        {
            var sb = new StringBuilder();
            foreach (var entry in config.Root.Entries)
                SerializeEntry(sb, entry, 0);
            return sb.ToString();
        }

        public static void Serialize(Stream configStream, Stream outputStream)
        {
            var config = new ParamFile(configStream);
            var text = SerializeToConfigText(config);
            using var writer = new StreamWriter(outputStream, Encoding.UTF8, -1, true);
            writer.Write(text);
        }

        public static void Serialize(ParamFile config, Stream outputStream)
        {
            var text = SerializeToConfigText(config);
            using var writer = new StreamWriter(outputStream, Encoding.UTF8, -1, true);
            writer.Write(text);
        }

        private static void SerializeEntry(StringBuilder sb, ParamEntry entry, int indent)
        {
            var ind = new string('\t', indent);
            switch (entry)
            {
                case ParamClass cls:
                    var basePart = string.IsNullOrEmpty(cls.BaseClassName) ? "" : $" : {cls.BaseClassName}";
                    sb.AppendLine($"{ind}class {cls.Name}{basePart} {{");
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
                SerializeEntry(sb, entry, indent);
        }

        private static void SerializeValueLine(StringBuilder sb, ParamValue pv, int indent)
        {
            var ind = new string('\t', indent);
            if (pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression)
                sb.AppendLine($"{ind}{pv.Name} = \"{EscapeString(pv.Value.Value as string)}\";");
            else if (pv.Value.Type == ConfigValueType.Float)
                sb.AppendLine($"{ind}{pv.Name} = {((float)pv.Value.Value).ToString(CultureInfo.InvariantCulture)};");
            else if (pv.Value.Type == ConfigValueType.Int)
                sb.AppendLine($"{ind}{pv.Name} = {((int)pv.Value.Value).ToString(CultureInfo.InvariantCulture)};");
            else if (pv.Value.Type == ConfigValueType.Int64)
                sb.AppendLine($"{ind}{pv.Name} = {((long)pv.Value.Value).ToString(CultureInfo.InvariantCulture)};");
        }

        private static string EscapeString(string? s)
        {
            if (s == null) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
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
                    result[i] = (rv.Value as float?)?.ToString(CultureInfo.InvariantCulture) ?? "0";
                else if (rv.Type == ConfigValueType.Int)
                    result[i] = (rv.Value as int?)?.ToString(CultureInfo.InvariantCulture) ?? "0";
                else if (rv.Type == ConfigValueType.Int64)
                    result[i] = (rv.Value as long?)?.ToString(CultureInfo.InvariantCulture) ?? "0";
                else
                    result[i] = rv.Value?.ToString() ?? "null";
            }
            return result;
        }
    }
}
