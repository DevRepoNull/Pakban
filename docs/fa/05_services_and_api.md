# ۵. مرجع سرویس‌ها و کدهای هسته (Services & API Reference)

این فصل، مرجع فنی و مستندات برنامه‌نویسی سرویس‌ها و اینترفیس‌های کلیدی نرم‌افزار پاکبان است. توسعه‌دهندگان می‌توانند از این مستند برای نگهداری، آزمون و توسعه ماژول‌های جدید استفاده نمایند.

---

## ۵.۱. سرویس پویش دیسک (`IStorageScanner` / `StorageScannerService`)

اینترفیس `IStorageScanner` قرارداد استاندارد اسکن درایوها و شناسایی فایل‌های زباله را تعریف می‌کند.

```csharp
namespace Pakban.Services;

public interface IStorageScanner
{
    IReadOnlyList<DriveInfo> GetAvailableDrives();
    
    Task<StorageScanReport> ScanDrivesAsync(
        IEnumerable<DriveInfo> selectedDrives,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### متدهای کلیدی:
- **`GetAvailableDrives()`:**  
  فهرست درایوهای فیزیکی آماده به کار سیستم (`DriveType.Fixed` یا `DriveType.Removable`) را که از نظر بیت‌لاکر یا وضعیت اتصال بدون خطا هستند بازمی‌گرداند.
- **`ScanDrivesAsync(...)`:**  
  عملیات اسکن کامل را به صورت غیرهمگام (Non-blocking) بر روی درایوهای مشخص‌شده اجرا می‌کند. در طول اجرا، درصد پیشرفت و حجم کشف‌شده را گزارش کرده و به درخواست لغو (`CancellationToken`) بلافاصله واکنش نشان می‌دهد.

### مدل‌های داده گزارش اسکن:
- **`StorageScanProgress`:** حاوی فیلدهای `CurrentPath`, `CurrentCategory`, `ScannedBytes`, `ScannedFiles`, `ProgressPercentage`.
- **`StorageScanReport`:** حاوی لیست اقلام کشف‌شده (`Items`), پوشه‌های بازمانده (`LeftoverFolders`) و زمان سپری‌شده (`Duration`).

---

## ۵.۲. سرویس پاکسازی و حذف (`ICleanupService` / `CleanupService`)

سرویس `CleanupService` مسئولیت اجرای فرآیند حذف فیزیکی یا انتقال اقلام به سطل زباله را بر عهده دارد.

```csharp
namespace Pakban.Services;

public interface ICleanupService
{
    Task<CleanupReport> CleanPathsAsync(
        IEnumerable<string> paths,
        bool isCritical = false,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<CleanupReport> CleanTargetsAsync(
        IEnumerable<CleanupTarget> targets,
        IProgress<CleanupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
```

### خروجی گزارش پاکسازی (`CleanupReport`):
- `SuccessCount`: تعداد فایل‌ها یا پوشه‌هایی که با موفقیت پاک یا بازیافت شدند.
- `FailedCount`: تعداد اقلام رد شده (به دلیل قفل بودن فایل یا خطای دسترسی).
- `FreedBytes`: مجموع بایت‌های آزاد شده.
- `FreedSpaceMB` / `FreedSpaceGB`: معادل مگابایتی و گیگابایتی حجم آزاد شده.
- `Failures`: لیست جزئیات تمام خطاها شامل نوع Exception و وضعیت قفل بودن (`IsLocked`).

---

## ۵.۳. کتابخانه عملیات فایل و گارد امنیتی (`FileOps`)

کلاس استاتیک `FileOps` موتور کم‌سطح محاسباتی و امنیتی برنامه است:

- **`IsPathSafeForCleanup(string? path)`:**  
  بررسی تطابق مسیر با استانداردهای امنیتی (مسدودسازی کاراکترهای نامعتبر، رد ریشه دیسک‌ها، و مسدودسازی شاخه‌های محافظت‌شده ویندوز).
- **`IsReparsePoint(string path)`:**  
  استعلام متادیتای فایل از سیستم‌عامل برای کشف سریع Symbolic Link یا Directory Junction بدون ورود به دایرکتوری هدف.
- **`MoveToRecycleBin(string path)`:**  
  فراخوانی تابع Win32 `SHFileOperationW` با پرچم `FOF_ALLOWUNDO` جهت انتقال بدون کادر تایید و بازگشت‌پذیر فایل‌ها به سطل زباله.
- **`EmptyRecycleBin()`:**  
  تخلیه قطعی سطل زباله سیستم با فراخوانی `SHEmptyRecycleBinW`.
- **`MeasureFolder(string dir, CancellationToken ct)`:**  
  محاسبه دقیق و امن حجم پوشه و تعداد فایل‌های درون آن بدون دنبال کردن Symlinkها.
- **`MeasureFiles(string dir, string pattern, CancellationToken ct)`:**  
  محاسبه حجم فایل‌های منطبق با الگوی مشخص‌شده در پوشه هدف.
- **`BuildCategories()`:**  
  ایجاد لیست ۱۳ دسته‌بندی اصلی پاکسازی همراه با مسیرها، آیکون‌ها و وضعیت نیازمندی به دسترسی Administrator.

---

## ۵.۴. سرویس ردیابی داده‌های رها شده (`OrphanDetection`)

کلاس `OrphanDetection` وظیفه اسکن پیشرفته دایرکتوری‌های دیتای کاربر را بر عهده دارد:

```csharp
public static async Task<List<OrphanItem>> FindAsync(CancellationToken ct = default);
```

### الگوریتم اجرا:
1. **استخراج لیست برنامه‌های نصب‌شده:**  
   اسکن کلیدهای رجیستری ویندوز در مسیرهای:
   - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`
   به علاوه اسکن تمامی فایل‌های میانبر (`.lnk`) در منوی استارت ویندوز (`CommonStartMenu` و `StartMenu`).
2. **پایش پوشه‌های داده:**  
   بررسی پوشه‌های سطح اول در مسیرهای `%LOCALAPPDATA%`, `%APPDATA%` و `%PROGRAMDATA%`.
3. **تطبیق و فیلترگذاری:**  
   پوشه‌های متعلق به مایکروسافت، شرکت‌های شناخته‌شده اصلی، و برنامه‌های موجود در رجیستری نادیده گرفته می‌شوند. مابقی پوشه‌ها به عنوان کاندیدای داده‌های بازمانده محاسبه و گزارش می‌شوند.

---

## ۵.۵. سرویس حافظه رم (`RamService`)

کلاس `RamService` واسط کار با توابع کرنل و مدیریت مصرف حافظه است:

- **`GetStats()`:**  
  فراخوانی توابع `GlobalMemoryStatusEx` و `GetPerformanceInfo` و تولید رکورد `RamStats` شامل:
  - `TotalGB`: کل حافظه فیزیکی.
  - `UsedGB`: مقدار رم اشغال شده.
  - `AvailableGB`: مقدار رم آزاد.
  - `CachedGB`: حجم کش شده در System Cache.
  - `LoadPercent`: درصد فشار لحظه‌ای رم.
- **`Optimize()`:**  
  حلقه پویش تمامی پردازه‌های در دسترس، باز کردن Handle آن‌ها با دسترسی `PROCESS_SET_QUOTA` و اجرای تابع Win32 `EmptyWorkingSet`. سپس فراخوانی متد `GC.Collect()` و `GC.WaitForPendingFinalizers()` برای خود برنامه پاکبان. بازگرداندن رکورد `OptimizeResult` شامل مقدار حافظه آزادشده و تعداد پردازه‌های پاکسازی‌شده.

---

## ۵.۶. سرویس ثبت وقایع (`LoggingService`)

- مبتنی بر کتابخانه قدرتمند **Serilog**.
- مسیر ذخیره لاگ‌ها: `%APPDATA%\Pakban\logs\app-yyyyMMdd.log`.
- پیکربندی:
  - چرخش روزانه فایل‌ها (Daily Rolling).
  - نگهداری لاگ‌ها حداکثر تا ۳۰ روز گذشته (`retainedFileCountLimit: 30`).
  - ثبت جزئیات خطاهای Unhandled در سطوح AppDomain و Dispatcher.
  - محافظت از مسیر فایل لاگ در برابر حملات Symlink Arbitrary File Write (CWE-59).
