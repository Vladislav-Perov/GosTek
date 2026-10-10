namespace GosTek;

/// <summary>Режим темы: как в системе, всегда тёмная, всегда светлая.</summary>
public enum ThemeMode { System, Dark, Light }

public static class ThemeService
{
    private const string PrefKey = "app_theme_dark";      // старый ключ (bool), нужен для переноса
    private const string ModeKey = "app_theme_mode";

    /// <summary>Цвет ошибки и подсветки пустых полей (одинаков в обеих темах).</summary>
    public static readonly Color ErrorColor = Color.FromArgb("#EF4444");

    /// <summary>Выбранный режим. Для «Системной» IsDark следует за темой устройства.</summary>
    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;

    /// <summary>Сейчас на экране тёмная тема (с учётом режима).</summary>
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
        if (Preferences.Default.ContainsKey(ModeKey))
            Mode = (ThemeMode)Preferences.Default.Get(ModeKey, (int)ThemeMode.Dark);
        else
            Mode = Preferences.Default.Get(PrefKey, true) ? ThemeMode.Dark : ThemeMode.Light;   // перенос со старой версии

        if (!Enum.IsDefined(Mode))
            Mode = ThemeMode.Dark;

        Apply();

        // В режиме «Системная» следим за переключением темы в самой системе
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, _) => {
                if (Mode != ThemeMode.System) return;
                Apply();
                Changed?.Invoke();
            };
    }

    public static void SetMode(ThemeMode mode)
    {
        if (mode == Mode) return;

        Mode = mode;
        Preferences.Default.Set(ModeKey, (int)mode);
        Apply();
        Changed?.Invoke();
    }

    /// <summary>Быстрый переключатель: из любого режима в противоположную тему.</summary>
    public static void Toggle() => SetMode(IsDark ? ThemeMode.Light : ThemeMode.Dark);

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        // Сначала сообщаем MAUI режим, затем читаем итоговую тему
        app.UserAppTheme = Mode switch {
            ThemeMode.Dark => AppTheme.Dark,
            ThemeMode.Light => AppTheme.Light,
            _ => AppTheme.Unspecified,
        };

        IsDark = Mode switch {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => app.RequestedTheme == AppTheme.Dark,
        };

        foreach (var (key, dark, light) in Palette)
            app.Resources[key] = Color.FromArgb(IsDark ? dark : light);
    }
}
