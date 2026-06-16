using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIS.Core.Config;
using ConfigValueType = BIS.Core.Config.ValueType;
using BIS.PBO;
using BIS.PBO.Deobfuscator.Format;
using BIS.PBO.Deobfuscator.Profiles.Specialized;
using P3DModel = BIS.P3D.P3D;

namespace BIS.PBO.Deobfuscator.Profiles
{
    public class SuffixRecoveryProfile : IObfuscationProfile
    {
        private static readonly string[] RealExtensions = new[]
        {
            ".paa", ".p3d", ".rvmat", ".dll", ".so"
        };

        private static readonly Regex RandomNamePattern = new Regex(
            @"^[A-Za-z0-9]{2,12}$",
            RegexOptions.Compiled
        );
        /// <summary>
        /// Handles PBOs whose filenames have been stripped to suffix-only patterns.
        ///
        /// These PBOs have file entries where the base name has been truncated, leaving
        /// only the suffix and extension (e.g. "data\abav\_as.paa" instead of
        /// "data\abav\avs_assault_as.paa") or just an extension with no base name
        /// (e.g. "acex\.paa"). The folder structure and extension are preserved.
        ///
        /// Detection uses structural heuristics:
        ///   - File entries whose names consist only of _suffix.ext or .ext
        ///   - Presence of a config.bin for cross-reference recovery
        ///
        /// Recovery strategy: scan the binarized config.bin for ASCII path strings that
        /// reference the original full file paths. Cross-reference by folder + suffix
        /// to rebuild names where possible.
        /// </summary>
        public string ProfileName => "Suffix-based Recovery";

        // PBO paths use \ as separator; normalize to / for cross-platform Path methods
        private static string NormalizePath(string pboPath) => pboPath.Replace('\\', '/');

        private static string GetFileName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            return norm.Contains('/') ? norm.Substring(norm.LastIndexOf('/') + 1) : norm;
        }

        private static string StripPrefix(string path, string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return path;
            var prefixSlash = prefix.TrimEnd('/') + "/";
            return path.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(prefixSlash.Length)
                : path;
        }

        private static string GetDirectoryName(string pboPath)
        {
            var norm = NormalizePath(pboPath);
            var idx = norm.LastIndexOf('/');
            return idx >= 0 ? norm.Substring(0, idx) : "";
        }

        public bool IsMatch(BIS.PBO.PBO pbo)
        {
            bool hasStrippedNames = pbo.Files.Any(f =>
            {
                var name = GetFileName(f.FileName);

                // Match: extension-only names like ".paa", ".rvmat"
                if (name.StartsWith("."))
                    return true;

                // Match: Cyrillic characters in filename
                if (name.Any(c => c >= 'а' && c <= 'я' || c >= 'А' && c <= 'Я'))
                    return true;

                // Match: suffix-only names like "_co.paa", "_as.paa"
                if (name.StartsWith("_"))
                    return true;

                return false;
            });

            return hasStrippedNames;
        }

        public DeobfuscationResult Deobfuscate(BIS.PBO.PBO pbo)
        {
            var result = new DeobfuscationResult { MatchedProfile = ProfileName };

            var config = pbo.GetRootConfig();
            var knownPaths = new List<string>();

            // Step 3d: scan all .bin entries for supplementary config data (e.g. CfgFunctions split files).
            // This runs even when config.bin is absent — the split files are independent.
            int binFilePaths = 0;
            int binDirPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var binFile = pbo.Files[fi];
                var binExt = Path.GetExtension(binFile.FileName);
                if (!string.Equals(binExt, ".bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(binFile.FileName, "config.bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(binFile.FileName, "texHeaders.bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var binStream = binFile.OpenRead();
                    var binHeader = new byte[4];
                    if (binStream.Read(binHeader, 0, 4) < 4)
                        continue;
                    binStream.Position = 0;

                    ParamFile parsedConfig;
                    if (binHeader[0] == 0x00 && binHeader[1] == 0x72 && binHeader[2] == 0x61 && binHeader[3] == 0x50)
                    {
                        parsedConfig = new ParamFile(binStream);
                    }
                    else
                    {
                        using var reader = new StreamReader(binStream);
                        var source = reader.ReadToEnd();
                        var tokens = ConfigTokenizer.Tokenize(source, binFile.FileName);
                        var parser = new ConfigParser();
                        parsedConfig = parser.Parse(tokens);
                    }

                    var binExtracted = ExtractPathsFromRap(parsedConfig.Root);
                    foreach (var path in binExtracted)
                    {
                        var norm = NormalizePath(path);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            binFilePaths++;
                        }
                    }

                    // Also extract directory-style paths (backslash-separated, no extension)
                    var dirPaths = new List<string>();
                    CollectBinaryStringValues(parsedConfig.Root, dirPaths);
                    foreach (var dp in dirPaths)
                    {
                        var norm = NormalizePath(dp);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            binDirPaths++;
                        }
                    }
                }
                catch
                {
                }
            }
            int binPaths = binFilePaths + binDirPaths;
            if (binPaths > 0)
                Console.WriteLine($"  -> Scanned {binPaths} config paths from supplementary .bin files ({binFilePaths} file, {binDirPaths} directory).");

            if (config != null)
            {
                Console.WriteLine("  -> Parsing config.bin for path references...");
                var configPaths = ExtractPathsFromRap(config.Root);
                foreach (var cp in configPaths)
                {
                    if (!knownPaths.Contains(cp))
                        knownPaths.Add(cp);
                }
                Console.WriteLine($"  -> Found {configPaths.Count} candidate path strings in config.bin, {knownPaths.Count} total.");
            }
            else
            {
                Console.WriteLine("  -> No config.bin found, skipping context recovery.");
            }

            var prefix = NormalizePath(pbo.Prefix ?? "");

            // Step 1: Detect and filter out decoy files and stub scripts
            Console.WriteLine("  -> Scanning files for decoy injection markers...");
            int decoys = 0;
            int stubs = 0;
            int entryPoints = 0;

            for (int i = 0; i < pbo.Files.Count; i++)
            {
                var file = pbo.Files[i];
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                string nameOnly = Path.GetFileNameWithoutExtension(file.FileName);
                bool hasDirectory = file.FileName.Contains('\\');

                // Zero-byte files are known decoy entries
                if (file.Size == 0)
                {
                    result.FilteredOut.Add(i);
                    decoys++;
                    continue;
                }

                // Stub scripts are small files with random names
                if (file.Size < 20 && RandomNamePattern.IsMatch(nameOnly))
                {
                    result.FilteredOut.Add(i);
                    stubs++;
                    continue;
                }

                // Entry point scripts are larger files with known extensions
                if (RealExtensions.Contains(ext) && file.Size > 100)
                {
                    entryPoints++;
                }
            }

            result.Stats["Decoys"] = decoys;
            result.Stats["Stubs"] = stubs;
            result.Stats["EntryPoints"] = entryPoints;
            result.Stats["Genuine"] = pbo.Files.Count - decoys - stubs;

            // Step 2: scan P3D/ODOL files for embedded texture/material paths
            Console.WriteLine("  -> Scanning P3D files for embedded paths...");
            int p3dScanned = 0;
            int p3dPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var p3d = new P3DModel(stream);
                    p3dScanned++;

                    foreach (var lod in p3d.LODs)
                    {
                        foreach (var tex in lod.GetTextures())
                        {
                            var norm = NormalizePath(tex);
                            if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                        foreach (var mat in lod.GetMaterials())
                        {
                            var norm = NormalizePath(mat);
                            if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                            {
                                knownPaths.Add(norm);
                                p3dPaths++;
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            if (p3dScanned > 0)
                Console.WriteLine($"  -> Scanned {p3dScanned} P3D files, extracted {p3dPaths} unique paths (total candidates: {knownPaths.Count}).");
            else
                Console.WriteLine($"  -> No P3D files found in PBO.");

            // Step 3b: scan standalone RVMAT files for texture/material references
            int rvmatScanned = 0;
            int rvmatPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".rvmat", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var rvmat = file.ReadAsConfig();
                    rvmatScanned++;
                    var rvmatExtracted = ExtractPathsFromRap(rvmat.Root);
                    foreach (var path in rvmatExtracted)
                    {
                        var norm = NormalizePath(path);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            rvmatPaths++;
                        }
                    }
                }
                catch
                {
                }
            }
            if (rvmatScanned > 0)
                Console.WriteLine($"  -> Scanned {rvmatScanned} RVMAT files, extracted {rvmatPaths} unique paths (total candidates: {knownPaths.Count}).");

            // Step 3c: scan texHeaders.bin for texture path references
            int texHeaderPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                if (!file.FileName.Equals("texHeaders.bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = file.OpenRead();
                    var texHeaders = TexHeaders.Read(stream);
                    foreach (var tex in texHeaders.Textures)
                    {
                        var norm = NormalizePath(tex.PAAFile);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            texHeaderPaths++;
                        }
                    }
                    Console.WriteLine($"  -> Scanned texHeaders.bin: {texHeaders.Textures.Count} entries, {texHeaderPaths} new paths (total candidates: {knownPaths.Count}).");
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    if (msg.Length > 80) msg = msg[..80] + "...";
                    Console.WriteLine($"  -> texHeaders.bin parse failed: {ex.GetType().Name}: {msg}");
                }
                break;
            }

            // Step 3d: scan supplementary .bin files for text-format config data (e.g. CfgFunctions split files)
            int extraBinPaths = 0;
            for (int fi = 0; fi < pbo.Files.Count; fi++)
            {
                var file = pbo.Files[fi];
                var ext = Path.GetExtension(file.FileName);
                if (!string.Equals(ext, ".bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                // Skip already-parsed files
                if (string.Equals(file.FileName, "config.bin", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(file.FileName, "texHeaders.bin", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var binConfig = ProfileUtils.TryParseBinEntry(file);
                    if (binConfig == null) continue;

                    var extracted = ExtractPathsFromRap(binConfig.Root);
                    foreach (var path in extracted)
                    {
                        var norm = NormalizePath(path);
                        if (!string.IsNullOrEmpty(norm) && IsValidPathString(norm) && !knownPaths.Contains(norm))
                        {
                            knownPaths.Add(norm);
                            extraBinPaths++;
                        }
                    }
                }
                catch
                {
                }
            }
            if (extraBinPaths > 0)
                Console.WriteLine($"  -> Scanned supplementary .bin files: {extraBinPaths} new paths (total candidates: {knownPaths.Count}).");
            else
                Console.WriteLine($"  -> No supplementary config data found in .bin files.");

            // Strip PBO prefix from all candidate paths
            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixSlash = prefix.TrimEnd('/') + "/";
                for (int i = 0; i < knownPaths.Count; i++)
                {
                    var norm = NormalizePath(knownPaths[i]);
                    if (norm.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
                        knownPaths[i] = norm.Substring(prefixSlash.Length);
                }
            }

            int total = pbo.Files.Count;
            int recovered = 0;
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (config == null)
            {
                result.Stats["Recovered"] = 0;
                result.Stats["Total"] = total;
                result.Stats["Unrecovered"] = total;
                Console.WriteLine($"  -> Recovery skipped (no config.bin). {total} files remain obfuscated.");
                return result;
            }

            // Step 4a: heuristic class-name-to-filename matching (priority)
            // Uses clean class names from config.bin rather than obfuscated path strings
            var classNames = ExtractClassNames(config.Root);
            var modPrefix = DetectModPrefix(classNames, prefix);
            if (modPrefix != null)
                Console.WriteLine($"  -> Detected mod prefix: \"{modPrefix}\" — stripping from class names");
            var suffixToClass = BuildSuffixToClassMap(classNames, prefix, modPrefix);
            if (suffixToClass.Count > 0)
            {
                Console.WriteLine($"  -> Matching {classNames.Count} class names to {total} files...");
                for (int i = 0; i < total; i++)
                {
                    if (result.RecoveredNames.ContainsKey(i))
                        continue;

                    var file = pbo.Files[i];
                    var dir = GetDirectoryName(file.FileName);
                    var name = GetFileName(file.FileName);

                    if (!name.StartsWith("_") && !name.StartsWith("."))
                        continue;

                    var ext = Path.GetExtension(name);
                    var suffix = Path.GetFileNameWithoutExtension(name);

                    bool matched = false;
                    foreach (var kvp in suffixToClass)
                    {
                        var dirWord = kvp.Key;
                        var candidates = kvp.Value;

                        if (!dir.Contains(dirWord, StringComparison.OrdinalIgnoreCase) &&
                            !dirWord.Contains(dir.Replace("/", ""), StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (var cls in candidates)
                        {
                            var strippedCls = ProfileUtils.StripColorSuffixes(cls);
                            var reconstructed = $"{dir}/{strippedCls.ToLowerInvariant()}{suffix.ToLowerInvariant()}{ext.ToLowerInvariant()}";
                            if (!usedPaths.Contains(reconstructed))
                            {
                                Console.WriteLine($"  -> Class match: {file.FileName}  =>  {reconstructed}");
                                result.RecoveredNames[i] = reconstructed;
                                usedPaths.Add(reconstructed);
                                recovered++;
                                matched = true;
                                break;
                            }
                        }
                        if (matched)
                            break;
                    }
                }
            }

            // Step 4b: config model/image path recovery
            // Walks config.bin for model= and image= properties to map obfuscated
            // paths to clean parent class names (which are always ASCII).
            Console.WriteLine("  -> Building config model/image/picture path maps...");
            var configModelMap = BuildConfigModelMap(config.Root, prefix);
            var configImageMap = BuildConfigImageMap(config.Root);
            var configPictureMap = BuildConfigPictureMap(config.Root, prefix);
            var displayNameMap = BuildConfigDisplayNameMap(config.Root);
            if (configModelMap.Count > 0)
                Console.WriteLine($"  -> Config model map: {configModelMap.Count} model references to class names");
            if (configImageMap.Count > 0)
                Console.WriteLine($"  -> Config image map: {configImageMap.Count} image references to class names");
            if (configPictureMap.Count > 0)
                Console.WriteLine($"  -> Config picture map: {configPictureMap.Count} picture references to class names");
            if (displayNameMap.Count > 0)
                Console.WriteLine($"  -> Config displayName map: {displayNameMap.Count} class names with displayName properties");

            for (int i = 0; i < total; i++)
            {
                if (result.RecoveredNames.ContainsKey(i))
                    continue;

                var file = pbo.Files[i];
                var dir = GetDirectoryName(file.FileName);
                var name = GetFileName(file.FileName);

                if (!name.StartsWith("_") && !name.StartsWith("."))
                    continue;

                var ext = Path.GetExtension(name);
                var suffix = Path.GetFileNameWithoutExtension(name);
                var rawNorm = file.RawFileName.Replace('\\', '/');

                string? baseName = null;
                if (string.Equals(ext, ".p3d", StringComparison.OrdinalIgnoreCase) &&
                    configModelMap.TryGetValue(rawNorm, out var modelClassName))
                {
                    baseName = modelClassName;
                }
                else if (string.Equals(ext, ".paa", StringComparison.OrdinalIgnoreCase))
                {
                    if (configImageMap.TryGetValue(rawNorm, out var variantName))
                        baseName = variantName;
                    else if (configPictureMap.TryGetValue(rawNorm, out var pictureClassName))
                        baseName = pictureClassName;
                }

                if (baseName != null)
                {
                    var reconstructed = $"{dir}/{baseName.ToLowerInvariant()}{suffix.ToLowerInvariant()}{ext.ToLowerInvariant()}";
                    if (!usedPaths.Contains(reconstructed))
                    {
                        Console.WriteLine($"  -> Config match: {file.FileName}  =>  {reconstructed}");
                        result.RecoveredNames[i] = reconstructed;
                        usedPaths.Add(reconstructed);
                        recovered++;
                    }
                }
            }

            // Step 4c: P3D texture back-referencing for remaining unrecovered entries
            int p3dRefRecovered = 0;
            if (recovered < total)
            {
                var p3dRefMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Directory → class name map from P3D RVMAT references.
                // Used as fallback when Cyrillic path matching fails — bypasses
                // P3D (Latin-1) vs PBO (UTF-8) encoding mismatch.
                var rvmatDirClassMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int pi = 0; pi < total; pi++)
                {
                    var p3dFile = pbo.Files[pi];
                    var p3dExt = Path.GetExtension(p3dFile.FileName);
                    if (!string.Equals(p3dExt, ".p3d", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? modelClassName = null;
                    if (result.RecoveredNames.TryGetValue(pi, out var recoveredModelName))
                        modelClassName = Path.GetFileNameWithoutExtension(recoveredModelName);
                    if (modelClassName == null)
                    {
                        var rawNorm = p3dFile.RawFileName.Replace('\\', '/');
                        if (configModelMap.TryGetValue(rawNorm, out var configClassName))
                            modelClassName = configClassName;
                    }
                    if (modelClassName == null)
                        modelClassName = Path.GetFileNameWithoutExtension(p3dFile.FileName);
                    if (string.IsNullOrEmpty(modelClassName))
                        continue;

                    try
                    {
                        using var stream = p3dFile.OpenRead();
                        var p3d = new P3DModel(stream);
                        foreach (var lod in p3d.LODs)
                        {
                            foreach (var tex in lod.GetTextures() ?? Enumerable.Empty<string>())
                            {
                                var fixedTex = StripPrefix(
                                    P3DTextureReferenceUpdater.FixEncoding(tex).Replace('\\', '/').TrimStart('/'), prefix);
                                if (!string.IsNullOrEmpty(fixedTex) && !p3dRefMap.ContainsKey(fixedTex))
                                    p3dRefMap[fixedTex] = modelClassName;
                            }
                            foreach (var mat in lod.GetMaterials() ?? Enumerable.Empty<string>())
                            {
                                var fixedMat = StripPrefix(
                                    P3DTextureReferenceUpdater.FixEncoding(mat).Replace('\\', '/'), prefix);
                                if (!string.IsNullOrEmpty(fixedMat) && !p3dRefMap.ContainsKey(fixedMat))
                                    p3dRefMap[fixedMat] = modelClassName;

                                // Also store the raw (pre-FixEncoding) path as a fallback key.
                                // FixEncoding converts Cyrillic to ASCII approximations, making
                                // the key unmatchable against PBO RawFileName (which retains the
                                // original Cyrillic). The raw path bridges this gap.
                                var rawMat = StripPrefix(mat.Replace('\\', '/'), prefix);
                                if (!string.IsNullOrEmpty(rawMat) &&
                                    !string.Equals(rawMat, fixedMat, StringComparison.OrdinalIgnoreCase) &&
                                    !p3dRefMap.ContainsKey(rawMat))
                                {
                                    p3dRefMap[rawMat] = modelClassName;
                                }

                                // Track which directories have P3D-referenced RVMATs
                                var fixedMatDir = GetDirectoryName(fixedMat);
                                if (!string.IsNullOrEmpty(fixedMatDir) && !rvmatDirClassMap.ContainsKey(fixedMatDir))
                                    rvmatDirClassMap[fixedMatDir] = modelClassName;
                            }
                        }
                    }
                    catch { }
                }

                if (p3dRefMap.Count > 0)
                {
                    for (int ri = 0; ri < total; ri++)
                    {
                        if (result.RecoveredNames.ContainsKey(ri))
                            continue;

                        var uFile = pbo.Files[ri];
                        var uDir = GetDirectoryName(uFile.FileName);
                        var uName = GetFileName(uFile.FileName);

                        if (!uName.StartsWith("_") && !uName.StartsWith("."))
                            continue;

                        var uRawNorm = uFile.RawFileName.Replace('\\', '/');
                        if (!p3dRefMap.TryGetValue(uRawNorm, out var modelClass))
                        {
                            // Fallback: try the sanitized FileName.
                            // Multiple entries may sanitize from different Cyrillic originals
                            // to the same ASCII suffix (e.g. _co.paa), so matching on
                            // RawFileName fails but FileName matches the p3dRefMap key.
                            var uFileNorm = uFile.FileName.Replace('\\', '/');
                            p3dRefMap.TryGetValue(uFileNorm, out modelClass);
                        }
                        if (modelClass == null)
                        {
                            // DEBUG: log unmatched entries so we can see what's being missed
                            if (uName.StartsWith("_") || uName.StartsWith("."))
                                Console.WriteLine($"  -> P3D ref MISS: {uFile.FileName} (raw: {uFile.RawFileName})");
                            continue;
                        }

                        var uExt = Path.GetExtension(uName);
                        var uSuffix = Path.GetFileNameWithoutExtension(uName);
                        var baseName = ProfileUtils.StripColorSuffixes(modelClass).ToLowerInvariant();
                        var reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}{uExt.ToLowerInvariant()}";

                        int collisionCounter = 1;
                        while (usedPaths.Contains(reconstructed))
                        {
                            collisionCounter++;
                            reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}_{collisionCounter}{uExt.ToLowerInvariant()}";
                            if (collisionCounter > 999)
                                break;
                        }
                        if (usedPaths.Contains(reconstructed))
                            continue;

                        Console.WriteLine($"  -> P3D ref match: {uFile.FileName}  =>  {reconstructed}");
                        result.RecoveredNames[ri] = reconstructed;
                        usedPaths.Add(reconstructed);
                        p3dRefRecovered++;
                    }

                    if (p3dRefRecovered > 0)
                        Console.WriteLine($"  -> P3D texture back-referencing: {p3dRefRecovered} filenames recovered");

                    // Post-loop RVMAT texture resolution: for recovered .rvmat entries,
                    // extract their texture paths by matching UNRECOVERED files in the
                    // same directory. This avoids Cyrillic encoding issues entirely by
                    // using directory + suffix + extension as the match key.
                    int rvmatTexRecovered = 0;
                    var rvmatDirClasses = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    for (int ri = 0; ri < total; ri++)
                    {
                        if (!result.RecoveredNames.TryGetValue(ri, out var recoveredName))
                            continue;
                        if (!recoveredName.EndsWith(".rvmat", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var rvmatDir = ProfileUtils.GetDirectoryName(recoveredName);
                        string rvmatClass;
                        try
                        {
                            rvmatClass = Path.GetFileNameWithoutExtension(recoveredName);
                        }
                        catch
                        {
                            continue;
                        }
                        if (string.IsNullOrEmpty(rvmatClass))
                            continue;

                        if (!rvmatDirClasses.TryGetValue(rvmatDir, out var classSet))
                        {
                            classSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            rvmatDirClasses[rvmatDir] = classSet;
                        }
                        classSet.Add(rvmatClass);
                    }

                    if (rvmatDirClasses.Count > 0)
                    {
                        for (int ri = 0; ri < total; ri++)
                        {
                            if (result.RecoveredNames.ContainsKey(ri))
                                continue;

                            var uFile = pbo.Files[ri];
                            var uDir = ProfileUtils.GetDirectoryName(uFile.FileName);
                            var uName = ProfileUtils.GetFileName(uFile.FileName);

                            if (!uName.StartsWith("_") && !uName.StartsWith("."))
                                continue;

                            if (!rvmatDirClasses.TryGetValue(uDir, out var candidates))
                            {
                                // Fallback: use P3D-derived directory→class map for directories
                                // whose RVMAT files couldn't be found via Cyrillic path matching
                                if (!rvmatDirClassMap.TryGetValue(uDir, out var dirClass))
                                    continue;
                                candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { dirClass };
                            }

                            var uExt = Path.GetExtension(uName);
                            var uSuffix = Path.GetFileNameWithoutExtension(uName);

                            foreach (var candidateClass in candidates)
                            {
                                var baseName = ProfileUtils.StripColorSuffixes(candidateClass).ToLowerInvariant();
                                var reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}{uExt.ToLowerInvariant()}";

                                int collisionCounter = 1;
                                while (usedPaths.Contains(reconstructed))
                                {
                                    collisionCounter++;
                                    reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}_{collisionCounter}{uExt.ToLowerInvariant()}";
                                    if (collisionCounter > 999)
                                        break;
                                }
                                if (usedPaths.Contains(reconstructed))
                                    continue;

                                Console.WriteLine($"  -> RVMAT dir match: {uFile.FileName}  =>  {reconstructed}");
                                result.RecoveredNames[ri] = reconstructed;
                                usedPaths.Add(reconstructed);
                                rvmatTexRecovered++;
                                break;
                            }
                        }

                        if (rvmatTexRecovered > 0)
                            Console.WriteLine($"  -> RVMAT directory resolution: {rvmatTexRecovered} filenames recovered");
                        p3dRefRecovered += rvmatTexRecovered;
                    }
                }

                recovered += p3dRefRecovered;
            }

            // Step 4d: knownPaths cross-referencing for remaining strays
            // knownPaths contains ALL candidate paths from config.bin, P3D, RVMAT, texHeaders.
            // These are clean (non-obfuscated) paths. Match strays against knownPaths by
            // shared directory + suffix + extension pattern to recover the original base name.
            int knownPathRecovered = 0;
            if (recovered < total && knownPaths.Count > 0)
            {
                // Build lookup: dir+suffix+ext → list of clean base names
                var knownPathLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kp in knownPaths)
                {
                    var kpDir = GetDirectoryName(kp);
                    var kpName = GetFileName(kp);
                    var kpExt = Path.GetExtension(kpName);
                    var kpWithoutExt = Path.GetFileNameWithoutExtension(kpName);
                    var kpSuffix = "";
                    // Skip knownPaths with non-ASCII base names (obfuscated/Cyrillic from RVMAT files)
                    // or wildcard chars (?/*) from obfuscated config.bin paths.
                    if (kpWithoutExt.Any(c => c > 127) || kpWithoutExt.Contains('?') || kpWithoutExt.Contains('*'))
                        continue;
                    var kpClean = kpWithoutExt;
                    var lastUnderscore = kpWithoutExt.LastIndexOf('_');
                    if (lastUnderscore > 0)
                    {
                        kpSuffix = kpWithoutExt.Substring(lastUnderscore);
                        kpClean = kpWithoutExt.Substring(0, lastUnderscore);
                    }
                    if (string.IsNullOrEmpty(kpClean))
                        continue;

                    var key = $"{kpDir}|{kpSuffix}|{kpExt}";
                    if (!knownPathLookup.TryGetValue(key, out var list))
                        knownPathLookup[key] = new List<string> { kpClean };
                    else if (!list.Contains(kpClean))
                        list.Add(kpClean);
                }

                for (int ri = 0; ri < total; ri++)
                {
                    if (result.RecoveredNames.ContainsKey(ri))
                        continue;

                    var uFile = pbo.Files[ri];
                    var uDir = GetDirectoryName(uFile.FileName);
                    var uName = GetFileName(uFile.FileName);
                    var uExt = Path.GetExtension(uName);
                    var uWithoutExt = Path.GetFileNameWithoutExtension(uName);
                    var uSuffix = "";
                    var uLastUnderscore = uWithoutExt.LastIndexOf('_');
                    if (uLastUnderscore > 0)
                        uSuffix = uWithoutExt.Substring(uLastUnderscore);
                    else if (uName.StartsWith("_"))
                        uSuffix = uWithoutExt; // Suffix-only: _co.paa → suffix is "_co"
                    else if (uName.StartsWith("."))
                        uSuffix = uWithoutExt; // Extension-only: .paa → suffix is ""

                    var key = $"{uDir}|{uSuffix}|{uExt}";
                    if (!knownPathLookup.TryGetValue(key, out var candidates))
                        continue;

                    var matchBase = candidates[0];
                    var reconstructed = $"{uDir}/{matchBase.ToLowerInvariant()}{uSuffix.ToLowerInvariant()}{uExt.ToLowerInvariant()}";

                    int collisionCounter = 1;
                    while (usedPaths.Contains(reconstructed))
                    {
                        collisionCounter++;
                        reconstructed = $"{uDir}/{matchBase.ToLowerInvariant()}{uSuffix.ToLowerInvariant()}_{collisionCounter}{uExt.ToLowerInvariant()}";
                        if (collisionCounter > 999)
                            break;
                    }
                    if (usedPaths.Contains(reconstructed))
                        continue;

                    Console.WriteLine($"  -> KnownPath match: {uFile.FileName}  =>  {reconstructed}");
                    result.RecoveredNames[ri] = reconstructed;
                    usedPaths.Add(reconstructed);
                    knownPathRecovered++;
                }

                if (knownPathRecovered > 0)
                    Console.WriteLine($"  -> KnownPath back-referencing: {knownPathRecovered} filenames recovered");
                recovered += knownPathRecovered;
            }

            // Step 4e: displayName cross-referencing for remaining strays
            int displayNameRecovered = 0;
            if (recovered < total && displayNameMap.Count > 0)
            {
                var displayNameWordIndex = BuildDisplayNameWordIndex(displayNameMap, prefix);

                for (int ri = 0; ri < total; ri++)
                {
                    if (result.RecoveredNames.ContainsKey(ri))
                        continue;

                    var uFile = pbo.Files[ri];
                    var uDir = GetDirectoryName(uFile.FileName);
                    var uName = GetFileName(uFile.FileName);

                    if (!uName.StartsWith("_") && !uName.StartsWith("."))
                        continue;

                    var dirWords = uDir.Split('/', '\\')
                        .SelectMany(w => w.Split('_'))
                        .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    string? matchedClass = null;
                    foreach (var dirWord in dirWords)
                    {
                        if (displayNameWordIndex.TryGetValue(dirWord, out var candidates))
                        {
                            matchedClass = candidates.OrderBy(c => c.Length).First();
                            break;
                        }
                    }

                    if (matchedClass == null)
                        continue;

                    var uExt = Path.GetExtension(uName);
                    var uSuffix = Path.GetFileNameWithoutExtension(uName);
                    var baseName = ProfileUtils.StripColorSuffixes(matchedClass).ToLowerInvariant();
                    var reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}{uExt.ToLowerInvariant()}";

                    int collisionCounter = 1;
                    while (usedPaths.Contains(reconstructed))
                    {
                        collisionCounter++;
                        reconstructed = $"{uDir}/{baseName}{uSuffix.ToLowerInvariant()}_{collisionCounter}{uExt.ToLowerInvariant()}";
                        if (collisionCounter > 999)
                            break;
                    }
                    if (usedPaths.Contains(reconstructed))
                        continue;

                    Console.WriteLine($"  -> displayName match: {uFile.FileName}  =>  {reconstructed}");
                    result.RecoveredNames[ri] = reconstructed;
                    usedPaths.Add(reconstructed);
                    displayNameRecovered++;
                }

                if (displayNameRecovered > 0)
                    Console.WriteLine($"  -> DisplayName cross-referencing: {displayNameRecovered} filenames recovered");
                recovered += displayNameRecovered;
            }

            result.Stats["Recovered"] = recovered;
            result.Stats["Total"] = total;
            result.Stats["Unrecovered"] = total - recovered;
            Console.WriteLine($"  -> Recovery complete. {recovered}/{total} filenames recovered from config.bin context.");

            return result;
        }

        /// <summary>
        /// Recursively walks a parsed raP class tree and collects all string values
        /// that look like file paths (contain directory separators and a file extension).
        /// </summary>
        private static List<string> ExtractPathsFromRap(ParamClass cls)
        {
            var results = new List<string>();
            var pathPattern = new Regex(
                @".+\.[a-zA-Z0-9]{2,5}$",
                RegexOptions.Compiled);

            CollectStringValues(cls, results, pathPattern);
            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Accepts paths containing Cyrillic obfuscation chars (> 127) and wildcards (?/*)
        /// which are common in obfuscated config.bin paths. Only rejects control chars
        /// and the Unicode replacement character (bad UTF-8 decoding).
        /// </summary>
        private static bool IsValidPathString(string s) =>
            s.All(c => c >= 32 && c != '\uFFFD');

        private static void CollectStringValues(ParamClass cls, List<string> results, Regex pathPattern)
        {
            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass nested:
                        CollectStringValues(nested, results, pathPattern);
                        break;

                    case ParamValue pv when pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression:
                        var strVal = pv.Value.Value as string;
                        if (!string.IsNullOrEmpty(strVal) && strVal.Contains('\\') && pathPattern.IsMatch(strVal) && IsValidPathString(strVal))
                            results.Add(strVal.Replace('/', '\\'));
                        break;

                    case ParamArray pa:
                        foreach (var rv in pa.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && pathPattern.IsMatch(s) && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;

                    case ParamArraySpec pas:
                        foreach (var rv in pas.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && pathPattern.IsMatch(s) && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;
                }
            }
        }

        private static void CollectBinaryStringValues(ParamClass cls, List<string> results)
        {
            foreach (var entry in cls.Entries)
            {
                switch (entry)
                {
                    case ParamClass nested:
                        CollectBinaryStringValues(nested, results);
                        break;

                    case ParamValue pv when pv.Value.Type == ConfigValueType.Generic || pv.Value.Type == ConfigValueType.Expression:
                        var strVal = pv.Value.Value as string;
                        if (!string.IsNullOrEmpty(strVal) && strVal.Contains('\\') && strVal.Length > 3 && IsValidPathString(strVal))
                            results.Add(strVal.Replace('/', '\\'));
                        break;

                    case ParamArray pa:
                        foreach (var rv in pa.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && s.Length > 3 && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;

                    case ParamArraySpec pas:
                        foreach (var rv in pas.Array.Entries)
                        {
                            if ((rv.Type == ConfigValueType.Generic || rv.Type == ConfigValueType.Expression) && rv.Value is string s)
                            {
                                if (!string.IsNullOrEmpty(s) && s.Contains('\\') && s.Length > 3 && IsValidPathString(s))
                                    results.Add(s.Replace('/', '\\'));
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Recursively collects all class names from the raP tree.
        /// Filters out common BIS config class names and short names.
        /// </summary>
        private static List<string> ExtractClassNames(ParamClass root)
        {
            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CfgPatches", "CfgWeapons", "CfgVehicles", "CfgMagazines",
                "CfgAmmo", "CfgGlasses", "CfgUnitInsignia", "CfgFactionClasses",
                "CfgEditorCategories", "CfgEditorSubcategories", "CfgMods",
                "XtdGearModels", "XtdGearInfos", "units", "weapons", "items",
                "containers", "accessories", "ammunition", "grenades",
                "launchers", "missiles", "bombs", "mines", "explosives",
                "throw", "put", "Default", "WeaponSlotInfo", "SlotInfo",
                "PointerSlot", "MuzzleSlot", "CowsSlot", "UnderbarrelSlot",
                "MuzzleCoef", "AmmoCoef", "MagazineCoef",
                "CfgNonAIVehicles", "CfgMarkerColors", "CfgMarkerShapes",
                "CfgWaypoints", "CfgActionSounds", "CfgCloudlets",
                "CfgSoundShaders", "CfgSoundSets", "CfgUnitSounds",
                "CfgMusic", "CfgRadio", "CfgVoice", "CfgSFX",
                "CfgWorlds", "CfgWorldList"
            };

            CollectClassNames(root, results, seen, exclude);
            return results;
        }

        private static void CollectClassNames(ParamClass cls, List<string> names, HashSet<string> seen, HashSet<string> exclude)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    if (nested.Name != null &&
                        nested.Name.Length >= 3 &&
                        !exclude.Contains(nested.Name) &&
                        seen.Add(nested.Name))
                    {
                        names.Add(nested.Name);
                    }
                    CollectClassNames(nested, names, seen, exclude);
                }
            }
        }

        /// Detects a common mod prefix from class names (e.g., "jsoar_", "uksf_").
        /// Class-name frequency analysis (>70%), with fallback from PBO prefix property.
        private static string? DetectModPrefix(List<string> classNames, string pboPrefix)
        {
            if (classNames.Count == 0)
                return null;

            var firstWords = classNames
                .Select(n => n.Split('_')[0])
                .Where(w => w.Length >= 2)
                .GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Word = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            if (firstWords.Count > 0)
            {
                var mostCommon = firstWords[0];
                if ((double)mostCommon.Count / classNames.Count >= 0.7)
                    return mostCommon.Word + "_";
            }

            // Fallback: derive tag from PBO prefix property
            if (!string.IsNullOrEmpty(pboPrefix))
            {
                var parts = pboPrefix.Split(new[] { '\\', '/' });
                var lastPart = parts[^1];
                var tag = lastPart.Split('_')[0];
                if (tag.Length >= 2 && classNames.Any(n =>
                    n.StartsWith(tag + "_", StringComparison.OrdinalIgnoreCase)))
                    return tag.ToLowerInvariant() + "_";
            }

            return null;
        }

        /// <summary>
        /// Builds a mapping from directory words (e.g., "avs", "abav") to class names
        /// that contain those words. Strips the PBO prefix and mod prefix from class names
        /// before matching, and stores the stripped names for cleaner output filenames.
        /// </summary>
        private static Dictionary<string, List<string>> BuildSuffixToClassMap(List<string> classNames, string prefix, string? modPrefix = null)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var cls in classNames)
            {
                var normalized = cls;

                if (!string.IsNullOrEmpty(modPrefix) &&
                    normalized.StartsWith(modPrefix, StringComparison.OrdinalIgnoreCase))
                    normalized = normalized.Substring(modPrefix.Length);

                if (!string.IsNullOrEmpty(prefix))
                {
                    var prefixClean = prefix.TrimEnd('/');
                    // Only strip PBO prefix if followed by underscore to prevent
                    // partial-word matches (e.g., prefix "avs" should not match
                    // class segment "avs_assault_vest" unless explicitly "avs_").
                    if (normalized.StartsWith(prefixClean + "_", StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(prefixClean.Length + 1).TrimStart('/');
                }

                if (string.IsNullOrEmpty(normalized))
                    continue;

                var words = normalized.Split('_')
                    .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var word in words)
                {
                    if (!map.TryGetValue(word, out var list))
                    {
                        map[word] = new List<string> { normalized };
                    }
                    else if (!list.Contains(normalized))
                    {
                        list.Add(normalized);
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// Parses config.bin to build a map from model path → parent class name.
        /// Config.bin stores model paths with the PBO prefix (e.g. "jsoar\model\....p3d").
        /// PBO entry raw names store the relative path without the prefix ("model\....p3d").
        /// This normalises both sides and picks the shortest class name for each unique model.
        /// </summary>
        private static Dictionary<string, string> BuildConfigModelMap(ParamClass root, string pboPrefix)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var prefix = (pboPrefix ?? "").Replace('\\', '/').TrimEnd('/');
            CollectModelPaths(root, map, prefix);
            return map;
        }

        private static void CollectModelPaths(ParamClass cls, Dictionary<string, string> result, string pboPrefix)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            val.Name == "model" &&
                            val.Value?.Value is string sv &&
                            sv.EndsWith(".p3d", StringComparison.OrdinalIgnoreCase))
                        {
                            var rawPath = sv.Replace('\\', '/');
                            var relPath = rawPath;
                            if (!string.IsNullOrEmpty(pboPrefix) &&
                                relPath.StartsWith(pboPrefix + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                relPath = relPath.Substring(pboPrefix.Length + 1);
                            }
                            if (!result.TryGetValue(relPath, out var existing) ||
                                nested.Name.Length < existing.Length)
                            {
                                result[relPath] = nested.Name;
                            }
                        }
                    }
                    CollectModelPaths(nested, result, pboPrefix);
                }
            }
        }

        /// <summary>
        /// Builds a map from image path → parent class (variant) name.
        /// Enables semantic naming of icon files (e.g. acex_001.paa → acex/mc.paa).
        /// </summary>
        private static Dictionary<string, string> BuildConfigImageMap(ParamClass root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectImagePaths(root, map);
            return map;
        }

        private static void CollectImagePaths(ParamClass cls, Dictionary<string, string> result)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            string.Equals(val.Name, "image", StringComparison.OrdinalIgnoreCase) &&
                            val.Value?.Value is string sv &&
                            sv.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                        {
                            var rawPath = sv.Replace('\\', '/');
                            var slashIdx = rawPath.IndexOf('/');
                            var relPath = slashIdx >= 0 ? rawPath.Substring(slashIdx + 1) : rawPath;
                            if (!result.ContainsKey(relPath))
                                result[relPath] = nested.Name.ToLowerInvariant();
                        }
                    }
                    CollectImagePaths(nested, result);
                }
            }
        }

        /// <summary>
        /// Builds a map from picture path → parent class name.
        /// Enables semantic naming of icon files (e.g. data/.paa → data/jsoar_avs.paa).
        /// Uses explicit PBO prefix stripping (same approach as BuildConfigModelMap)
        /// rather than the first-segment stripping used by BuildConfigImageMap.
        /// </summary>
        private static Dictionary<string, string> BuildConfigPictureMap(ParamClass root, string pboPrefix)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var prefix = (pboPrefix ?? "").Replace('\\', '/').TrimEnd('/');
            CollectPicturePaths(root, map, prefix);
            return map;
        }

        private static void CollectPicturePaths(ParamClass cls, Dictionary<string, string> result, string pboPrefix)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            string.Equals(val.Name, "picture", StringComparison.OrdinalIgnoreCase) &&
                            val.Value?.Value is string sv &&
                            sv.EndsWith(".paa", StringComparison.OrdinalIgnoreCase))
                        {
                            var rawPath = sv.Replace('\\', '/');
                            var relPath = rawPath;
                            if (!string.IsNullOrEmpty(pboPrefix) &&
                                relPath.StartsWith(pboPrefix + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                relPath = relPath.Substring(pboPrefix.Length + 1);
                            }
                            if (!result.ContainsKey(relPath))
                                result[relPath] = nested.Name;
                        }
                    }
                    CollectPicturePaths(nested, result, pboPrefix);
                }
            }
        }

        /// <summary>
        /// Builds a map from class name → displayName by parsing config.bin for
        /// displayName properties. Enables validation and fallback recovery by
        /// cross-referencing in-game item names against class names.
        /// </summary>
        private static Dictionary<string, string> BuildConfigDisplayNameMap(ParamClass root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CollectDisplayNames(root, map);
            return map;
        }

        private static void CollectDisplayNames(ParamClass cls, Dictionary<string, string> result)
        {
            foreach (var entry in cls.Entries)
            {
                if (entry is ParamClass nested)
                {
                    foreach (var e in nested.Entries)
                    {
                        if (e is ParamValue val &&
                            string.Equals(val.Name, "displayName", StringComparison.OrdinalIgnoreCase) &&
                            val.Value?.Value is string sv &&
                            !string.IsNullOrEmpty(sv))
                        {
                            if (!result.ContainsKey(nested.Name))
                                result[nested.Name] = sv;
                        }
                    }
                    CollectDisplayNames(nested, result);
                }
            }
        }

        /// <summary>
        /// Builds a word index from displayName strings to class names for
        /// fuzzy matching. Tokenizes displayNames by splitting on spaces and
        /// punctuation, then filters to meaningful words (≥3 chars, not all digits).
        /// </summary>
        private static Dictionary<string, List<string>> BuildDisplayNameWordIndex(
            Dictionary<string, string> displayNameMap, string? prefix)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in displayNameMap)
            {
                var className = kvp.Key;
                var normalized = className;
                if (!string.IsNullOrEmpty(prefix))
                {
                    var prefixClean = prefix.TrimEnd('/');
                    if (normalized.StartsWith(prefixClean + "_", StringComparison.OrdinalIgnoreCase))
                        normalized = normalized.Substring(prefixClean.Length + 1).TrimStart('/');
                }

                var displayWords = TokenizeDisplayName(kvp.Value);
                foreach (var word in displayWords)
                {
                    if (!index.TryGetValue(word, out var list))
                    {
                        index[word] = new List<string> { normalized };
                    }
                    else if (!list.Contains(normalized))
                    {
                        list.Add(normalized);
                    }
                }
            }
            return index;
        }

        private static List<string> TokenizeDisplayName(string displayName)
        {
            return displayName.ToLowerInvariant()
                .Split(new[] { ' ', '(', ')', '[', ']', '-', '/', '\\', '\'', '"', ',' },
                       StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
