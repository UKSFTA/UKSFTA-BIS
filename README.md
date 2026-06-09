# UKSFTA-BIS

**ArmA File Format Library for UKSF Taskforce Alpha development pipeline.**

A centralized, cross-platform library providing high-performance reading, manipulation, and serialization capabilities for common Bohemia Interactive ArmA file formats. This library is optimized for the UKSFTA development pipeline, ensuring robust support for model forensics, auditing, and automated processing.

## 🚀 Key Features

*   **Comprehensive Format Support**:
    *   **P3D (ODOL/MLOD)**: Full parsing and conversion capabilities for models (v73-v75).
    *   **PBO**: Archive management, extraction, and validation.
    *   **PAA/PAC**: Texture conversion and manipulation.
    *   **RTM**: Skeletal animation playback and data extraction.
    *   **SQFC**: Compiled SQF script analysis.
    *   **WRP**: World configuration and terrain object parsing.
*   **Modernized Infrastructure**:
    *   **Target**: .NET 10.0 (C#) for high-performance and cross-platform reliability.
    *   **Cross-Platform**: Tested and verified build/test support for Linux and Windows.
*   **Developer Optimized**:
    *   Standardized build/development workflow.
    *   Integrated unit test suite for all file formats.

## 🛠 Setup & Development

This project utilizes a standardized build system to ensure consistency across the UKSFTA suite.

### Prerequisites
- .NET 10.0 SDK

### Build
To build the solution:
```bash
./build.sh
```

### Development & Testing
To run the test suite:
```bash
./dev.sh test
```

To watch for file changes and auto-run tests:
```bash
./dev.sh watch
```

## 🏗 Architectural Overview

The library is organized into modular namespaces, allowing projects to include only the necessary dependencies:

- `BIS.Core`: Shared utilities, compression (LZSS/LZO), and stream handling.
- `BIS.P3D`: ODOL/MLOD model structure and parsing.
- `BIS.PBO`: Archive structure and entry management.
- `BIS.PAA`: Texture pixel format conversion and encoding.
- `BIS.SQFC`: Compiled script analysis and AST representation.

## ⚖ License

This project is licensed under the **Arma Public License - Share Alike (APL-SA)**. See the `LICENSE` file for full details.
