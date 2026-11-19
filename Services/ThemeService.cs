/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Управление динамическим переключением светлой/тёмной/системной тем оформления и градиентов
 * СОДЕРЖИМОЕ: ThemeService, ThemeMode
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

    /// <summary>Тема оформления изменилась.</summary>
    public static event Action? Changed;

    // Ключ ресурса, цвет в тёмной теме, цвет в светлой
    private static readonly (string Key, string Dark, string Light)[] Palette = {
        ("PageBg",        "#1A1E22", "#F4F6F8"),
        ("CardBg",        "#23282D", "#FFFFFF"),
        ("CardHoverBg",   "#414950", "#EDF1F4"),
        ("CardStroke",    "#414950", "#D4DCE3"),
        ("TextPrimary",   "#EDF0F2", "#202B35"),
        ("TextSecondary", "#B7BEC5", "#51606F"),
        ("BrandGreen",    "#BACBD9", "#263B52"),
        ("AccentGreen",   "#BACBD9", "#263B52"),
        ("ActionBg",      "#324A62", "#263B52"),
        ("ActionHoverBg", "#3D5772", "#324A62"),
        ("Divider",       "#414950", "#D4DCE3"),
        ("IconBoxBg",     "#363F49", "#E7EDF3"),
        ("TabBarBg",      "#1A1E22", "#FFFFFF"),
        ("TabActiveBg",   "#363F49", "#E7EDF3"),
        ("ToggleTrackBg", "#414950", "#C4CFDA"),

        // Экран «Проверка»: важное замечание (красный), желательное (жёлтый) и их мягкие фоны
        ("BadColor",      "#FFABA4", "#B42318"),
        ("BadSoftBg",     "#4A2625", "#FBE5E2"),
        ("WarnColor",     "#F1C778", "#805003"),
        ("WarnSoftBg",    "#453720", "#FCEFD6"),
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
                NotifyChanged();
            };
    }

    public static void SetMode(ThemeMode mode)
    {
        if (mode == Mode) return;

        Mode = mode;
        Preferences.Default.Set(ModeKey, (int)mode);
        Apply();
        NotifyChanged();
    }

    private static void NotifyChanged()
    {
        var handler = Changed;
        if (handler == null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ThemeService.Changed] Handler error: {ex.Message}");
            }
        }
    }

    /// <summary>Применить системную тему нативной платформы (WinUI ElementTheme на Windows).</summary>
    public static void ApplyPlatformTheme(Window? window = null)
    {
#if WINDOWS
        var w = window ?? Application.Current?.Windows.FirstOrDefault();
        if (w?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
        {
            if (win.Content is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                fe.RequestedTheme = IsDark 
                    ? Microsoft.UI.Xaml.ElementTheme.Dark 
                    : Microsoft.UI.Xaml.ElementTheme.Light;
            }

            ApplyTitleBarTheme(win);
        }
#endif
    }

#if WINDOWS
    /// <summary>Кастомизация TitleBar окна Windows: расширение контента и прозрачные кнопки управления.</summary>
    public static void ApplyTitleBarTheme(Microsoft.UI.Xaml.Window win)
    {
        if (win.AppWindow is { } appWindow && Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.BackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.InactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            var fgColor = IsDark 
                ? Microsoft.UI.Colors.White 
                : Microsoft.UI.Colors.Black;

            titleBar.ButtonForegroundColor = fgColor;
            titleBar.ButtonHoverForegroundColor = fgColor;
            titleBar.ButtonPressedForegroundColor = fgColor;
            titleBar.ButtonInactiveForegroundColor = IsDark 
                ? global::Windows.UI.Color.FromArgb(120, 255, 255, 255)
                : global::Windows.UI.Color.FromArgb(120, 0, 0, 0);

            titleBar.ButtonHoverBackgroundColor = IsDark
                ? Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255)
                : Microsoft.UI.ColorHelper.FromArgb(20, 0, 0, 0);

            titleBar.ButtonPressedBackgroundColor = IsDark
                ? Microsoft.UI.ColorHelper.FromArgb(50, 255, 255, 255)
                : Microsoft.UI.ColorHelper.FromArgb(40, 0, 0, 0);
        }
        else
        {
            win.ExtendsContentIntoTitleBar = true;
        }
    }
#endif

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
                page.SetDynamicResource(VisualElement.BackgroundColorProperty, "PageBg");

            ApplyPlatformTheme(window);
        }
    }
}
