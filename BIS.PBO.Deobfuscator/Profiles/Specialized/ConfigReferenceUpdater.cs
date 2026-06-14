using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIS.Core.Config;
using BIS.PBO;
using ConfigValueType = BIS.Core.Config.ValueType;

namespace BIS.PBO.Deobfuscator
{
    public class ConfigReferenceUpdater : IReferenceUpdater
    {
        public byte[]? UpdateReferences(IPBOFileEntry fileEntry, Dictionary<string, string> pathMap)
        {
            if (!string.Equals(fileEntry.FileName, "config.bin", StringComparison.OrdinalIgnoreCase))
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

            using var ms = new MemoryStream();
            using (var writer = new BIS.Core.Streams.BinaryWriterEx(ms, true))
            {
                config.Write(writer);
            }
            return ms.ToArray();
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
    }
}
