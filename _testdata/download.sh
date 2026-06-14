#!/usr/bin/env bash
# download.sh — Fetch Bohemia Interactive Licensed Data Packages for format testing.
#
# Downloads ALDP (Arma Licensed Data Pack) PBOs from BI's CDN and extracts
# representative files into the _testdata/ format directories.
#
# Usage:
#   ./download.sh [--all] [--cwc] [--a1] [--a2] [--a2oa] [--dayz]
#
# By default, downloads only the smallest packages (CWC + A1 part 1).
# Use --all to download everything (several GB).
#
# Requires: curl, unzip
#
# License: All data remains under Bohemia Interactive's original licenses
# (APL, APL-SA, ADPL-SA, DPL). See _testdata/README.md for details.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

BASE_URL="https://tr4.bistudio.com"
DOWNLOAD_ALL=false
FETCH_CWC=false
FETCH_A1=false
FETCH_A2=false
FETCH_A2OA=false
FETCH_DAYZ=false

# Parse arguments
for arg in "$@"; do
    case "$arg" in
        --all)    DOWNLOAD_ALL=true ;;
        --cwc)    FETCH_CWC=true ;;
        --a1)     FETCH_A1=true ;;
        --a2)     FETCH_A2=true ;;
        --a2oa)   FETCH_A2OA=true ;;
        --dayz)   FETCH_DAYZ=true ;;
        --help)
            echo "Usage: $0 [--all] [--cwc] [--a1] [--a2] [--a2oa] [--dayz]"
            exit 0
            ;;
    esac
done

if [ "$DOWNLOAD_ALL" = true ]; then
    FETCH_CWC=true
    FETCH_A1=true
    FETCH_A2=true
    FETCH_A2OA=true
    FETCH_DAYZ=true
fi

# Default: small packages only
if [ "$FETCH_CWC" = false ] && [ "$FETCH_A1" = false ] && [ "$FETCH_A2" = false ] && [ "$FETCH_A2OA" = false ] && [ "$FETCH_DAYZ" = false ]; then
    FETCH_CWC=true
    FETCH_A1=true
fi

download_and_extract() {
    local url="$1"
    local name="$2"
    local zip_path="sources/${name}.zip"

    if [ -f "$zip_path" ]; then
        echo "  [SKIP] $name already downloaded"
    else
        echo "  [DL]   $name..."
        mkdir -p sources
        curl -fSLo "$zip_path" "$url"
    fi

    local extract_dir="sources/${name}_extracted"
    if [ -d "$extract_dir" ]; then
        echo "  [SKIP] $name already extracted"
    else
        echo "  [EXTR] $name..."
        mkdir -p "$extract_dir"
        unzip -qo "$zip_path" -d "$extract_dir"
    fi
}

# Track format coverage
declare -A FORMATS

register_format() {
    local format="$1"
    FORMATS["$format"]=$(( ${FORMATS["$format"]-0} + 1))
}

# Copy files up to a limit to avoid bloating the testdata dir
copy_files() {
    local src_dir="$1"
    local dst_dir="$2"
    local pattern="$3"
    local max_files="${4:-5}"

    mkdir -p "$dst_dir"
    local count=0
    while IFS= read -r -d '' file; do
        if [ "$count" -ge "$max_files" ]; then
            break
        fi
        local basename
        basename="$(basename "$file")"
        # Avoid overwriting
        if [ ! -f "$dst_dir/$basename" ]; then
            cp -n "$file" "$dst_dir/$basename"
            register_format "${pattern#*.}"
        fi
        count=$((count + 1))
    done < <(find "$src_dir" -type f -iname "$pattern" -print0 2>/dev/null)
}

echo ""
echo "=== Bohemia Licensed Data Package Downloader ==="
echo ""

# ──────────────────────────────────────────────
# CWC (most basic format)
# ──────────────────────────────────────────────
if [ "$FETCH_CWC" = true ]; then
    echo "[CWC] Arma: Cold War Crisis PBOs"
    download_and_extract "${BASE_URL}/ALDP_CWC_PBOs_ADPL-SA_APL-SA.zip" "ALDP_CWC_PBOs"
    copy_files "sources/ALDP_CWC_PBOs_extracted" "pbo" "*.pbo" 5
    echo ""
fi

# ──────────────────────────────────────────────
# Arma 1
# ──────────────────────────────────────────────
if [ "$FETCH_A1" = true ]; then
    echo "[A1] Arma 1 PBOs"
    download_and_extract "${BASE_URL}/ALDP_A1_PBOs_ADPL-SA_APL-SA_part1.zip" "ALDP_A1_PBOs_part1"
    copy_files "sources/ALDP_A1_PBOs_part1_extracted" "pbo" "*.pbo" 10
    echo ""
fi

# ──────────────────────────────────────────────
# Arma 2
# ──────────────────────────────────────────────
if [ "$FETCH_A2" = true ]; then
    echo "[A2] Arma 2 PBOs"
    download_and_extract "${BASE_URL}/ALDP_A2_PBOs_ADPL-SA_APL-SA_part1.zip" "ALDP_A2_PBOs_part1"
    copy_files "sources/ALDP_A2_PBOs_part1_extracted" "pbo" "*.pbo" 10
    echo ""
fi

# ──────────────────────────────────────────────
# Arma 2: Operation Arrowhead
# ──────────────────────────────────────────────
if [ "$FETCH_A2OA" = true ]; then
    echo "[A2OA] Arma 2: OA PBOs"
    download_and_extract "${BASE_URL}/ALDP_A2OA_PBOs_ADPL-SA_APL-SA.zip" "ALDP_A2OA_PBOs"
    copy_files "sources/ALDP_A2OA_PBOs_extracted" "pbo" "*.pbo" 10
    echo ""
fi

# ──────────────────────────────────────────────
# DayZ Mod
# ──────────────────────────────────────────────
if [ "$FETCH_DAYZ" = true ]; then
    echo "[DAYZ] DayZ Mod PBOs"
    download_and_extract "${BASE_URL}/DAYZ_MOD_PBOs_ADPL-SA.zip" "DAYZ_MOD_PBOs"
    copy_files "sources/DAYZ_MOD_PBOs_extracted" "pbo" "*.pbo" 5
    echo ""
fi

# ──────────────────────────────────────────────
# Extract individual format files from PBOs
# ──────────────────────────────────────────────
echo "[EXTR] Extracting individual format files from PBOs..."
PBO_DIR="$SCRIPT_DIR/pbo"
if ls "$PBO_DIR"/*.pbo 2>/dev/null >/dev/null; then
    # Build the standalone PboExtract tool using the BIS.PBO library directly
    EXTRACTOR_DIR="$SCRIPT_DIR/PboExtract"
    EXTRACTOR_OUT="$EXTRACTOR_DIR/bin/Debug/net10.0/PboExtract.dll"

    if [ ! -f "$EXTRACTOR_OUT" ]; then
    echo "  [BUILD] Building PboExtract from library source..."
    dotnet build "$EXTRACTOR_DIR" --configuration Debug 2>/dev/null || true
    fi

    if [ -f "$EXTRACTOR_OUT" ]; then
        echo "  Using: PboExtract (library-based)"
        for pbo in "$PBO_DIR"/*.pbo; do
            name="$(basename "$pbo" .pbo)"
            extract_base="$SCRIPT_DIR/sources/pbo_extracted/$name"
            mkdir -p "$extract_base"

            echo "  Extracting: $name.pbo"
            dotnet "$EXTRACTOR_OUT" "$pbo" "$extract_base" 2>/dev/null || true

            # Copy format files
            copy_files "$extract_base" "$SCRIPT_DIR/paa" "*.paa" 10
            copy_files "$extract_base" "$SCRIPT_DIR/p3d" "*.p3d" 5
            copy_files "$extract_base" "$SCRIPT_DIR/config" "config.bin" 3
            copy_files "$extract_base" "$SCRIPT_DIR/rvmat" "*.rvmat" 5
        done
    else
    echo "  [WARN] PboExtract build failed — build it manually with:"
    echo "    dotnet build \"$EXTRACTOR_DIR\" --configuration Debug"
        echo "  Individual format files (PAA, P3D, config.bin) won't be extracted."
    fi
else
    echo "  No PBOs downloaded yet, skipping extraction."
fi

# ──────────────────────────────────────────────
# Summary
# ──────────────────────────────────────────────
echo ""
echo "=== Summary ==="
for fmt in "${!FORMATS[@]}"; do
    echo "  ${fmt^^}: ${FORMATS[$fmt]} files"
done
echo ""
echo "Arma 3 Samples (symlink to use):"
echo "  ln -sf \"/path/to/Steam/steamapps/common/Arma 3 Samples\" \"$SCRIPT_DIR/sources/arma3-samples\""
echo ""
echo "Arma Reforger Samples (source only — no compiled PAK files):"
echo "  git clone https://github.com/BohemiaInteractive/Arma-Reforger-Samples.git sources/reforger-samples"
echo ""
echo "DayZ Samples (source only — no compiled PAK files):"
echo "  git clone https://github.com/BohemiaInteractive/DayZ-Samples.git sources/dayz-samples"
echo ""
echo "For Reforger/DayZ PAK files, symlink to your game installation:"
echo "  ln -sf \"/path/to/ArmaReforger/Data/\" \"$SCRIPT_DIR/pak/reforger\""
echo "  ln -sf \"/path/to/DayZ/Data/\" \"$SCRIPT_DIR/pak/dayz\""
echo ""
