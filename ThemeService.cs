using System;
using System.Collections.Generic;
using System.Text;

namespace GosTek;

public static class ThemeService
{
    private const string PrefKey = "app_theme_dark";

    public static bool IsDark { get; private set; } = true;

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
    }

    private static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        var palette = IsDark ? Dark : Light;
        foreach (var (key, value) in palette)
            app.Resources[key] = value;

        app.UserAppTheme = IsDark ? AppTheme.Dark : AppTheme.Light;
    }

    private static readonly Dictionary<string, Color> Dark = new() {
        ["PageBg"] = Color.FromArgb("#0F172A"),
        ["CardBg"] = Color.FromArgb("#1E293B"),
        ["CardHoverBg"] = Color.FromArgb("#273449"),
        ["CardStroke"] = Color.FromArgb("#334155"),
        ["TextPrimary"] = Color.FromArgb("#F1F5F9"),
        ["TextSecondary"] = Color.FromArgb("#94A3B8"),
        ["BrandGreen"] = Color.FromArgb("#34D399"),
        ["AccentGreen"] = Color.FromArgb("#34D399"),
        ["Divider"] = Color.FromArgb("#334155"),
        ["IconBoxBg"] = Color.FromArgb("#064E3B"),
        ["TabBarBg"] = Color.FromArgb("#111C30"),
        ["TabActiveBg"] = Color.FromArgb("#143A33"),
        ["ToggleTrackBg"] = Color.FromArgb("#475569"),
    };

    private static readonly Dictionary<string, Color> Light = new() {
        ["PageBg"] = Color.FromArgb("#F8FAFC"),
        ["CardBg"] = Color.FromArgb("#FFFFFF"),
        ["CardHoverBg"] = Color.FromArgb("#F1F5F9"),
        ["CardStroke"] = Color.FromArgb("#E2E8F0"),
        ["TextPrimary"] = Color.FromArgb("#0F172A"),
        ["TextSecondary"] = Color.FromArgb("#64748B"),
        ["BrandGreen"] = Color.FromArgb("#059669"),
        ["AccentGreen"] = Color.FromArgb("#10B981"),
        ["Divider"] = Color.FromArgb("#E2E8F0"),
        ["IconBoxBg"] = Color.FromArgb("#D1FAE5"),
        ["TabBarBg"] = Color.FromArgb("#FFFFFF"),
        ["TabActiveBg"] = Color.FromArgb("#D1FAE5"),
        ["ToggleTrackBg"] = Color.FromArgb("#CBD5E1"),
    };
}