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
    private sealed record Tab(string Key, string IconKey, string Title, string? Route);

    private static readonly Tab[] TabList =
    {
        new("home",      "home",      "Главная",  "//MainPage"),
        new("templates", "templates", "Шаблоны",  "//TemplatesPage"),
        new("scan",      "scan",      "Скан",     "//ScanPage"),
        new("check",     "check",     "Проверка", "//CheckPage"),
        new("profile",   "profile",   "Профиль",  "//ProfilePage"),
    };

    private static readonly Tab[] WindowsTabList =
    {
        new("home",      "home",      "Главная",  "//MainPage"),
        new("templates", "templates", "Шаблоны",  "//TemplatesPage"),
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
    private const int CompactRailWidth = 104;
    private const int ExpandedRailWidth = 220;
    private static bool s_isRailCollapsed = Preferences.Get(PrefRailCollapsed, true);

    internal static int WindowsRailWidth => s_isRailCollapsed ? CompactRailWidth : ExpandedRailWidth;

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
            shell.FlyoutWidth = WindowsRailWidth;
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
        for (int i = 0; i < WindowsTabList.Length; i++)
        {
            var tab = WindowsTabList[i];
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
            Size = 24,
            IconColor = "TextSecondary",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var label = new Label
        {
            Text = tab.Title,
            FontSize = 12,
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
            Size = 27,
            IconColor = "TextSecondary",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var label = new Label
        {
            Text = tab.Title,
            FontSize = 16,
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
        SettingsOverlay.Dismiss();

        await NavigationGuard.NavigateAsync(async () =>
        {
            SettingsOverlay.Dismiss();
            if (tab.Route is not null)
                await Shell.Current.GoToAsync(tab.Route);
            else
                await Stubs.ShowAsync(tab.Key);
        });
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
#if WINDOWS
        Platforms.Windows.WindowSizing.UpdateDragRegion();
#endif
    }

    private static void SetRailItemLayout(Border border, Label label, bool compact)
    {
        if (border.Content is not Grid grid)
            return;

        if (grid.RowDefinitions.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        grid.ColumnSpacing = compact ? 0 : 10;
        grid.RowSpacing = compact ? 4 : 0;
        grid.ColumnDefinitions[0].Width = compact ? GridLength.Star : GridLength.Auto;
        grid.ColumnDefinitions[1].Width = compact ? new GridLength(0) : GridLength.Star;
        Grid.SetRow(label, compact ? 1 : 0);
        Grid.SetColumn(label, compact ? 0 : 1);
        label.FontSize = compact ? 13 : 16;
        label.HorizontalTextAlignment = compact ? TextAlignment.Center : TextAlignment.Start;

        border.Padding = compact ? new Thickness(4, 8) : new Thickness(12, 10);
        border.WidthRequest = compact ? 90 : -1;
        border.HorizontalOptions = compact ? LayoutOptions.Center : LayoutOptions.Fill;
    }

    private void ApplyRailCollapsedState(bool animate)
    {
        bool collapsed = s_isRailCollapsed;
        double targetWidth = WindowsRailWidth;

        if (collapsed)
        {
            LogoLabel.IsVisible = false;
            LogoIcon.IsVisible = true;
            RailToggleIcon.Icon = "chevronright";
            SemanticProperties.SetDescription(RailToggleBtn, "Развернуть панель");

            Grid.SetColumn(RailToggleBtn, 2);
            RailToggleBtn.HorizontalOptions = LayoutOptions.End;

            WindowsHeaderGrid.Padding = new Thickness(10, 16);
            WindowsTabsStack.Padding = new Thickness(0, 6);
            WindowsFooterStack.Padding = new Thickness(0, 12, 0, 18);

            foreach (var (_, border, icon, label, isRail) in _tabViews)
            {
                if (!isRail) continue;
                SetRailItemLayout(border, label, compact: true);
            }

            SetRailItemLayout(WindowsSettingsButton, WindowsSettingsLabel, compact: true);
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
                SetRailItemLayout(border, label, compact: false);
            }

            SetRailItemLayout(WindowsSettingsButton, WindowsSettingsLabel, compact: false);
        }

        WindowsRail.ClearValue(VisualElement.WidthRequestProperty);

        if (Shell.Current is { } shell)
        {
            if (animate)
            {
                double startWidth = shell.FlyoutWidth > 0 ? shell.FlyoutWidth : (collapsed ? ExpandedRailWidth : CompactRailWidth);
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
