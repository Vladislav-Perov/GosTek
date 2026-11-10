/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Универсальный компонент отображения векторных иконок (IconView) с поддержкой тем
 * СОДЕРЖИМОЕ: IconView (ContentView)
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Resources.Styles.Icons, GosTek.ThemeService
 * ==============================================================================
 */
using GosTek.Resources.Styles;
using Microsoft.Maui.Controls.Shapes;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace GosTek.Views.Controls;

/// <summary>
/// Переиспользуемый компонент векторной иконки.
/// Создаёт и масштабирует Path через Icons.CreatePath, реагирует на динамическую смену тем.
/// </summary>
public class IconView : ContentView
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(
            nameof(Icon),
            typeof(string),
            typeof(IconView),
            string.Empty,
            propertyChanged: (b, _, _) => ((IconView)b).UpdateIcon());

    public static readonly BindableProperty SizeProperty =
        BindableProperty.Create(
            nameof(Size),
            typeof(double),
            typeof(IconView),
            18.0,
            propertyChanged: (b, _, _) => ((IconView)b).UpdateIcon());

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(
            nameof(IconColor),
            typeof(string),
            typeof(IconView),
            "TextPrimary",
            propertyChanged: (b, _, _) => ((IconView)b).UpdateIcon());

    public static readonly BindableProperty StrokeThicknessProperty =
        BindableProperty.Create(
            nameof(StrokeThickness),
            typeof(double),
            typeof(IconView),
            2.0,
            propertyChanged: (b, _, _) => ((IconView)b).UpdateIcon());

    public static readonly BindableProperty FilledProperty =
        BindableProperty.Create(
            nameof(Filled),
            typeof(bool),
            typeof(IconView),
            false,
            propertyChanged: (b, _, _) => ((IconView)b).UpdateIcon());

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string IconColor
    {
        get => (string)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool Filled
    {
        get => (bool)GetValue(FilledProperty);
        set => SetValue(FilledProperty, value);
    }

    public IconView()
    {
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;
        InputTransparent = true;

        ThemeService.Changed += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        MainThread.BeginInvokeOnMainThread(UpdateIcon);
    }

    private void UpdateIcon()
    {
        if (string.IsNullOrWhiteSpace(Icon))
        {
            Content = null;
            return;
        }

        string strokeResource = string.IsNullOrWhiteSpace(IconColor) ? "TextPrimary" : IconColor;
        string? fillResource = Filled ? strokeResource : null;

        var path = Icons.CreatePath(Icon, Size, strokeResource, fillResource, StrokeThickness);
        path.HorizontalOptions = LayoutOptions.Center;
        path.VerticalOptions = LayoutOptions.Center;

        Content = path;
    }
}
