# UKSFTA-BIS

**ArmA File Format Library for the UKSFTA development pipeline.**

[![Build](https://img.shields.io/github/actions/workflow/status/UKSFTA/UKSFTA-BIS/build.yml?label=build&logo=github)](https://github.com/UKSFTA/UKSFTA-BIS/actions/workflows/build.yml)
[![Tests](https://img.shields.io/github/actions/workflow/status/UKSFTA/UKSFTA-BIS/test.yml?label=tests&logo=github)](https://github.com/UKSFTA/UKSFTA-BIS/actions/workflows/test.yml)
[![Lint](https://img.shields.io/github/actions/workflow/status/UKSFTA/UKSFTA-BIS/lint.yml?label=lint&logo=github)](https://github.com/UKSFTA/UKSFTA-BIS/actions/workflows/lint.yml)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/platform-linux%20%7C%20windows-lightgrey)](.github/workflows)

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
| `BIS.SQF` | SQF tokenizer, Pratt parser, linter (20 rules), formatter |
| `BIS.Stringtable` | Stringtable XML parser and linter (6 rules) |
| `BIS.CLI` | `bis` CLI tool: lint, format, pack, convert, inspect |
| `BIS.IntegrationTest` | Cross-format integration tests against real game data |

## 🛠 CLI Tool (`bis`)

The `bis` CLI provides all library functionality from the command line:

```bash
# Lint config files (accepts files or directories, recursive)
bis lint config source/ --exit-code
bis lint config config.cpp --json
bis lint config source/ --fix

# Lint SQF scripts
bis lint sqf source/
bis lint sqf source/ --fix

# Lint stringtable XML
bis lint stringtable source/

# Lint PBO archives
bis lint pbo *.pbo

# Format SQF scripts
bis fmt sqf source/           # format in-place
bis fmt sqf source/ --check   # CI check (exit 1 if unformatted)

# Pack/unpack PBO archives
bis pbo list archive.pbo
bis pbo extract archive.pbo -o output/
bis pbo pack source/ -o output.pbo -p my_prefix -c

# Config serialization
bis config serialize config.cpp -o output.txt

# Inspect models
bis p3d info model.p3d
bis p3d validate model.p3d
bis p3d convert model.p3d -o converted.mlod

# Analyze textures
bis paa analyze texture.paa
bis paa suggest texture.paa
```

All `lint` commands support `--json` for structured output and `--exit-code` for CI pipelines.

## 🔍 Linting Overview

The library ships four format-specific linters that replicate HEMTT's lint rule set:

### Config Linter (L-Cxx) — 12 rules
| Rule | Severity | Description | Fix |
|---|---|---|---|
| L-C02 | Error | Duplicate property | — |
| L-C03 | Error | Duplicate class | — |
| L-C04 | Error | Missing external base class | ✓ |
| L-C05 | Warning | External parent case mismatch | ✓ |
| L-C07 | Error | Expected `[]` array suffix on array value | — |
| L-C09 | Error | Missing magazine in CfgMagazineWells | — |
| L-C11 | Warning | Unusual file extension on property | — |
| L-C12 | Help | Quoted math could be unquoted | ✓ |
| L-C13 | Help | Unnecessary `_this call` in callback | ✓ |
| L-C14 | Warning | Unused extern class declaration | ✓ |
| L-C15 | Warning | CfgPatches references missing class | — |
| L-C16 | Warning | File reference starts with path separator | — |

### SQF Linter (L-Sxx) — 20 rules
| Rule | Severity | Description | Fix |
|---|---|---|---|
| L-S01 | Warning | Tab character in source | ✓ |
| L-S02 | Warning | Inconsistent indentation (mixed tabs+spaces) | — |
| L-S04 | Help | Wrong command casing | ✓ |
| L-S05 | Error | Assignment in `if`/`while` condition (use `==`) | ✓ |
| L-S06 | Help | `find` result compared with `> -1` (use `>= 0`) | — |
| L-S11 | Help | `if (!x) then { ... } else` (swap branches) | — |
| L-S12 | Warning | Unused local variable | — |
| L-S13 | Warning | Unused parameter | — |
| L-S14 | Warning | Variable shadows outer scope declaration | — |
| L-S15 | Warning | Unused assignment (value overwritten before read) | — |
| L-S16 | Warning | Missing `private` on local variable | ✓ |
| L-S17 | Help | All-caps local variable (use lowercase) | ✓ |
| L-S18 | Warning | Vehicle check with `isNull` instead of `isNull objectParent` | — |
| L-S19 | Help | Extra `!` negation | ✓ |
| L-S20 | Warning | Boolean comparison with `== true`/`== false` | ✓ |
| L-S21 | Error | Impossible range comparison (`_x < 5 && _x > 10`) | — |
| L-S23 | Warning | Reassigning reserved variable | — |
| L-S24 | Warning | Magic number literal (except 0/1/-1/100/255) | — |
| L-S25 | Help | `count == 0` (use `isEqualTo []`) | ✓ |
| L-S27 | Help | `select count _x - 1` (use `select -1`) | ✓ |
| L-S36 | Error | Global variable declared with `private` | ✓ |

### Stringtable Linter (L-Lxx) — 6 rules
| Rule | Severity | Description |
|---|---|---|
| L-L01 | Warning | Keys not alphabetically sorted |
| L-L03 | Warning | Translation has leading/trailing whitespace |
| L-L04 | Warning | Unknown language code |
| L-L05 | Error | Key with no translations |
| L-L06 | Warning | Empty translation value |
| L-L07 | Warning | Missing `Original` language |

### PBO Linter (L-Pxx) — 5 rules
| Rule | Severity | Description |
|---|---|---|
| L-P01 | Error | Duplicate file entries |
| L-P02 | Warning | Obfuscated entry name (raw ≠ sanitized) |
| L-P03 | Warning | Missing or empty `prefix` property |
| L-P04 | Warning | Empty PBO (no files) |
| L-P05 | Warning | Zero timestamp on non-empty entry |

### Preprocessor (PWx) — 2 rules
| Rule | Severity | Description |
|---|---|---|
| PW1 | Warning | Unused `#define` macro |
| PW2 | Warning | Missing `#include` file |

## ✨ SQF Formatting

`bis fmt sqf` auto-formats SQF scripts with consistent style. Configurable options:

| Option | Default | Description |
|---|---|---|
| Indent size | 4 | Spaces per indent level |
| Use tabs | false | Use `\t` instead of spaces |
| Brace style | K&R | `KAndR` (same line) or `Allman` (new line) |

```bash
# Check style (CI mode)
bis fmt sqf source/ --check

# Format in place (default)
bis fmt sqf source/
```

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