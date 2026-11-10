/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Анимированный переключатель светлой и тёмной темы с векторными иконками Солнце/Луна
 * СОДЕРЖИМОЕ: ThemeToggle
 * ЗАВИСИМОСТИ: GosTek.ThemeService, GosTek.Views.Controls.IconView
 * ==============================================================================
 */
using GosTek.Views.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

/// <summary>
/// Переключатель тёмной/светлой темы с векторной сменой иконок. Сам следит за ThemeService,
/// поэтому страницам не нужно ничего синхронизировать в OnAppearing.
/// </summary>
public partial class ThemeToggle : ContentView
{
    public ThemeToggle()
    {
        InitializeComponent();

        Sync(animated: false);
        Loaded += (_, _) => Sync(animated: false);
        ThemeService.Changed += () => Sync(animated: true);
    }

    private void OnTapped(object? sender, TappedEventArgs e) => ThemeService.Toggle();

    private void Sync(bool animated)
    {
        bool dark = ThemeService.IsDark;
        ThemeIcon.Icon = dark ? "moon" : "sun";

        // Расчёт позиций:
        // Базовая позиция ползунка в AbsoluteLayout: X = 3, Y = 3 (размер 16x16 при треке 40x22).
        // В тёмной теме (выкл): TranslationX = 0, ползунок остаётся на X = 3.
        // В светлой теме (вкл): TranslationX = 18, ползунок перемещается на X = 3 + 18 = 21.
        // Правый край ползунка в светлой теме: 21 + 16 = 37, отступ справа: 40 - 37 = 3 px.
        double target = dark ? 0 : 18;

        if (animated && IsLoaded)
            _ = ToggleThumb.TranslateToAsync(target, 0, 150, Easing.CubicInOut);
        else
            ToggleThumb.TranslationX = target;
    }
}
