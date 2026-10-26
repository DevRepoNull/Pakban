# 6. Build, Testing, Deployment, and Diagnostics (Build & Deployment)

This manual outlines the procedures for building Pakban from source, executing headless smoke tests, compiling self-contained single-file binaries, and troubleshooting runtime behavior.

---

## 6.1. Development Prerequisites

To compile and execute Pakban from source:

- **Operating System:** Windows 10 (Version 1809 or higher) or Windows 11 (x64 or ARM64).
- **Development Kit:** **.NET 10.0 SDK** or later.
- **IDE / Editor:**
  - **Visual Studio 2022** (version 17.12 or newer with the *.NET Desktop Development* workload)
  - Or **Visual Studio Code** with the *C# Dev Kit* extension
  - Or the standard **.NET CLI**.

---

## 6.2. Building the Solution

### 1. Building via Visual Studio:
1. Open `Pakban.sln`.
2. Set the build configuration to **Release** and platform to **x64** (or Any CPU).
3. Press `Ctrl + Shift + B` to build the solution.
4. Press `F5` to execute under the debugger or `Ctrl + F5` to run directly.

### 2. Building via .NET CLI:

```powershell
# Navigate to the solution directory
cd d:\DotNet\WindowsCleaner

# Restore dependencies and build in Release mode
dotnet build Pakban.sln -c Release

# Launch the compiled binary
.\bin\Release\net10.0-windows\Pakban.exe
```

---

## 6.3. Headless Pipeline Smoke Test

Pakban includes a dedicated headless execution switch designed for automated smoke testing and CI/CD validation without launching the WPF graphical user interface:

```powershell
.\bin\Release\net10.0-windows\Pakban.exe --selftest-scan
```

### Execution Behavior:
- Launches the application in non-interactive mode.
- Executes full storage measurement routines across all 13 categories.
- Benchmarks execution time and outputs results to `selftest.log` adjacent to the executable.
- Exits cleanly upon completion.

---

## 6.4. Self-Contained Single-File Publishing

For end-user distribution, Pakban can be published as a self-contained single-file executable that runs on any modern Windows installation **without requiring the .NET runtime to be installed**:

```powershell
dotnet publish Pakban.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true
```

### Characteristics of the Output:
- The compiled output is written to: `bin\Release\net10.0-windows\win-x64\publish\Pakban.exe`.
- Bundles the .NET 10 runtime, WPF dependencies, embedded Vazirmatn fonts, and Win32 interop assemblies into a single standalone binary.
- Requires no installation or administrative setup—users simply double-click to run.

---

## 6.5. Diagnostics and Troubleshooting

If unexpected behavior, locked file conflicts, or exceptions occur, diagnostic logs are generated automatically at:

```
%APPDATA%\Pakban\logs\app-yyyyMMdd.log
```

### Sample Log Output:
```text
[2026-10-27 00:09:00.123 +03:30] [INF] Pakban logging initialized. Target log directory: C:\Users\User\AppData\Roaming\Pakban\logs
[2026-10-27 00:09:02.450 +03:30] [INF] Storage scan initiated on drives: C:\
[2026-10-27 00:09:05.890 +03:30] [WRN] Skipped locked file during scan: C:\Users\User\AppData\Local\Temp\etilqs_abc (Sharing violation)
[2026-10-27 00:09:10.012 +03:30] [INF] Scan completed. Total junk found: 4.82 GB (12,410 files)
```

User preferences are saved in `%APPDATA%\Pakban\settings.json`. Deleting this JSON file restores all application settings to their default factory state.
