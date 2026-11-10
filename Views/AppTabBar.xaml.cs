/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Компонент навигационной панели (левый rail на Windows, нижний бар на Android)
 * СОДЕРЖИМОЕ: AppTabBar
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.Controls.IconView, GosTek.Services.HapticHelper
 * ==============================================================================
 */
using GosTek.Services;
using GosTek.Views.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

/// <summary>
/// Адаптивный таббар: на Windows отображается как левый вертикальный rail (боковая панель),
/// на Android — как нижняя навигационная панель.
/// Ключи вкладок: home, templates, scan, check, profile.
/// </summary>
public partial class AppTabBar : ContentView
{
    private const double HoverScale = 1.05;

    private sealed record Tab(string Key, string IconKey, string Title, string? Route);

    private static readonly Tab[] TabList =
    {
        new("home",      "home",      "Главная",  "//MainPage"),
        new("templates", "templates", "Шаблоны",  "//TemplatesPage"),
        new("scan",      "scan",      "Скан",     "//ScanPage"),
        new("check",     "check",     "Проверка", "//CheckPage"),
        new("profile",   "profile",   "Профиль",  "//ProfilePage"),
    };

    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(AppTabBar), string.Empty,
        propertyChanged: (bindable, _, _) => ((AppTabBar)bindable).ApplyStates());

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    /// <summary>
    /// Событие запроса открытия настроек из левого rail на Windows.
    /// </summary>
    public event EventHandler? SettingsRequested;

    private readonly List<(string Key, Border Border, IconView Icon, Label Label, bool IsRail)> _tabViews = new();
    private string? _hoveredKey;

    public AppTabBar()
    {
        InitializeComponent();

        BuildAndroidTabs();
        BuildWindowsRailTabs();
        ApplyStates();
    }

    private void BuildAndroidTabs()
    {
        for (int i = 0; i < TabList.Length; i++)
        {
            var tab = TabList[i];
            var (border, icon, label) = CreateAndroidTab(tab);
            Grid.SetColumn(border, i);

            TabsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            TabsGrid.Children.Add(border);
            _tabViews.Add((tab.Key, border, icon, label, false));
        }
    }

    private void BuildWindowsRailTabs()
    {
        for (int i = 0; i < TabList.Length; i++)
        {
            var tab = TabList[i];
            var (border, icon, label) = CreateWindowsRailTab(tab);
            WindowsTabsStack.Children.Add(border);
            _tabViews.Add((tab.Key, border, icon, label, true));
        }
    }

    public void ResetHover()
    {
        _hoveredKey = null;
        ApplyStates();
    }

    private (Border Border, IconView Icon, Label Label) CreateAndroidTab(Tab tab)
    {
        var icon = new IconView
        {
            Icon = tab.IconKey,
            Size = 18,
            IconColor = "TextSecondary",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var label = new Label
        {
            Text = tab.Title,
            FontSize = 10,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalOptions = LayoutOptions.Center,
        };

        var content = new VerticalStackLayout
        {
            Spacing = 3,
            Children = { icon, label }
        };

        var border = new Border
        {
            Margin = new Thickness(2, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(2, 6),
            Content = content,
        };

        AttachGestures(border, tab);
        return (border, icon, label);
    }

    private (Border Border, IconView Icon, Label Label) CreateWindowsRailTab(Tab tab)
    {
        var icon = new IconView
        {
            Icon = tab.IconKey,
            Size = 20,
            IconColor = "TextSecondary",
            VerticalOptions = LayoutOptions.Center,
        };

        var label = new Label
        {
            Text = tab.Title,
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children = { icon }
        };

        Grid.SetColumn(icon, 0);
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var border = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 10),
            Content = grid,
        };

        AttachGestures(border, tab);
        return (border, icon, label);
    }

    private void AttachGestures(Border border, Tab tab)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnTabTappedAsync(tab);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHover(tab.Key);
        pointer.PointerExited += (_, _) =>
        {
            if (_hoveredKey == tab.Key)
                SetHover(null);
        };

        border.GestureRecognizers.Add(tap);
        border.GestureRecognizers.Add(pointer);
    }

    private async Task OnTabTappedAsync(Tab tab)
    {
        HapticHelper.Click();
        if (tab.Key == ActiveTab)
            return;

        ResetHover();

        if (tab.Route is not null)
            await Shell.Current.GoToAsync(tab.Route);
        else
            await Stubs.ShowAsync(tab.Key);
    }

    private void SetHover(string? key)
    {
        _hoveredKey = key;
        ApplyStates();
    }

    private void ApplyStates()
    {
        foreach (var (key, border, icon, label, isRail) in _tabViews)
        {
            bool active = key == ActiveTab;
            bool hover = key == _hoveredKey;

            if (active)
            {
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, isRail ? "IconBoxBg" : "TabActiveBg");
                icon.IconColor = "BrandGreen";
                label.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
            }
            else if (hover)
            {
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardHoverBg");
                icon.IconColor = "TextPrimary";
                label.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            }
            else
            {
                border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                border.BackgroundColor = Colors.Transparent;
                icon.IconColor = "TextSecondary";
                label.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            }

            border.Scale = hover ? HoverScale : 1.0;
        }
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        HapticHelper.Click();
        if (SettingsRequested is not null)
        {
            SettingsRequested.Invoke(this, EventArgs.Empty);
            return;
        }

        await Shell.Current.GoToAsync("//MainPage?openSettings=true");
    }
}
