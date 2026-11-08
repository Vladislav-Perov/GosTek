/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Управление динамическим переключением светлой/тёмной/системной тем оформления
 * СОДЕРЖИМОЕ: ThemeService, AppThemeMode
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.SettingsService
 * ==============================================================================
 */
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
        ("PageBg",        "#0C1017", "#F6F4EE"),
        ("CardBg",        "#151A23", "#FFFFFF"),
        ("CardHoverBg",   "#1D2330", "#EFEBE0"),
        ("CardStroke",    "#2A3142", "#E2DED2"),
        ("TextPrimary",   "#E8ECF4", "#16213A"),
        ("TextSecondary", "#8A94AA", "#5A6479"),
        ("BrandGreen",    "#3FC3A1", "#0D6B57"),
        ("AccentGreen",   "#3FC3A1", "#0D6B57"),
        ("Divider",       "#2A3142", "#E2DED2"),
        ("IconBoxBg",     "#0B3D31", "#DCEFE9"),
        ("TabBarBg",      "#10141D", "#FFFFFF"),
        ("TabActiveBg",   "#12312A", "#DCEFE9"),
        ("ToggleTrackBg", "#3A4358", "#D6D1C3"),

        // Экран «Проверка»: важное замечание (красный), желательное (жёлтый) и их мягкие фоны (по макету)
        ("BadColor",      "#F2877C", "#B42318"),
        ("BadSoftBg",     "#3B1A17", "#FBE5E2"),
        ("WarnColor",     "#F0B35A", "#9A5B00"),
        ("WarnSoftBg",    "#3A2B10", "#FCEFD6"),
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

    /// <summary>Применить системную тему нативной платформы (WinUI ElementTheme на Windows).</summary>
    public static void ApplyPlatformTheme(Window? window = null)
    {
#if WINDOWS
        var w = window ?? Application.Current?.Windows.FirstOrDefault();
        if (w?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win && win.Content is Microsoft.UI.Xaml.FrameworkElement fe)
        {
            fe.RequestedTheme = IsDark 
                ? Microsoft.UI.Xaml.ElementTheme.Dark 
                : Microsoft.UI.Xaml.ElementTheme.Light;
        }
#endif
    }

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

        foreach (var window in app.Windows)
        {
            if (window.Page is { } page)
                page.BackgroundColor = Color.FromArgb(IsDark ? "#0C1017" : "#F6F4EE");

            ApplyPlatformTheme(window);
        }
    }
}
