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

    private const string PrefRailCollapsed = "rail_collapsed";
    private static bool s_isRailCollapsed = Preferences.Get(PrefRailCollapsed, false);

    public AppTabBar()
    {
        InitializeComponent();

        BuildAndroidTabs();
        BuildWindowsRailTabs();
        ApplyRailCollapsedState(animate: false);
        ApplyStates();

        Loaded += OnAppTabBarLoaded;
        Unloaded += OnAppTabBarUnloaded;
    }

    private void OnAppTabBarLoaded(object? sender, EventArgs e)
    {
        if (Shell.Current is { } shell)
        {
            shell.Navigated -= OnShellNavigated;
            shell.Navigated += OnShellNavigated;
            SyncActiveTabFromRoute(shell.CurrentState?.Location?.OriginalString);

            // Гарантируем установку ширины Flyout при загрузке
            shell.FlyoutWidth = s_isRailCollapsed ? 64 : 220;
        }
    }

    private void OnAppTabBarUnloaded(object? sender, EventArgs e)
    {
        if (Shell.Current is { } shell)
            shell.Navigated -= OnShellNavigated;
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
    {
        SyncActiveTabFromRoute(e.Current?.Location?.OriginalString);
    }

    private void SyncActiveTabFromRoute(string? route)
    {
        if (string.IsNullOrEmpty(route))
            return;

        foreach (var tab in TabList)
        {
            if (tab.Route is not null && route.Contains(tab.Route.TrimStart('/'), StringComparison.OrdinalIgnoreCase))
            {
                if (ActiveTab != tab.Key)
                    ActiveTab = tab.Key;
                break;
            }
        }
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
            HorizontalOptions = LayoutOptions.Center,
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
            HorizontalOptions = LayoutOptions.Fill,
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
        SettingsRequested?.Invoke(this, EventArgs.Empty);
        await SettingsOverlay.ShowAsync();
    }

    private void OnRailToggleTapped(object? sender, EventArgs e)
    {
        HapticHelper.Click();
        s_isRailCollapsed = !s_isRailCollapsed;
        Preferences.Set(PrefRailCollapsed, s_isRailCollapsed);
        ApplyRailCollapsedState(animate: true);
    }

    private void OnCompactThemeClicked(object? sender, EventArgs e)
    {
        HapticHelper.Click();
        ThemeService.Toggle();
        CompactThemeIcon.Icon = ThemeService.IsDark ? "sun" : "moon";
    }

    private void ApplyRailCollapsedState(bool animate)
    {
        bool collapsed = s_isRailCollapsed;
        double targetWidth = collapsed ? 64 : 220;

        if (collapsed)
        {
            LogoLabel.IsVisible = false;
            LogoIcon.IsVisible = false;
            RailToggleIcon.Icon = "chevronright";
            SemanticProperties.SetDescription(RailToggleBtn, "Развернуть панель");

            Grid.SetColumn(RailToggleBtn, 0);
            Grid.SetColumnSpan(RailToggleBtn, 3);
            RailToggleBtn.HorizontalOptions = LayoutOptions.Center;

            WindowsHeaderGrid.Padding = new Thickness(0, 16, 0, 16);
            WindowsTabsStack.Padding = new Thickness(0, 6);
            WindowsFooterStack.Padding = new Thickness(0, 12, 0, 18);

            foreach (var (_, border, icon, label, isRail) in _tabViews)
            {
                if (!isRail) continue;
                label.IsVisible = false;
                border.Padding = new Thickness(0);
                border.WidthRequest = 44;
                border.HeightRequest = 44;
                border.HorizontalOptions = LayoutOptions.Center;
                icon.HorizontalOptions = LayoutOptions.Center;
                if (border.Content is Grid g)
                {
                    g.ColumnSpacing = 0;
                    g.HorizontalOptions = LayoutOptions.Center;
                    if (g.ColumnDefinitions.Count >= 2)
                    {
                        g.ColumnDefinitions[0].Width = new GridLength(44);
                        g.ColumnDefinitions[1].Width = new GridLength(0);
                    }
                }
            }

            WindowsSettingsLabel.IsVisible = false;
            WindowsSettingsGrid.ColumnSpacing = 0;
            WindowsSettingsGrid.HorizontalOptions = LayoutOptions.Center;
            if (WindowsSettingsGrid.ColumnDefinitions.Count >= 2)
            {
                WindowsSettingsGrid.ColumnDefinitions[0].Width = new GridLength(44);
                WindowsSettingsGrid.ColumnDefinitions[1].Width = new GridLength(0);
            }
            WindowsSettingsIcon.HorizontalOptions = LayoutOptions.Center;
            WindowsSettingsButton.WidthRequest = 44;
            WindowsSettingsButton.HeightRequest = 44;
            WindowsSettingsButton.Padding = new Thickness(0);
            WindowsSettingsButton.HorizontalOptions = LayoutOptions.Center;

            WindowsThemeRow.IsVisible = false;
            WindowsCompactThemeBtn.IsVisible = true;
            CompactThemeIcon.Icon = ThemeService.IsDark ? "sun" : "moon";
        }
        else
        {
            LogoLabel.IsVisible = true;
            LogoIcon.IsVisible = true;
            RailToggleIcon.Icon = "chevronleft";
            SemanticProperties.SetDescription(RailToggleBtn, "Свернуть панель");

            Grid.SetColumn(RailToggleBtn, 2);
            Grid.SetColumnSpan(RailToggleBtn, 1);
            RailToggleBtn.HorizontalOptions = LayoutOptions.End;

            WindowsHeaderGrid.Padding = new Thickness(14, 20, 14, 16);
            WindowsTabsStack.Padding = new Thickness(10, 6);
            WindowsFooterStack.Padding = new Thickness(10, 12, 10, 18);

            foreach (var (_, border, icon, label, isRail) in _tabViews)
            {
                if (!isRail) continue;
                label.IsVisible = true;
                border.Padding = new Thickness(12, 10);
                border.ClearValue(VisualElement.WidthRequestProperty);
                border.ClearValue(VisualElement.HeightRequestProperty);
                border.HorizontalOptions = LayoutOptions.Fill;
                icon.HorizontalOptions = LayoutOptions.Center;
                if (border.Content is Grid g)
                {
                    g.ColumnSpacing = 12;
                    g.HorizontalOptions = LayoutOptions.Fill;
                    if (g.ColumnDefinitions.Count >= 2)
                    {
                        g.ColumnDefinitions[0].Width = GridLength.Auto;
                        g.ColumnDefinitions[1].Width = GridLength.Star;
                    }
                }
            }

            WindowsSettingsLabel.IsVisible = true;
            WindowsSettingsGrid.ColumnSpacing = 10;
            WindowsSettingsGrid.HorizontalOptions = LayoutOptions.Fill;
            if (WindowsSettingsGrid.ColumnDefinitions.Count >= 2)
            {
                WindowsSettingsGrid.ColumnDefinitions[0].Width = GridLength.Auto;
                WindowsSettingsGrid.ColumnDefinitions[1].Width = GridLength.Star;
            }
            WindowsSettingsIcon.HorizontalOptions = LayoutOptions.Center;
            WindowsSettingsButton.ClearValue(VisualElement.WidthRequestProperty);
            WindowsSettingsButton.ClearValue(VisualElement.HeightRequestProperty);
            WindowsSettingsButton.Padding = new Thickness(12, 10);
            WindowsSettingsButton.HorizontalOptions = LayoutOptions.Fill;

            WindowsThemeRow.IsVisible = true;
            WindowsCompactThemeBtn.IsVisible = false;
        }

        WindowsRail.ClearValue(VisualElement.WidthRequestProperty);

        if (Shell.Current is { } shell)
        {
            if (animate)
            {
                double startWidth = shell.FlyoutWidth > 0 ? shell.FlyoutWidth : (collapsed ? 220 : 64);
                this.AbortAnimation("FlyoutWidthAnim");
                var anim = new Animation(w => shell.FlyoutWidth = w, startWidth, targetWidth, Easing.CubicInOut);
                anim.Commit(this, "FlyoutWidthAnim", 16, 200);
            }
            else
            {
                shell.FlyoutWidth = targetWidth;
            }
        }
    }
}
