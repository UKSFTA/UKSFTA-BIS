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
            // Phase 0: Direct lookup — skip identity mappings (strays that were not renamed).
            // Identity mappings block suffix-based resolution in Phase 1/2 below.
            if (pathMap.TryGetValue(contentPath, out resolved) &&
                !string.Equals(resolved, contentPath, StringComparison.OrdinalIgnoreCase))
                return true;

            // Phase 0b: Strip leading PBO prefix if present (config.bin stores paths with
            // prefix like "jsoar/data/patches/_co.paa", pathMap uses relative paths).
            var firstSlash = contentPath.IndexOf('/');
            if (firstSlash > 0)
            {
                var stripped = contentPath.Substring(firstSlash + 1);
                // Direct lookup on stripped path (skip identity mappings too)
                if (pathMap.TryGetValue(stripped, out resolved) &&
                    !string.Equals(resolved, stripped, StringComparison.OrdinalIgnoreCase))
                    return true;
                // Try suffix-based resolution on stripped path (the first component was the PBO prefix)
                if (ResolveBySuffix(stripped, pathMap, out resolved))
                    return true;
            }

            // Phase 1 + 2: suffix-based resolution on the original content path.
            // This also handles paths like "data/.paa" where the first component
            // is NOT a PBO prefix and should not be stripped.
            return ResolveBySuffix(contentPath, pathMap, out resolved);
        }

        private static bool ResolveBySuffix(string contentPath, Dictionary<string, string> pathMap, out string? resolved)
        {
            var contentSlash = contentPath.LastIndexOf('/');
            var contentDir = contentSlash >= 0 ? contentPath.Substring(0, contentSlash) : "";
            var contentFile = contentSlash >= 0 ? contentPath.Substring(contentSlash + 1) : contentPath;

            // Phase 1: Check if contentPath's filename ends with any pathMap KEY's filename.
            // This handles cases where config.bin stores full paths and the key is a
            // different (obfuscated) full path — the suffix match catches it.
            foreach (var kvp in pathMap)
            {
                // Skip identity mappings (strays that were not renamed) — they map to themselves
                // and would match before more useful non-identity resolutions.
                if (string.Equals(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase))
                    continue;

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

            // Phase 2: For suffix-only content paths (e.g. "_co.paa") that didn't match
            // by key suffix, try reverse-matching: check if any pathMap VALUE's filename
            // ends with the content filename in the same directory.
            // This handles hiddenSelectionsTextures references like "data/patches/_co.paa"
            // where the VALUE is e.g. "data/patches/patch_co.paa" (recovered name).
            foreach (var kvp in pathMap)
            {
                // Skip identity mappings for the same reason as Phase 1 above.
                if (string.Equals(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase))
                    continue;

                var val = kvp.Value;
                var valSlash = val.LastIndexOf('/');
                var valDir = valSlash >= 0 ? val.Substring(0, valSlash) : "";
                var valFile = valSlash >= 0 ? val.Substring(valSlash + 1) : val;

                if (!valFile.EndsWith(contentFile, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(valDir, contentDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                resolved = kvp.Value;
                return true;
            }

            resolved = null;
            return false;
        }
    }
}
