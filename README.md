# UKSFTA-BIS

**ArmA File Format Library for the UKSFTA development pipeline.**

A centralized, cross-platform library providing high-performance reading, manipulation, and serialization for common Bohemia Interactive file formats across multiple Arma generations (Cold War Crisis through Reforger). This library is the foundational core for the UKSFTA mod suite, enabling robust model forensics, auditing, and automated processing.

## 🚀 Key Features

*   **Comprehensive Format Support**:
    *   **P3D (ODOL/MLOD)**: Full parsing and conversion for models (v73–v75).
    *   **PBO**: Archive management, extraction, validation, and obfuscation reversal.
    *   **PAA/PAC**: Texture conversion and manipulation (+ standalone encoder).
    *   **EBO**: Enfusion Binary Object (Reforger) format.
    *   **PAK**: Enfusion PAK archive format (Reforger / DayZ).
    *   **RTM**: Skeletal animation data extraction.
    *   **SQFC**: Compiled SQF script analysis.
    *   **WRP/OPRW**: World configuration and terrain object parsing.
    *   **ALB**: Bytecode analysis for Arma scripting.
    *   **BISign**: Public-key signature and bikey handling.
*   **Modernized Infrastructure**:
    *   **Target**: .NET 10.0 (C#) for high-performance and cross-platform reliability.
    *   **Cross-Platform**: Verified build/test support for Linux and Windows.
*   **Developer Optimized**:
    *   Modular namespace architecture for targeted dependency management.
    *   Integrated unit + integration test suite with graceful data-skipping.
    *   CI with separate build, test, and lint workflows.

## 🗂 Format Coverage by Game

| Format | Cold War Crisis | Arma 1 | Arma 2 | Arma 3 | Reforger |
|---|---|---|---|---|---|
| PBO | ✓ | ✓ | ✓ | ✓ | |
| P3D (ODOL) | | ✓ | ✓ | ✓ | |
| PAA | | ✓ | ✓ | ✓ | |
| RTM | | ✓ | ✓ | ✓ | |
| WRP | | ✓ | ✓ | ✓ | |
| SQFC | | | | ✓ | |
| EBO | | | | | ✓ |
| PAK | | | | | ✓ |
| ALB | | | | ✓ | |
| BISign | | | | ✓ | ✓ |

## 🔍 PBO Deobfuscation

The `BIS.PBO.Deobfuscator` library provides specialized profiles for recovering
PBO files that use non-standard or obfuscated structures where standard tools
may fail:

1. **ModularSuffixRecoveryProfile** — handles Cyrillic-based renaming and decoy injection via multiple detection modules.
2. **HeuristicFallbackProfile** — catch-all that detects structural anomalies (e.g. high small-file ratios, unusual naming density).
3. **Legacy Profiles** (SuffixRecoveryProfile, DecoyInjectionProfile) — targeted profiles for specific obfuscation variants.

> The [UKSFTA-P3D](https://github.com/UKSFTA/UKSFTA-P3D) project provides a
> P3D debinariser (ODOL → MLOD converter) using this library.

## 🛠 Setup & Development

### Prerequisites
- .NET 10.0 SDK

### Clone
```bash
git clone --recurse-submodules git@github.com:UKSFTA/UKSFTA-BIS.git
# or if already cloned:
git submodule update --init docs
```

### Build
```bash
# Linux / macOS
./build.sh

# Windows (or any platform)
dotnet build
```

### Test
```bash
# Full test suite (all modules)
./dev.sh test

# Or directly
dotnet test
```

### Test Data (Integration Tests)
Some tests exercise real Arma format files and will skip gracefully if test
data is not present. To download a minimal set of ALDP test packages:

```bash
# Linux / macOS
./_testdata/download.sh

# Windows (PowerShell)
.\_testdata\download.ps1
```

See [`_testdata/README.md`](_testdata/README.md) for details on symlinking your
local game installation for broader format coverage.

### Lint
The CI pipeline runs these checks. You can run them locally as well:

```bash
# C# code style
dotnet format --verify-no-changes

# Shell scripts (requires shellcheck)
shellcheck _testdata/download.sh build.sh dev.sh

# PowerShell scripts (requires PSScriptAnalyzer)
Invoke-ScriptAnalyzer -Path _testdata/download.ps1
```

## 📚 Module Documentation

Detailed documentation for each module, including format background, key types,
usage examples, and test data expectations:

| Module | Format | Doc |
|---|---|---|
| BIS.Core | Foundation utilities | [docs/Core.md](docs/Core.md) |
| BIS.P3D | ODOL/MLOD models | [docs/P3D.md](docs/P3D.md) |
| BIS.PBO | Archives | [docs/PBO.md](docs/PBO.md) |
| BIS.PBO.Deobfuscator | Obfuscation reversal | [docs/PBO-Deobfuscator.md](docs/PBO-Deobfuscator.md) |
| BIS.PAA | PAA/PAC textures | [docs/PAA.md](docs/PAA.md) |
| BIS.PAA.Encoder | Texture encoding | [docs/PAA-Encoder.md](docs/PAA-Encoder.md) |
| BIS.EBO | Encrypted PBO | [docs/EBO.md](docs/EBO.md) |
| BIS.PAK | Enfusion archives | [docs/PAK.md](docs/PAK.md) |
| BIS.RTM | Skeletal animations | [docs/RTM.md](docs/RTM.md) |
| BIS.SQFC | Compiled SQF | [docs/SQFC.md](docs/SQFC.md) |
| BIS.WRP | World files | [docs/WRP.md](docs/WRP.md) |
| BIS.ALB | Lip-sync bytecode | [docs/ALB.md](docs/ALB.md) |
| BIS.Sign | Signatures/keys | [docs/BISign.md](docs/BISign.md) |
| BIS.IntegrationTest | Integration tests | [docs/Integration-Test.md](docs/Integration-Test.md) |

Architecture overview and format coverage table: [docs/Home.md](docs/Home.md)

> `docs/` is a Git submodule pointing to the [repository wiki](https://github.com/UKSFTA/UKSFTA-BIS/wiki).
> To update it locally: `git submodule update --init docs`

## 🏗 Architectural Overview

The library is organized into 14 library projects plus test and utility projects:

| Project | Description |
|---|---|
| `BIS.Core` | Shared utilities, compression (LZSS/LZO), stream handling |
| `BIS.P3D` | ODOL/MLOD model parsing, conversion (v73–v75) |
| `BIS.PBO` | PBO archive structure, entry management, extraction |
| `BIS.PBO.Deobfuscator` | Obfuscation reversal profiles for non-standard PBOs |
| `BIS.PAA` | PAA/PAC texture pixel format decoding |
| `BIS.PAA.Encoder` | Standalone PAA texture encoder |
| `BIS.EBO` | Enfusion Binary Object (Reforger) format |
| `BIS.PAK` | Enfusion PAK archive format (Reforger / DayZ) |
| `BIS.RTM` | Skeletal animation data parsing |
| `BIS.SQFC` | Compiled SQF bytecode analysis |
| `BIS.WRP` | World / terrain object configuration parsing |
| `BIS.ALB` | Arla bytecode analysis |
| `BIS.Sign` | BIS public-key signature reading and generation |
| `BIS.IntegrationTest` | Cross-format integration tests against real game data |

## 🤖 CI / CD

Three focused GitHub Actions workflows replace a former monolithic pipeline:

* **build.yml** — Restore + Build on ubuntu and windows (push/PR to main, tags, manual).
* **test.yml** — Test on both platforms with XPlat Code Coverage upload to Codecov.
* **lint.yml** — Parallel linting: ShellCheck on bash scripts, PSScriptAnalyzer on PowerShell, and `dotnet format --verify-no-changes`.

## ⚖ License

This project is licensed under the **MIT License**. See the `LICENSE` file for full details.

## 🙏 Acknowledgements

This library builds upon work from two upstream projects:

- **[jetelain/bis-file-formats](https://github.com/jetelain/bis-file-formats)** — The fork this repository was originally derived from, containing the most up-to-date format parsers.
- **[Braini01/bis-file-formats](https://github.com/Braini01/bis-file-formats)** — The original upstream project that laid the foundation for many of the format implementations.

The project code has been heavily modified from its upstream origins; all
`<Authors>` metadata in `.csproj` files now reflects **UKSFTA** as the
current maintainer.