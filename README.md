# UKSFTA-BIS

**ArmA File Format Library for UKSF Taskforce Alpha development pipeline.**

A centralized, cross-platform library providing high-performance reading, manipulation, and serialization capabilities for common Bohemia Interactive ArmA file formats. This library is the foundational core for the UKSFTA mod suite, enabling robust model forensics, auditing, and automated processing.

## 🚀 Key Features

*   **Comprehensive Format Support**:
    *   **P3D (ODOL/MLOD)**: Full parsing and conversion capabilities for models (v73-v75).
    *   **PBO**: Archive management, extraction, and validation.
    *   **PAA/PAC**: Texture conversion and manipulation.
    *   **RTM**: Skeletal animation playback and data extraction.
    *   **SQFC**: Compiled SQF script analysis.
    *   **WRP/OPRW**: World configuration and terrain object parsing.
*   **Modernized Infrastructure**:
    *   **Target**: .NET 10.0 (C#) for high-performance and cross-platform reliability.
    *   **Cross-Platform**: Verified build/test support for Linux and Windows.
*   **Developer Optimized**:
    *   Standardized build/development workflow.
    *   Modular namespace architecture.
    *   Integrated unit test suite for all modules.

## 🛠 Setup & Development

This library utilizes a standardized build system to ensure consistency across the UKSFTA development ecosystem.

### Prerequisites
- .NET 10.0 SDK

### Build
To build the solution:
```bash
./build.sh
```

### Development & Testing
To run the full test suite for all modules:
```bash
./dev.sh test
```

## 🏗 Architectural Overview

The library is organized into modular namespaces, enabling targeted dependency management:

- `BIS.Core`: Shared utilities, compression (LZSS/LZO), and stream handling.
- `BIS.P3D`: ODOL/MLOD model structure, parsing, and conversion utilities.
- `BIS.PBO`: Archive structure and entry management.
- `BIS.PAA`: Texture pixel format conversion and encoding.
- `BIS.SQFC`: Compiled script analysis and AST representation.
- `BIS.RTM`: Animation data parsing.
- `BIS.WRP`: Terrain and world object configuration.

## ⚖ License

This project is licensed under the **MIT License**. See the `LICENSE` file for full details.
