# 2. Features, Capabilities, and Use Cases (Features & Use Cases)

Pakban consolidates a sophisticated suite of system optimization utilities into an intuitive, secure interface. This document details each feature module and practical real-world usage scenarios.

---

## 2.1. Navigation and Primary Modules

The application interface is structured around a streamlined, ergonomic navigation sidebar offering instant access to four dedicated pages:

```
┌────────────────────────────────────────────────────────┐
│                        PAKBAN                          │
├───────────────┬────────────────────────────────────────┤
│ 📊 Overview    │ • High-level storage and RAM health    │
│ 🧹 Cleaner     │ • 13 standard categories + Leftovers   │
│ ⚡ RAM & Perf  │ • Live memory graph & working-set trim │
│ ⚙️ Settings    │ • Themes, accent colors, bilingual UI  │
└───────────────┴────────────────────────────────────────┘
```

---

## 2.2. Overview Dashboard

The Dashboard provides an immediate analytical summary of disk and memory health upon opening:

- **Intelligent Metric Cards:**
  - **Cleanable Space:** Total aggregated volume of recoverable disk junk.
  - **Junk Items Count:** Exact count of non-essential files identified across the system.
  - **Live RAM Usage:** Real-time percentage and physical volume of memory currently allocated.
  - **Largest Consumer:** Surfaces the single heaviest category responsible for disk consumption.
- **Interactive Animated Donut Chart:**
  - Custom vector-rendered donut chart representing proportional storage consumption.
  - Interactive hover state: Hovering over any slice instantly highlights that category, showing its exact size and file count in the central legend.
- **Drive Utilization Monitors:**
  - Individual capacity bars for all active mounted hard disks and SSD partitions, displaying total, used, and free gigabytes.

---

## 2.3. Storage Cleaner Module

Pakban systematically categorizes system and application debris into **13 standard categories** alongside a specialized leftover finder:

### 13 Standard Cleanup Categories:

| # | Category Name | Identifier | Admin Required | Scope and Cleanup Algorithm |
| :---: | :--- | :--- | :---: | :--- |
| 1 | **User Temp Files** | `temp_user` | No | Cleans user `%TEMP%` folder; skips currently locked/in-use files. |
| 2 | **Windows Temp** | `temp_windows` | Yes | Wipes legacy OS scratch files inside `%WINDIR%\Temp`. |
| 3 | **Windows Update Cache** | `update_cache` | Yes | Clears cached update packages from `%WINDIR%\SoftwareDistribution\Download`. |
| 4 | **Delivery Optimization** | `delivery` | Yes | Purges P2P update cache files from Windows Delivery Optimization. |
| 5 | **Recycle Bin** | `recycle` | No | Empties the Windows Recycle Bin across all drives using `SHEmptyRecycleBinW`. |
| 6 | **Thumbnail Cache** | `thumbs` | No | Deletes corrupted or bloated `*cache_*.db` databases in Windows Explorer. |
| 7 | **Browser Caches** | `browsers` | No | Wipes caches for Google Chrome, Edge, Firefox, Brave, Vivaldi, and Opera across all user profiles. |
| 8 | **Shader Caches** | `shaders` | No | Clears compiled GPU shader caches for DirectX, NVIDIA, AMD, and Intel. |
| 9 | **Crash Dumps & WER** | `dumps` | Yes | Removes diagnostic crash dumps (`Minidump`) and Windows Error Reporting archives. |
| 10 | **Windows System Logs** | `logs` | Yes | Deletes accumulated historical log files in `%WINDIR%\Logs`. |
| 11 | **Prefetch Data** | `prefetch` | Yes | Clears stale application pre-caching files in `%WINDIR%\Prefetch`. |
| 12 | **Developer Caches** | `dev` | No | Cleans package manager caches for Python (`pip`), Node.js (`npm`, `Yarn`), and .NET (`NuGet`). |
| 13 | **Previous Windows** | `winold` | Yes | Detects legacy `Windows.old` directory and **safely moves it to the Recycle Bin**. |

---

## 2.4. Leftover (Orphaned) Application Data Finder

When software is uninstalled via Windows Settings or standard uninstallers, bulky configuration, cache, and telemetry directories are frequently left behind in:
- `%LOCALAPPDATA%`
- `%APPDATA%`
- `%PROGRAMDATA%`

Pakban executes a multi-point differential scan:
1. Enumerates all officially registered software from the Windows Registry (`HKLM` and `HKCU` uninstall keys).
2. Verifies installed shortcuts across the Start Menu.
3. Compares this whitelist against root directories in the user application data folders.
4. Identifies orphan directories belonging to defunct programs.

> [!IMPORTANT]
> **Safety Rule:** All orphaned folders are **moved to the Windows Recycle Bin** using shell APIs. They are never wiped permanently, guaranteeing that any misplaced user file can be recovered instantly.

---

## 2.5. RAM & Performance Monitoring

Pakban incorporates a real-time, lightweight performance suite:

- **Live Ring Gauge:** Visual circular indicator representing total system memory load percentage.
- **90-Second Rolling History Line Chart:** High-fidelity, smooth trendline visualizing RAM allocation changes over time.
- **Detailed Memory Breakdown Cards:** Real-time metrics for Total Physical Memory, In-Use RAM, Available Memory, and Windows System Cache.
- **Top Memory Consumers List:** Ranks the top memory-intensive processes currently active, with dynamic proportional bars and automatic polling.
- **Gentle Working-Set Trim (RAM Optimizer):** Calls the standard Windows API `EmptyWorkingSet` via `psapi.dll` across accessible processes to request the release of idle pages, followed by a managed garbage collection cycle within Pakban. Transparently reports the net memory reclaimed in megabytes.

---

## 2.6. Settings and Personalization

- **Runtime Bilingual Switching:** Dynamically toggle between English and Persian without needing an app restart. The layout automatically mirrors between LTR and RTL.
- **Dark and Light Themes:** Full high-contrast, polished dark and light visual themes designed for visual comfort.
- **6 Accent Color Palettes:** Choose from vibrant presets: Blue, Cyan, Purple, Emerald, Ruby, and Amber.
- **Confirmation Safety Guard:** Optional modal dialog confirmation before any cleanup procedure is executed.
- **Persistent Preferences:** User settings are automatically serialized to JSON in `%APPDATA%\Pakban\settings.json`.

---

## 2.7. Target Audience & Practical Use Cases

| User Persona | Key Benefits & Typical Use Case |
| :--- | :--- |
| **Everyday & Home Users** | Resolving "Disk Space Low" alerts on the `C:` drive without needing technical assistance or reinstalling Windows. |
| **Gamers & Streamers** | Clearing outdated GPU shader caches after driver upgrades and freeing up unused RAM prior to launching AAA games. |
| **Software Developers** | Wiping multi-gigabyte build artifacts and package manager caches (`pip`, `npm`, `NuGet`) accumulated over months. |
| **IT & Support Technicians** | Serving as a portable, standalone, zero-dependency diagnostic and optimization tool for maintenance on client machines. |
