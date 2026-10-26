namespace Pakban.Services;

/// <summary>
/// Lets ViewModels ask the shell (MainWindow) to show a modal confirmation without
/// referencing any WPF control directly. The window registers itself at startup.
/// </summary>
public static class UiDialogs
{
    public static Func<string, string, bool, Task<bool>>? Host { get; set; }

    public static Task<bool> ConfirmAsync(string title, string message, bool danger = false)
        => Host?.Invoke(title, message, danger) ?? Task.FromResult(true);
}
