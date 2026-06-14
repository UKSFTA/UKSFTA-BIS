# Test Data Directory

This directory contains test data files from multiple Arma game generations for
integration testing of all BIS format parsers (PBO, PAA, P3D, RTM, WRP, SQFC,
LIP, ALB, config.bin, RVMAT, PAK).

## Quick Start

```bash
# Download CWC + Arma 1 PBOs (small, fast)
./download.sh

# Or get everything
./download.sh --all

# On Windows (PowerShell):
# .\download.ps1
# .\download.ps1 -All
```

The `download.sh` / `download.ps1` scripts download ALDP packages from Bohemia
Interactive's CDN. No Steam or game installation is required for the basic
format test data.

For broader coverage (P3D models, PAA textures, RTM animations), you can
symlink or copy files from your local game installation — see the
symlinking section below.

## Data Sources

| Package | Source | Formats | License |
|---|---|---|---|
| ALDP CWC PBOs | [tr4.bistudio.com] | PBO | ADPL-SA / APL-SA |
| ALDP A1 PBOs | [tr4.bistudio.com] | PBO | ADPL-SA / APL-SA |
| ALDP A2 PBOs | [tr4.bistudio.com] | PBO | ADPL-SA / APL-SA |
| ALDP A2OA PBOs | [tr4.bistudio.com] | PBO | ADPL-SA / APL-SA |
| DayZ Mod PBOs | [tr4.bistudio.com] | PBO | ADPL-SA |
| Arma 3 Samples | [Steam] | P3D, PAA, RTM, RVMAT, config source | APL |
| Arma Reforger Samples | [GitHub] | Source only (build produces PAK) | APL |
| DayZ Samples | [GitHub] | Source only | APL |

[tr4.bistudio.com]: https://tr4.bistudio.com/
[Steam]: https://store.steampowered.com/app/390500/Arma_3_Samples/
[GitHub]: https://github.com/BohemiaInteractive/Arma-Reforger-Samples

## Directory Layout

```
_testdata/
├── download.sh          # Linux/macOS script to fetch ALDP packages and extract format files
├── download.ps1         # Windows PowerShell equivalent of download.sh
├── README.md            # This file
├── .gitignore           # Ignores all binary data
│
├── pbo/                 # Real Arma PBO files (from ALDP packages)
├── paa/                 # Extracted PAA textures
├── p3d/                 # Extracted P3D models
├── rtm/                 # RTM animation files (from game Samples or PBOs)
├── config/              # config.bin files (from PBO extraction or ALDP)
├── rvmat/               # Material files
├── wrp/                 # World files (from A2/A3 PBOs)
├── sqfc/                # Compiled SQF bytecode
├── lip/                 # Lip-sync files
├── alb/                 # ALB bytecode files
├── sign/                # BISign / bikey / biprivatekey files
├── pak/                 # Enfusion PAK archives (from Reforger/DayZ)
│
└── sources/             # Raw downloads and extracted archives
    ├── arma3-samples -> # Symlink to local Arma 3 Samples installation
    ├── ALDP_*_extracted/
    └── ALDP_*.zip
```

## Integration Test Setup

The integration tests (`BIS.IntegrationTest`) discover test data at runtime by
checking these directories. If a file is not found, the relevant test is skipped
with a clear message telling the user what to download.

## Linking Your Game Data

If you have Arma 3, DayZ, or Arma Reforger installed, you can symlink or
copy files from your local game installation for broader format coverage.
Adjust the paths below to match where your games are installed.

### Linux / macOS

```bash
# Arma 3 Samples (the SDK/tool samples, not the game itself — installable via Steam)
ln -sf "/path/to/Steam/steamapps/common/Arma 3 Samples" sources/arma3-samples

# Arma 3 game PBOs (for additional format variants)
ln -sf "/path/to/Steam/steamapps/common/Arma 3" sources/arma3-game

# DayZ PAK files
ln -sf "/path/to/DayZ" sources/dayz-game

# Arma Reforger PAK files
ln -sf "/path/to/ArmaReforger" sources/reforger-game
```

### Windows (PowerShell — requires admin/developer mode for symlinks)

```powershell
# Arma 3 Samples
New-Item -ItemType SymbolicLink -Path "sources\arma3-samples" -Target "C:\Path\To\Steam\steamapps\common\Arma 3 Samples"

# Arma 3 game PBOs
New-Item -ItemType SymbolicLink -Path "sources\arma3-game" -Target "C:\Path\To\Steam\steamapps\common\Arma 3"

# DayZ PAK files
New-Item -ItemType SymbolicLink -Path "sources\dayz-game" -Target "C:\Path\To\DayZ"

# Arma Reforger PAK files
New-Item -ItemType SymbolicLink -Path "sources\reforger-game" -Target "C:\Path\To\ArmaReforger"
```

If symlinks are not available (e.g. on older Windows or restricted environments),
you can use `Copy-Item -Recurse` instead, or place your game directories directly
at the expected paths.

## License Notes

All data files retain their original Bohemia Interactive licenses:

- **APL** (Arma Public License): Free to use, modify, and distribute
- **APL-SA** (Arma Public License Share Alike): Same as APL, derivatives must
  also be shared under APL-SA
- **ADPL-SA** (Arma and DayZ Public License Share Alike): Same terms as APL-SA
- **DPL** (DayZ Public License): DayZ-specific terms

See https://www.bohemia.net/community/licenses for full license texts.
