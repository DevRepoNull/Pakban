# Pakban — پاک‌بان

**Smart Windows Storage Cleaner & RAM Optimizer / پاک‌ساز هوشمند حافظه و بهینه‌ساز رم ویندوز**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![Language](https://img.shields.io/badge/Language-C%23-green.svg)](https://learn.microsoft.com/dotnet/csharp/)
[![UI](https://img.shields.io/badge/UI-WPF-orange.svg)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![License](https://img.shields.io/badge/License-MIT-brightgreen.svg)](LICENSE)

Pakban ("keeper of cleanliness" / نگهبان پاکیزگی) is a modern, bilingual (English / فارسی) desktop application built with **C# / WPF on .NET 10** that graphically visualizes your disk and RAM consumption and enables safe, high-speed cleanup without risking your system stability.

---

## 📖 Documentation / مستندات رسمی پروژه

Full official documentation is available in both Persian and English:

- 🇮🇷 **[مستندات رسمی فارسی (Persian Documentation Hub)](docs/README.md#مستندات-رسمی-به-زبان-فارسی-persian-documentation)**
  - 📄 **[کتابچه جامع و یکپارچه مستندات پاکبان (DOCUMENTATION_FA.md)](docs/DOCUMENTATION_FA.md)**
  - 1️⃣ [درباره پروژه و اهداف](docs/fa/01_about_and_vision.md)
  - 2️⃣ [ویژگی‌ها و کاربردها](docs/fa/02_features_and_usecases.md)
  - 3️⃣ [معماری فنی و طراحی سیستم](docs/fa/03_technical_architecture.md)
  - 4️⃣ [مدل امنیت و استانداردهای OWASP](docs/fa/04_security_and_protection.md)
  - 5️⃣ [مرجع سرویس‌ها و کدهای پایه](docs/fa/05_services_and_api.md)
  - 6️⃣ [راهنمای بیلد و استقرار](docs/fa/06_build_and_deployment.md)

- 🇬🇧 **[Official English Documentation Hub](docs/README.md#official-english-documentation)**
  - 📄 **[Unified Official Documentation Manual (DOCUMENTATION_EN.md)](docs/DOCUMENTATION_EN.md)**
  - 1️⃣ [About & Goals](docs/en/01_about_and_vision.md)
  - 2️⃣ [Features & Use Cases](docs/en/02_features_and_usecases.md)
  - 3️⃣ [Technical Architecture](docs/en/03_technical_architecture.md)
  - 4️⃣ [Security Model & Protection](docs/en/04_security_and_protection.md)
  - 5️⃣ [Services & API Reference](docs/en/05_services_and_api.md)
  - 6️⃣ [Build & Deployment](docs/en/06_build_and_deployment.md)

---

## ✨ Features

### 📊 Overview (Dashboard)
- Real-time stat cards: cleanable space, junk item count, live RAM load, largest consumer category.
- **Animated vector donut chart** with interactive hover legend.
- Per-drive partition usage bars.

### 🧹 Storage Cleaner
- **13 Standard Categories** with real-time byte measurement and file counting:
  1. User Temp Files (`%TEMP%`)
  2. Windows Temp (`%WINDIR%\Temp` - Admin)
  3. Windows Update Cache (`SoftwareDistribution\Download` - Admin)
  4. Delivery Optimization Cache (Admin)
  5. Windows Recycle Bin (via Shell API)
  6. Thumbnail Cache (`thumbcache_*.db`)
  7. Browser Caches (Chrome, Edge, Firefox, Brave, Vivaldi, Opera across all profiles)
  8. GPU Shader Caches (DirectX, NVIDIA, AMD, Intel)
  9. Crash Dumps & Windows Error Reporting (Admin)
  10. Windows System Logs (Admin)
  11. Prefetch Cache (Admin)
  12. Developer Caches (`pip`, `npm`, `Yarn`, `NuGet`)
  13. Previous Windows Installations (`Windows.old` - Admin)
- **Leftover Data Finder:** Identifies abandoned folders in `AppData` / `ProgramData` belonging to already-uninstalled software by cross-referencing the Windows Registry and Start Menu shortcuts.
- **Safe Recovery:** All leftovers and `Windows.old` directories are moved to the **Recycle Bin**, never wiped permanently.

### ⚡ RAM & Performance
- Live memory gauge + 90-second historical trend line chart.
- Detailed metrics: Total physical, in-use, available, and Windows system cache memory.
- Top memory-consuming processes list with auto-refresh.
- **Gentle Working-Set Trim:** Invokes `EmptyWorkingSet` via `psapi.dll` across processes followed by managed GC, transparently reporting reclaimed memory.

### ⚙️ Settings & Customization
- **Runtime Bilingual Switching:** Dynamically toggle between English and Persian with automatic RTL layout mirroring and the bundled **Vazirmatn** font.
- Dark and Light visual themes.
- 6 vibrant accent color presets.
- Optional pre-cleaning confirmation dialog. All preferences saved to `%APPDATA%\Pakban\settings.json`.

---

## 🔒 Safety & Security Model

| Target Data | Deletion Mechanism | Reversibility |
|---|---|---|
| Temp & Cache Categories | Permanent per-file deletion (in-use/locked files skipped) | No |
| Leftover (Orphan) Data | Moved to **Recycle Bin** via `SHFileOperationW` | ✅ Fully Recoverable |
| `Windows.old` | Moved to **Recycle Bin** via `SHFileOperationW` | ✅ Fully Recoverable |
| Recycle Bin | Emptied via Shell API | No |

### Defensive Hardening:
- **OWASP Desktop Compliance:** Strictly prevents Path Traversal (CWE-22) using `ProtectedSystemRoots` blacklist.
- **Symlink / Junction Traversal Defense (CWE-59):** Inspects NTFS reparse point metadata and skips symbolic link descent to prevent external directory deletion traps.
- **Fault-Tolerant I/O:** Absorbs sharing violations (`ERROR_SHARING_VIOLATION - 32`) gracefully without crashing.

---

## 🚀 Build & Run

### Prerequisites
- Windows 10 (Build 1809+) or Windows 11
- .NET 10.0 SDK

### Visual Studio
1. Open `Pakban.sln`.
2. Select `Release` configuration and press `F5` (or `Ctrl+F5`).

### .NET CLI
```bash
# Build
dotnet build Pakban.sln -c Release

# Run
./bin/Release/net10.0-windows/Pakban.exe

# Publish as a self-contained single-file executable (no .NET required on target machine)
dotnet publish Pakban.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true

# Headless smoke test of the scan pipeline (outputs to selftest.log)
./bin/Release/net10.0-windows/Pakban.exe --selftest-scan
```

---

## 🏗️ Project Architecture

```
Pakban/
├── Assets/                 # App icon, logo, and embedded Vazirmatn font
├── Controls/               # Custom OnRender vector charts (DonutChart, LineChart, PercentBar)
├── Localization/           # Runtime dictionary (Loc.cs) & dynamic RTL FlowDirection
├── Models/                 # CleanCategory, OrphanItem, CleanupReport models
├── Services/               # StorageScannerService, CleanupService, FileOps, RamService, Serilog
├── ViewModels/             # MVVM ViewModels (RelayCommand, ViewModelBase)
├── Views/                  # MainWindow and feature pages (WPF XAML)
├── Themes/                 # Dark and Light ResourceDictionaries
├── docs/                   # Complete official documentation hub (FA & EN)
├── app.manifest            # Per-monitor v2 DPI & long path awareness
├── Pakban.csproj           # .NET 10 project file
└── Pakban.sln              # Solution file
```

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
