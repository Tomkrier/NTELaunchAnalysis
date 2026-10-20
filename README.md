# LaunchAnalysis

Launcher analysis projects for **Neverness To Everness (NTE)**.

This directory contains two independent launcher implementations under analysis:

- `NTELauncher_Rust/` — launcher written in Rust
- `NTELauncher_C#/` — launcher written in C#

## NTELauncher_Rust

Rust-based game launcher for NTE.

### Contents

- `Cargo.toml` — Rust project manifest
- `src/` — Rust source code
- `target/` — build output
- `build.bat` — build script
- `NTE_Rust_Standalone.bat` — standalone launcher script

### Build

```bat
cd NTELauncher_Rust
build.bat
```

Requires [Rust](https://www.rust-lang.org/) and Cargo to be installed.

## NTELauncher_C#

C#-based game launcher for NTE.

### Contents

- `NTEGameLauncher.cs` — C# source code
- `NTEGameLauncher.exe` — compiled launcher executable
- `build.bat` — build script
- `NTE_Standalone.bat` — standalone launcher script

### Build

```bat
cd NTELauncher_C#
build.bat
```

Requires the .NET SDK or a compatible C# toolchain.


## Notes

- This folder is for launcher analysis only. Game client files are located in the `Client/` directory at the repository root.
- Scripts and executables are provided as-is for analysis purposes.
