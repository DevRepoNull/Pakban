using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Pakban.Services;

namespace Pakban.Localization;

/// <summary>
/// Central two-language dictionary (English / Persian). Binds from XAML through
/// the indexer, and every binding refreshes automatically when the language changes.
/// </summary>
public class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();

    private string _lang = "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Translate a key in the current language (falls back to English, then to the key).</summary>
    public string this[string key]
    {
        get
        {
            if (Strings.TryGetValue(_lang, out var dict) && dict.TryGetValue(key, out var value))
                return value;
            if (Strings.TryGetValue("en", out var en) && en.TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }
    }

    public string Language => _lang;

    public bool IsRtl => _lang == "fa";

    public FontFamily AppFont => _lang == "fa"
        ? new FontFamily("pack://application:,,,/Assets/Fonts/#Vazirmatn, Segoe UI, Tahoma")
        : new FontFamily("Segoe UI, Tahoma");

    public string OtherLanguage => _lang == "fa" ? "EN" : "فا";

    public void SetLanguage(string code, bool save = true)
    {
        code = code == "fa" ? "fa" : "en";
        if (code == _lang) return;
        _lang = code;
        if (save) SettingsService.Current.Language = code;
        // "Item[]" refreshes every indexer binding at once.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRtl)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AppFont)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtherLanguage)));
        LanguageChanged?.Invoke();
    }

    /// <summary>Raised after the language changed, for collections that rebuild localized text.</summary>
    public event Action? LanguageChanged;

    /// <summary>Translate with {0}-style placeholders, culture-invariant so numbers stay Latin.</summary>
    public static string F(string key, params object?[] args)
        => string.Format(CultureInfo.InvariantCulture, I[key], args);

    public static string T(string key) => I[key];

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Keep the compiler happy about the unused helper (used by subclasses/tests).
    internal void Refresh(string name) => Raise(name);

    // ------------------------------------------------------------------ //
    //  Dictionary                                                         //
    // ------------------------------------------------------------------ //

    public static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["en"] = new Dictionary<string, string>
        {
            // Shell
            ["app_name"] = "Pakban",
            ["app_tagline"] = "Smart Windows Storage Cleaner",
            ["nav_dashboard"] = "Overview",
            ["nav_cleaner"] = "Storage Cleaner",
            ["nav_ram"] = "RAM & Performance",
            ["nav_settings"] = "Settings",
            ["nav_about"] = "About",
            ["btn_minimize"] = "Minimize",
            ["btn_maximize"] = "Maximize",
            ["btn_close"] = "Close",
            ["btn_language"] = "Switch language",
            ["btn_theme"] = "Toggle theme (Dark / Light)",

            // Generic
            ["ok"] = "OK",
            ["cancel"] = "Cancel",
            ["close"] = "Close",
            ["status_ready"] = "Ready",
            ["status_idle"] = "No scan yet",
            ["files"] = "files",
            ["items"] = "items",

            // Dashboard
            ["dash_title"] = "Overview",
            ["dash_sub"] = "A quick health report of your storage and memory",
            ["btn_scan"] = "Scan now",
            ["btn_rescan"] = "Rescan",
            ["dash_total_cleanable"] = "Cleanable space",
            ["dash_ram_usage"] = "RAM usage",
            ["dash_largest"] = "Largest category",
            ["dash_items_found"] = "Junk items found",
            ["dash_breakdown"] = "What is occupying your space",
            ["dash_drives"] = "Drives",
            ["dash_last_scan"] = "Last scan",
            ["dash_never"] = "Never — run your first scan",
            ["dash_no_data"] = "Run a scan and the breakdown will appear here",
            ["dash_go_clean"] = "Go to cleaner",
            ["dash_free"] = "free",
            ["dash_used"] = "used",
            ["dash_orphans"] = "Leftover data",
            ["dash_other"] = "Other",
            ["dash_ram_now"] = "right now",

            // Cleaner
            ["cleaner_title"] = "Storage Cleaner",
            ["cleaner_sub"] = "See exactly what is taking up space, then remove it safely",
            ["tab_standard"] = "Standard categories",
            ["tab_orphans"] = "Leftover data",
            ["btn_select_all"] = "Select all",
            ["btn_clear_sel"] = "Clear",
            ["btn_clean"] = "Clean selected",
            ["btn_cancel_scan"] = "Cancel",
            ["status_scanning"] = "Scanning {0}…",
            ["status_cleaning"] = "Cleaning {0}…",
            ["cleaner_admin_hint"] = "System areas need administrator rights — some categories are locked",
            ["btn_elevate"] = "Restart as Administrator",
            ["badge_admin"] = "System",
            ["clean_done_title"] = "Cleaning complete",
            ["clean_done_msg"] = "Freed {0} across {1} categories ({2} files removed).",
            ["clean_errors"] = "Some files were skipped because they were in use — that is normal.",
            ["confirm_clean_title"] = "Confirm cleanup",
            ["confirm_clean_msg"] = "{0} will be permanently deleted from {1} categories. Continue?",
            ["orphan_confirm_msg"] = "The selected leftover folders will be moved to the Recycle Bin, so you can restore them if something breaks. Continue?",
            ["orphan_desc"] = "Folders that most likely belong to apps you have already uninstalled",
            ["orphan_empty"] = "No leftover data found. Your system looks clean!",
            ["orphan_note"] = "These are smart guesses based on your installed apps. Review the list and only delete folders you recognize — they go to the Recycle Bin, so they are recoverable.",
            ["orphan_hint"] = "Will be moved to the Recycle Bin",
            ["col_name"] = "Name",
            ["col_path"] = "Location",
            ["col_size"] = "Size",
            ["scan_cancelled"] = "Scan cancelled",

            // Categories
            ["cat_temp_user"] = "User temp files",
            ["cat_temp_user_desc"] = "Leftovers from apps and installers in your personal temp folder",
            ["cat_temp_windows"] = "System temp files",
            ["cat_temp_windows_desc"] = "Windows' own temporary working folder",
            ["cat_update_cache"] = "Windows Update cache",
            ["cat_update_cache_desc"] = "Downloaded update packages that are already installed",
            ["cat_delivery"] = "Delivery Optimization cache",
            ["cat_delivery_desc"] = "Update chunks Windows shared over the local network",
            ["cat_recycle"] = "Recycle Bin",
            ["cat_recycle_desc"] = "Deleted files still waiting in the Recycle Bin",
            ["cat_thumbs"] = "Thumbnail & icon cache",
            ["cat_thumbs_desc"] = "Explorer's cached previews — rebuilt automatically",
            ["cat_browsers"] = "Browser caches",
            ["cat_browsers_desc"] = "Chrome, Edge, Firefox and friends (close browsers first for best results)",
            ["cat_shaders"] = "Shader caches",
            ["cat_shaders_desc"] = "DirectX / NVIDIA / AMD shader caches — rebuilt on demand",
            ["cat_dumps"] = "Crash dumps & error reports",
            ["cat_dumps_desc"] = "Minidumps, MEMORY.DMP and Windows Error Reporting data",
            ["cat_logs"] = "Windows logs",
            ["cat_logs_desc"] = "Diagnostic logs collected under C:\\Windows\\Logs",
            ["cat_prefetch"] = "Prefetch traces",
            ["cat_prefetch_desc"] = "Launch traces — cleaning may slow the next launches slightly",
            ["cat_dev"] = "Developer tool caches",
            ["cat_dev_desc"] = "pip, npm, Yarn and NuGet download caches",
            ["cat_winold"] = "Old Windows installation",
            ["cat_winold_desc"] = "The previous Windows kept after an upgrade (moved to Recycle Bin)",

            // RAM page
            ["ram_title"] = "RAM & Performance",
            ["ram_sub"] = "Live memory usage and a gentle way to reclaim it",
            ["ram_used"] = "Used",
            ["ram_available"] = "Available",
            ["ram_total"] = "Total",
            ["ram_cached"] = "Cached",
            ["ram_history"] = "Usage — last 90 seconds",
            ["ram_top"] = "Top memory consumers",
            ["btn_optimize"] = "Optimize RAM",
            ["ram_optimizing"] = "Optimizing…",
            ["ram_optimized_title"] = "Optimization done",
            ["ram_optimized_msg"] = "Freed about {0} of RAM.",
            ["ram_optimize_hint"] = "Asks running apps to release their unused memory (working-set trim). Windows caches will refill over time — that is healthy.",
            ["ram_refresh"] = "Refresh",

            // Settings
            ["set_title"] = "Settings",
            ["set_sub"] = "Tune Pakban to your taste — everything saves automatically",
            ["set_language"] = "Language",
            ["set_lang_fa"] = "فارسی",
            ["set_lang_en"] = "English",
            ["set_theme"] = "Theme",
            ["set_theme_system"] = "System",
            ["set_theme_system_desc"] = "Match Windows",
            ["set_theme_dark"] = "Dark",
            ["set_theme_dark_desc"] = "Always dark",
            ["set_theme_light"] = "Light",
            ["set_theme_light_desc"] = "Always light",
            ["set_accent"] = "Accent color",
            ["set_confirm"] = "Ask before cleaning",
            ["set_confirm_desc"] = "Show a confirmation dialog before anything is deleted",
            ["set_saved"] = "Settings are saved automatically",
            ["about_title"] = "About Pakban",
            ["about_body"] = "Pakban (پاک‌بان) means \"the keeper of cleanliness\". It maps out what is eating your disk — temp files, update leftovers, browser caches and folders orphaned by uninstalled apps — shows every piece graphically, and lets you clean it with confidence. Junk categories are deleted permanently, while risky items are always sent to the Recycle Bin instead.",
            ["about_version"] = "Version",
            ["about_tech"] = "Built with C# / WPF on .NET 10 — no external dependencies",
        },

        ["fa"] = new Dictionary<string, string>
        {
            // Shell
            ["app_name"] = "پاک‌بان",
            ["app_tagline"] = "پاک‌ساز هوشمند ویندوز",
            ["nav_dashboard"] = "نمای کلی",
            ["nav_cleaner"] = "پاک‌سازی حافظه",
            ["nav_ram"] = "رم و کارایی",
            ["nav_settings"] = "تنظیمات",
            ["nav_about"] = "درباره",
            ["btn_minimize"] = "کوچک‌کردن",
            ["btn_maximize"] = "بزرگ‌کردن",
            ["btn_close"] = "بستن",
            ["btn_language"] = "تغییر زبان",
            ["btn_theme"] = "تغییر تم (تاریک / روشن)",

            // Generic
            ["ok"] = "تأیید",
            ["cancel"] = "انصراف",
            ["close"] = "بستن",
            ["status_ready"] = "آماده",
            ["status_idle"] = "هنوز اسکن نشده",
            ["files"] = "فایل",
            ["items"] = "مورد",

            // Dashboard
            ["dash_title"] = "نمای کلی",
            ["dash_sub"] = "گزارش سریع سلامت حافظه و رم سیستم",
            ["btn_scan"] = "اسکن کن",
            ["btn_rescan"] = "اسکن مجدد",
            ["dash_total_cleanable"] = "فضای قابل پاک‌سازی",
            ["dash_ram_usage"] = "مصرف رم",
            ["dash_largest"] = "بزرگ‌ترین بخش",
            ["dash_items_found"] = "موارد ناخواسته",
            ["dash_breakdown"] = "چه چیزی فضای دیسک را اشغال کرده",
            ["dash_drives"] = "درایوها",
            ["dash_last_scan"] = "آخرین اسکن",
            ["dash_never"] = "هنوز — اولین اسکن را اجرا کن",
            ["dash_no_data"] = "اول اسکن کن تا تفکیک اینجا نمایش داده شود",
            ["dash_go_clean"] = "رفتن به پاک‌سازی",
            ["dash_free"] = "آزاد",
            ["dash_used"] = "مصرف‌شده",
            ["dash_orphans"] = "داده‌های بی‌صاحب",
            ["dash_other"] = "سایر",
            ["dash_ram_now"] = "همین حالا",

            // Cleaner
            ["cleaner_title"] = "پاک‌سازی حافظه",
            ["cleaner_sub"] = "دقیق ببین چه چیزی فضا را گرفته و بعد آن را ایمن پاک کن",
            ["tab_standard"] = "بخش‌های استاندارد",
            ["tab_orphans"] = "داده‌های بی‌صاحب",
            ["btn_select_all"] = "انتخاب همه",
            ["btn_clear_sel"] = "لغو انتخاب",
            ["btn_clean"] = "پاک‌سازی انتخاب‌شده‌ها",
            ["btn_cancel_scan"] = "انصراف",
            ["status_scanning"] = "در حال اسکن {0}…",
            ["status_cleaning"] = "در حال پاک‌سازی {0}…",
            ["cleaner_admin_hint"] = "بخش‌های سیستمی به دسترسی مدیر نیاز دارند — برخی بخش‌ها قفل هستند",
            ["btn_elevate"] = "اجرای مجدد به‌عنوان مدیر",
            ["badge_admin"] = "سیستمی",
            ["clean_done_title"] = "پاک‌سازی انجام شد",
            ["clean_done_msg"] = "{0} در {1} بخش آزاد شد ({2} فایل حذف شد).",
            ["clean_errors"] = "چند فایل به‌دلیل در حال استفاده بودن رد شدند — طبیعی است.",
            ["confirm_clean_title"] = "تأیید پاک‌سازی",
            ["confirm_clean_msg"] = "{0} از {1} بخش به‌صورت دائمی حذف می‌شود. ادامه می‌دهی؟",
            ["orphan_confirm_msg"] = "پوشه‌های بی‌صاحب انتخاب‌شده به سطل بازیافت منتقل می‌شوند تا اگر مشکلی پیش آمد قابل بازیابی باشند. ادامه می‌دهی؟",
            ["orphan_desc"] = "پوشه‌هایی که به احتمال زیاد متعلق به برنامه‌هایی هستند که حذف‌شان کرده‌ای",
            ["orphan_empty"] = "هیچ داده بی‌صاحبی پیدا نشد. سیستم تمیز به نظر می‌رسد!",
            ["orphan_note"] = "این فهرست با تحلیل هوشمند برنامه‌های نصب‌شده‌ات ساخته می‌شود. فقط پوشه‌هایی را پاک کن که می‌شناسی — همه به سطل بازیافت می‌روند و قابل بازیابی‌اند.",
            ["orphan_hint"] = "به سطل بازیافت منتقل می‌شود",
            ["col_name"] = "نام",
            ["col_path"] = "محل",
            ["col_size"] = "حجم",
            ["scan_cancelled"] = "اسکن لغو شد",

            // Categories
            ["cat_temp_user"] = "فایل‌های موقت کاربر",
            ["cat_temp_user_desc"] = "باقی‌ماندهٔ برنامه‌ها و نصب‌ها در پوشهٔ موقت شخصی تو",
            ["cat_temp_windows"] = "فایل‌های موقت سیستم",
            ["cat_temp_windows_desc"] = "پوشهٔ کاری موقت خود ویندوز",
            ["cat_update_cache"] = "حافظهٔ نهان Windows Update",
            ["cat_update_cache_desc"] = "بسته‌های آپدیت دانلودشده‌ای که نصب شده‌اند",
            ["cat_delivery"] = "حافظهٔ نهان Delivery Optimization",
            ["cat_delivery_desc"] = "قطعات آپدیت که ویندوز در شبکهٔ محلی به اشتراک گذاشته",
            ["cat_recycle"] = "سطل بازیافت",
            ["cat_recycle_desc"] = "فایل‌های حذف‌شده‌ای که هنوز در سطل بازیافت منتظرند",
            ["cat_thumbs"] = "حافظهٔ نهان تصاویر بندانگشتی",
            ["cat_thumbs_desc"] = "پیش‌نمایش‌های ذخیره‌شدهٔ اکسپلورر — خودکار بازسازی می‌شوند",
            ["cat_browsers"] = "حافظهٔ نهان مرورگرها",
            ["cat_browsers_desc"] = "کروم، اج، فایرفاکس و… (برای نتیجهٔ بهتر اول مرورگرها را ببند)",
            ["cat_shaders"] = "حافظهٔ نهان شیدرها",
            ["cat_shaders_desc"] = "حافظهٔ شیدرهای DirectX / NVIDIA / AMD — در صورت نیاز دوباره ساخته می‌شوند",
            ["cat_dumps"] = "فایل‌های خطا و کرش",
            ["cat_dumps_desc"] = "فایل‌های Minidump و MEMORY.DMP و گزارش خطای ویندوز",
            ["cat_logs"] = "لاگ‌های ویندوز",
            ["cat_logs_desc"] = "لاگ‌های تشخیصی جمع‌شده در \u200EC:\\Windows\\Logs\u200E",
            ["cat_prefetch"] = "ردپاهای Prefetch",
            ["cat_prefetch_desc"] = "ردپای اجرای برنامه‌ها — پاک‌سازی ممکن است اجرای بعدی را کمی کند کند",
            ["cat_dev"] = "حافظهٔ نهان ابزارهای توسعه",
            ["cat_dev_desc"] = "حافظهٔ دانلود pip، npm، Yarn و NuGet",
            ["cat_winold"] = "ویندوز قدیمی (\u200EWindows.old\u200E)",
            ["cat_winold_desc"] = "نسخهٔ قبلی ویندوز که بعد از ارتقا نگه داشته شده (به سطل بازیافت می‌رود)",

            // RAM page
            ["ram_title"] = "رم و کارایی",
            ["ram_sub"] = "مصرف زندهٔ رم و راهی ملایم برای پس‌گرفتن آن",
            ["ram_used"] = "مصرف‌شده",
            ["ram_available"] = "آزاد",
            ["ram_total"] = "کل",
            ["ram_cached"] = "ذخیره‌شده",
            ["ram_history"] = "مصرف — ۹۰ ثانیهٔ اخیر",
            ["ram_top"] = "بیشترین مصرف‌کننده‌های رم",
            ["btn_optimize"] = "بهینه‌سازی رم",
            ["ram_optimizing"] = "در حال بهینه‌سازی…",
            ["ram_optimized_title"] = "بهینه‌سازی انجام شد",
            ["ram_optimized_msg"] = "حدود {0} از رم آزاد شد.",
            ["ram_optimize_hint"] = "از برنامه‌های در حال اجرا می‌خواهد حافظهٔ بلااستفاده‌شان را آزاد کنند. حافظهٔ نهان ویندوز به‌مرور پر می‌شود — این طبیعی و سالم است.",
            ["ram_refresh"] = "به‌روزرسانی",

            // Settings
            ["set_title"] = "تنظیمات",
            ["set_sub"] = "پاک‌بان را به سلیقهٔ خودت تنظیم کن — همه‌چیز خودکار ذخیره می‌شود",
            ["set_language"] = "زبان",
            ["set_lang_fa"] = "فارسی",
            ["set_lang_en"] = "English",
            ["set_theme"] = "پوسته ظاهری",
            ["set_theme_system"] = "پیرو سیستم",
            ["set_theme_system_desc"] = "هماهنگ با ویندوز",
            ["set_theme_dark"] = "تاریک",
            ["set_theme_dark_desc"] = "همیشه تیره",
            ["set_theme_light"] = "روشن",
            ["set_theme_light_desc"] = "همیشه روشن",
            ["set_accent"] = "رنگ اصلی",
            ["set_confirm"] = "قبل از پاک‌سازی سؤال شود",
            ["set_confirm_desc"] = "قبل از هر حذفی یک پنجرهٔ تأیید نشان داده شود",
            ["set_saved"] = "تنظیمات خودکار ذخیره می‌شوند",
            ["about_title"] = "درباره پاک‌بان",
            ["about_body"] = "پاک‌بان یعنی نگهبان پاکیزگی. این برنامه نقشهٔ کامل آنچه فضای دیسکت را خورده را ترسیم می‌کند — فایل‌های موقت، بقایای آپدیت، حافظهٔ مرورگرها و پوشه‌های بی‌صاحبِ برنامه‌های حذف‌شده — همه را به‌صورت گرافیکی نشان می‌دهد و اجازه می‌دهد با خیال راحت پاک‌شان کنی. بخش‌های ناخواسته به‌صورت دائمی حذف می‌شوند، ولی موارد پرخطر همیشه به سطل بازیافت منتقل می‌شوند تا قابل بازیابی باشند.",
            ["about_version"] = "نسخه",
            ["about_tech"] = "ساخته‌شده با C#‎ / WPF روی NET 10. — بدون هیچ وابستگی خارجی",
        },
    };
}
