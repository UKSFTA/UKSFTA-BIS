<#
.SYNOPSIS
    Fetch Bohemia Interactive Licensed Data Packages for format testing.

.DESCRIPTION
    Downloads ALDP (Arma Licensed Data Pack) PBOs from BI's CDN and extracts
    representative files into the _testdata/ format directories.

    By default, downloads only the smallest packages (CWC + A1 part 1).
    Use -All to download everything (several GB).

    Requires: PowerShell 5.1+ (Invoke-WebRequest, Expand-Archive)

    License: All data remains under Bohemia Interactive's original licenses
    (APL, APL-SA, ADPL-SA, DPL). See _testdata/README.md for details.

.PARAMETER All
    Download all available packages (CWC, A1, A2, A2OA, DayZ).

.PARAMETER Cwc
    Download Arma: Cold War Crisis PBOs only.

.PARAMETER A1
    Download Arma 1 PBOs only.

.PARAMETER A2
    Download Arma 2 PBOs only.

.PARAMETER A2Oa
    Download Arma 2: Operation Arrowhead PBOs only.

.PARAMETER DayZ
    Download DayZ Mod PBOs only.

.EXAMPLE
    .\download.ps1
    Downloads CWC + A1 packages (small, fast).

.EXAMPLE
    .\download.ps1 -All
    Downloads all available packages (several GB).

.EXAMPLE
    .\download.ps1 -A2 -A2Oa
    Downloads A2 and OA packages only.
#>

param(
    [switch]$All,
    [switch]$Cwc,
    [switch]$A1,
    [switch]$A2,
    [switch]$A2Oa,
    [switch]$DayZ
)

$ErrorActionPreference = "Stop"

$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) {
    $ScriptDir = Get-Location
}
Set-Location $ScriptDir

$BaseUrl = "https://tr4.bistudio.com"

# Determine flags
if ($All) {
    $FetchCwc = $true
    $FetchA1  = $true
    $FetchA2  = $true
    $FetchA2Oa = $true
    $FetchDayZ = $true
} else {
    $FetchCwc = $Cwc
    $FetchA1  = $A1
    $FetchA2  = $A2
    $FetchA2Oa = $A2Oa
    $FetchDayZ = $DayZ
}

# Default: small packages only
if (-not ($FetchCwc -or $FetchA1 -or $FetchA2 -or $FetchA2Oa -or $FetchDayZ)) {
    $FetchCwc = $true
    $FetchA1  = $true
}

# Format tracking
$Formats = @{}

function Register-Format {
    param([string]$Format)
    if ($Formats.ContainsKey($Format)) {
        $Formats[$Format]++
    } else {
        $Formats[$Format] = 1
    }
}

function Download-AndExtract {
    param([string]$Url, [string]$Name)

    $zipPath = "sources/$Name.zip"
    $extractDir = "sources/${Name}_extracted"

    if (Test-Path $zipPath) {
        Write-Host "  [SKIP] $Name already downloaded"
    } else {
        Write-Host "  [DL]   $Name..."
        New-Item -ItemType Directory -Force -Path "sources" | Out-Null
        Invoke-WebRequest -Uri $Url -OutFile $zipPath
    }

    if (Test-Path $extractDir) {
        Write-Host "  [SKIP] $Name already extracted"
    } else {
        Write-Host "  [EXTR] $Name..."
        New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
        Expand-Archive -Path $zipPath -DestinationPath $extractDir
    }
}

function Copy-Files {
    param(
        [string]$SrcDir,
        [string]$DstDir,
        [string]$Pattern,
        [int]$MaxFiles = 5
    )

    New-Item -ItemType Directory -Force -Path $DstDir | Out-Null
    $count = 0

    if (-not (Test-Path $SrcDir)) { return }

    Get-ChildItem -Path $SrcDir -Recurse -File -Filter $Pattern | ForEach-Object {
        if ($count -ge $MaxFiles) { break }
        $dstPath = Join-Path $DstDir $_.Name
        if (-not (Test-Path $dstPath)) {
            Copy-Item -Path $_.FullName -Destination $dstPath -NoClobber
            $ext = [System.IO.Path]::GetExtension($Pattern).TrimStart('.')
            Register-Format -Format $ext
        }
        $count++
    }
}

Write-Host ""
Write-Host "=== Bohemia Licensed Data Package Downloader ==="
Write-Host ""

# ──────────────────────────────────────────────
# CWC (most basic format)
# ──────────────────────────────────────────────
if ($FetchCwc) {
    Write-Host "[CWC] Arma: Cold War Crisis PBOs"
    Download-AndExtract -Url "${BaseUrl}/ALDP_CWC_PBOs_ADPL-SA_APL-SA.zip" -Name "ALDP_CWC_PBOs"
    Copy-Files -SrcDir "sources/ALDP_CWC_PBOs_extracted" -DstDir "pbo" -Pattern "*.pbo" -MaxFiles 5
    Write-Host ""
}

# ──────────────────────────────────────────────
# Arma 1
# ──────────────────────────────────────────────
if ($FetchA1) {
    Write-Host "[A1] Arma 1 PBOs"
    Download-AndExtract -Url "${BaseUrl}/ALDP_A1_PBOs_ADPL-SA_APL-SA_part1.zip" -Name "ALDP_A1_PBOs_part1"
    Copy-Files -SrcDir "sources/ALDP_A1_PBOs_part1_extracted" -DstDir "pbo" -Pattern "*.pbo" -MaxFiles 10
    Write-Host ""
}

# ──────────────────────────────────────────────
# Arma 2
# ──────────────────────────────────────────────
if ($FetchA2) {
    Write-Host "[A2] Arma 2 PBOs"
    Download-AndExtract -Url "${BaseUrl}/ALDP_A2_PBOs_ADPL-SA_APL-SA_part1.zip" -Name "ALDP_A2_PBOs_part1"
    Copy-Files -SrcDir "sources/ALDP_A2_PBOs_part1_extracted" -DstDir "pbo" -Pattern "*.pbo" -MaxFiles 10
    Write-Host ""
}

# ──────────────────────────────────────────────
# Arma 2: Operation Arrowhead
# ──────────────────────────────────────────────
if ($FetchA2Oa) {
    Write-Host "[A2OA] Arma 2: OA PBOs"
    Download-AndExtract -Url "${BaseUrl}/ALDP_A2OA_PBOs_ADPL-SA_APL-SA.zip" -Name "ALDP_A2OA_PBOs"
    Copy-Files -SrcDir "sources/ALDP_A2OA_PBOs_extracted" -DstDir "pbo" -Pattern "*.pbo" -MaxFiles 10
    Write-Host ""
}

# ──────────────────────────────────────────────
# DayZ Mod
# ──────────────────────────────────────────────
if ($FetchDayZ) {
    Write-Host "[DAYZ] DayZ Mod PBOs"
    Download-AndExtract -Url "${BaseUrl}/DAYZ_MOD_PBOs_ADPL-SA.zip" -Name "DAYZ_MOD_PBOs"
    Copy-Files -SrcDir "sources/DAYZ_MOD_PBOs_extracted" -DstDir "pbo" -Pattern "*.pbo" -MaxFiles 5
    Write-Host ""
}

# ──────────────────────────────────────────────
# Extract individual format files from PBOs
# ──────────────────────────────────────────────
Write-Host "[EXTR] Extracting individual format files from PBOs..."
$PboDir = Join-Path $ScriptDir "pbo"

if (Test-Path $PboDir) {
    $pboFiles = Get-ChildItem -Path $PboDir -Filter "*.pbo"
    if ($pboFiles.Count -gt 0) {
        # Build PboExtract from library source (no dependency on Utils tooling)
        $extractorDir = Join-Path $ScriptDir "PboExtract"
        $extractorOut = Join-Path $extractorDir "bin\Debug\net10.0\PboExtract.dll"

        if (-not (Test-Path $extractorOut)) {
            Write-Host "  [BUILD] Building PboExtract from library source..."
            & dotnet build $extractorDir --configuration Debug 2>$null
        }

        if (Test-Path $extractorOut) {
            Write-Host "  Using: PboExtract (library-based)"
            $extractBase = Join-Path $ScriptDir "sources/pbo_extracted"
            New-Item -ItemType Directory -Force -Path $extractBase | Out-Null

            foreach ($pbo in $pboFiles) {
                $name = [System.IO.Path]::GetFileNameWithoutExtension($pbo.Name)
                $extractDir = Join-Path $extractBase $name
                New-Item -ItemType Directory -Force -Path $extractDir | Out-Null

                Write-Host "  Extracting: $name.pbo"
                try {
                    & dotnet $extractorOut $pbo.FullName $extractDir 2>$null
                } catch {
                    # Extraction failures are non-fatal
                }

                Copy-Files -SrcDir $extractDir -DstDir (Join-Path $ScriptDir "paa") -Pattern "*.paa" -MaxFiles 10
                Copy-Files -SrcDir $extractDir -DstDir (Join-Path $ScriptDir "p3d") -Pattern "*.p3d" -MaxFiles 5
                Copy-Files -SrcDir $extractDir -DstDir (Join-Path $ScriptDir "config") -Pattern "config.bin" -MaxFiles 3
                Copy-Files -SrcDir $extractDir -DstDir (Join-Path $ScriptDir "rvmat") -Pattern "*.rvmat" -MaxFiles 5
            }
        } else {
        Write-Host "  [WARN] PboExtract build failed — build it manually with:"
        Write-Host "    dotnet build `"$extractorDir`" --configuration Debug"
            Write-Host "  Individual format files (PAA, P3D, config.bin) won't be extracted."
        }
    } else {
        Write-Host "  No PBOs downloaded yet, skipping extraction."
    }
} else {
    Write-Host "  No PBO directory found, skipping extraction."
}

# ──────────────────────────────────────────────
# Summary
# ──────────────────────────────────────────────
Write-Host ""
Write-Host "=== Summary ==="
foreach ($key in $Formats.Keys) {
    Write-Host ("  {0}: {1} files" -f $key.ToUpper(), $Formats[$key])
}
Write-Host ""

Write-Host "Arma 3 Samples (symlink to use):"
Write-Host "  New-Item -ItemType SymbolicLink -Path \"$ScriptDir/sources/arma3-samples\" -Target \"C:\Path\To\Steam\steamapps\common\Arma 3 Samples\""
Write-Host "  (Requires admin/developer mode on Windows for symlinks)"
Write-Host ""

Write-Host "Arma Reforger Samples (source only — no compiled PAK files):"
Write-Host "  git clone https://github.com/BohemiaInteractive/Arma-Reforger-Samples.git sources/reforger-samples"
Write-Host ""

Write-Host "DayZ Samples (source only — no compiled PAK files):"
Write-Host "  git clone https://github.com/BohemiaInteractive/DayZ-Samples.git sources/dayz-samples"
Write-Host ""

Write-Host "For Reforger/DayZ PAK files, symlink to your game installation:"
Write-Host "  New-Item -ItemType SymbolicLink -Path \"$ScriptDir/pak/reforger\" -Target \"C:\Path\To\ArmaReforger\Data\""
Write-Host "  New-Item -ItemType SymbolicLink -Path \"$ScriptDir/pak/dayz\" -Target \"C:\Path\To\DayZ\Data\""
Write-Host ""
