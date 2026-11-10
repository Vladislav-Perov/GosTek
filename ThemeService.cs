/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Управление динамическим переключением светлой/тёмной (Slate Dark)/системной тем оформления и градиентов
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

    /// <summary>Тема переключена. На это событие подписан ThemeToggle.</summary>
    public static event Action? Changed;

    // Ключ ресурса, цвет в тёмной теме (Slate Dark), цвет в светлой
    private static readonly (string Key, string Dark, string Light)[] Palette = {
        ("PageBg",        "#0F172A", "#F6F4EE"),
        ("CardBg",        "#1E293B", "#FFFFFF"),
        ("CardHoverBg",   "#334155", "#EFEBE0"),
        ("CardStroke",    "#334155", "#E2DED2"),
        ("TextPrimary",   "#F8FAFC", "#16213A"),
        ("TextSecondary", "#94A3B8", "#5A6479"),
        ("BrandGreen",    "#2DD4BF", "#0D6B57"),
        ("AccentGreen",   "#2DD4BF", "#0D6B57"),
        ("Divider",       "#334155", "#E2DED2"),
        ("IconBoxBg",     "#134E4A", "#DCEFE9"),
        ("TabBarBg",      "#0F172A", "#FFFFFF"),
        ("TabActiveBg",   "#134E4A", "#DCEFE9"),
        ("ToggleTrackBg", "#334155", "#D6D1C3"),

        // Экран «Проверка»: важное замечание (красный), желательное (жёлтый) и их мягкие фоны
        ("BadColor",      "#F87171", "#B42318"),
        ("BadSoftBg",     "#450A0A", "#FBE5E2"),
        ("WarnColor",     "#FBBF24", "#9A5B00"),
        ("WarnSoftBg",    "#451A03", "#FCEFD6"),
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

    /// <summary>Плавное переключение темы с эффектом мягкого затемнения и моргания.</summary>
    public static async Task ToggleWithAnimationAsync()
    {
        var targetMode = IsDark ? ThemeMode.Light : ThemeMode.Dark;
        await SetModeWithAnimationAsync(targetMode);
    }

    /// <summary>Установка режима темы с визуальным эффектом затухания/проявления.</summary>
    public static async Task SetModeWithAnimationAsync(ThemeMode mode)
    {
        if (mode == Mode)
            return;

        var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is ContentPage contentPage && contentPage.Content is Layout layout)
        {
            var veil = new BoxView
            {
                Color = Colors.Black,
                Opacity = 0,
                InputTransparent = true,
                ZIndex = 99999,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };

            try
            {
                if (layout is Grid grid)
                {
                    if (grid.RowDefinitions.Count > 0)
                        Grid.SetRowSpan(veil, Math.Max(1, grid.RowDefinitions.Count));
                    if (grid.ColumnDefinitions.Count > 0)
                        Grid.SetColumnSpan(veil, Math.Max(1, grid.ColumnDefinitions.Count));
                    grid.Children.Add(veil);
                }
                else
                {
                    layout.Children.Add(veil);
                }

                await veil.FadeToAsync(0.45, 120, Easing.CubicIn);
                SetMode(mode);
                await veil.FadeToAsync(0.0, 180, Easing.CubicOut);
            }
            catch
            {
                SetMode(mode);
            }
            finally
            {
                try
                {
                    if (layout.Children.Contains(veil))
                        layout.Children.Remove(veil);
                }
                catch { }
            }
        }
        else
        {
            SetMode(mode);
        }
    }

    /// <summary>Быстрый переключатель: из любого режима в противоположную тему.</summary>
    public static void Toggle()
    {
        if (Application.Current is not null && MainThread.IsMainThread)
        {
            _ = ToggleWithAnimationAsync();
        }
        else
        {
            SetMode(IsDark ? ThemeMode.Light : ThemeMode.Dark);
        }
    }

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

        // Градиенты дизайн-системы
        var brandGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(IsDark ? "#0F766E" : "#0D6B57"), 0.0f),
                new GradientStop(Color.FromArgb(IsDark ? "#2DD4BF" : "#14B8A6"), 1.0f),
            },
        };
        app.Resources["BrandGreenGradient"] = brandGradient;

        var mainRadial = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.4),
            Radius = 0.9,
            GradientStops = new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(IsDark ? "#1E293B" : "#FFFFFF"), 0.0f),
                new GradientStop(Color.FromArgb(IsDark ? "#0F172A" : "#F6F4EE"), 1.0f),
            },
        };
        app.Resources["MainPageBackgroundBrush"] = mainRadial;

        foreach (var window in app.Windows)
        {
            if (window.Page is { } page)
                page.BackgroundColor = Color.FromArgb(IsDark ? "#0F172A" : "#F6F4EE");

            ApplyPlatformTheme(window);
        }
    }
}
