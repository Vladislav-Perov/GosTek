namespace GosTek;

public static class ThemeService
{
    private const string PrefKey = "app_theme_dark";

    /// <summary>Цвет ошибки и подсветки пустых полей (одинаков в обеих темах).</summary>
    public static readonly Color ErrorColor = Color.FromArgb("#EF4444");

    public static bool IsDark { get; private set; } = true;

    /// <summary>Тема переключена. На это событие подписан ThemeToggle.</summary>
    public static event Action? Changed;

    // Ключ ресурса, цвет в тёмной теме, цвет в светлой
    private static readonly (string Key, string Dark, string Light)[] Palette = {
        ("PageBg",        "#0F172A", "#F8FAFC"),
        ("CardBg",        "#1E293B", "#FFFFFF"),
        ("CardHoverBg",   "#273449", "#F1F5F9"),
        ("CardStroke",    "#334155", "#E2E8F0"),
        ("TextPrimary",   "#F1F5F9", "#0F172A"),
        ("TextSecondary", "#94A3B8", "#64748B"),
        ("BrandGreen",    "#34D399", "#059669"),
        ("AccentGreen",   "#34D399", "#10B981"),
        ("Divider",       "#334155", "#E2E8F0"),
        ("IconBoxBg",     "#064E3B", "#D1FAE5"),
        ("TabBarBg",      "#111C30", "#FFFFFF"),
        ("TabActiveBg",   "#143A33", "#D1FAE5"),
        ("ToggleTrackBg", "#475569", "#CBD5E1"),
    };

    // Вызвать один раз при старте приложения
    public static void Init()
    {
        IsDark = Preferences.Default.Get(PrefKey, true);
        Apply();
    }

    public static void Toggle()
    {
        IsDark = !IsDark;
        Preferences.Default.Set(PrefKey, IsDark);
        Apply();
        Changed?.Invoke();
    }

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        foreach (var (key, dark, light) in Palette)
            app.Resources[key] = Color.FromArgb(IsDark ? dark : light);

        app.UserAppTheme = IsDark ? AppTheme.Dark : AppTheme.Light;
    }
}
