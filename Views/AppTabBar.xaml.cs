using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

/// <summary>
/// Общий нижний таббар. Страница указывает активную вкладку: ActiveTab="home".
/// Ключи вкладок: home, templates, scan, check, profile.
/// </summary>
public partial class AppTabBar : ContentView
{
    private const double HoverScale = 1.08;

    // Route = null: перехода пока нет, вкладка показывает заглушку
    private sealed record Tab(string Key, string Icon, string Title, string? Route);

    private static readonly Tab[] TabList =
    {
        new("home",      "🏠",  "Главная",  "//MainPage"),
        new("templates", "📋",  "Шаблоны",  "//TemplatesPage"),
        new("scan",      "📷",  "Скан",     null),
        new("check",     "🛡️", "Проверка", null),
        new("profile",   "👤",  "Профиль",  "//ProfilePage"),
    };

    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(AppTabBar), string.Empty,
        propertyChanged: (bindable, _, _) => ((AppTabBar)bindable).ApplyStates());

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    private readonly List<(string Key, Border Border)> _tabs = new();
    private string? _hoveredKey;

    public AppTabBar()
    {
        InitializeComponent();

        for (int i = 0; i < TabList.Length; i++) {
            var border = CreateTab(TabList[i]);
            Grid.SetColumn(border, i);

            TabsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            TabsGrid.Children.Add(border);
            _tabs.Add((TabList[i].Key, border));
        }

        ApplyStates();
    }

    /// <summary>
    /// Сбрасывает наведение. Вызывается со страницы в OnAppearing:
    /// при смене страницы PointerExited не приходит, и вкладка оставалась «залипшей».
    /// </summary>
    public void ResetHover()
    {
        _hoveredKey = null;
        ApplyStates();
    }

    // Вкладка: иконка и подпись в скруглённой рамке + жесты. Цвета выставляет ApplyStates
    private Border CreateTab(Tab tab)
    {
        var content = new VerticalStackLayout { Spacing = 2 };
        content.Children.Add(new Label { Text = tab.Icon, FontSize = 18, HorizontalOptions = LayoutOptions.Center });
        content.Children.Add(new Label {
            Text = tab.Title,
            FontSize = 10,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalOptions = LayoutOptions.Center,
        });

        var border = new Border {
            Margin = new Thickness(2, 0),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(2, 6),
            Content = content,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnTabTappedAsync(tab);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHover(tab.Key);
        pointer.PointerExited += (_, _) => {
            if (_hoveredKey == tab.Key)
                SetHover(null);
        };

        border.GestureRecognizers.Add(tap);
        border.GestureRecognizers.Add(pointer);
        return border;
    }

    private async Task OnTabTappedAsync(Tab tab)
    {
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

    // ===== Внешний вид: обычная / наведённая / активная вкладка =====
    private void ApplyStates()
    {
        foreach (var (key, border) in _tabs) {
            bool active = key == ActiveTab;
            bool hover = key == _hoveredKey;

            // Фон: активная > наведённая > прозрачная.
            // DynamicResource, чтобы цвета менялись вместе с темой.
            if (active) {
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, "TabActiveBg");
            } else if (hover) {
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardHoverBg");
            } else {
                border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                border.BackgroundColor = Colors.Transparent;
            }

            border.Scale = hover ? HoverScale : 1.0;

            // Иконка и подпись
            if (border.Content is Layout layout) {
                foreach (var label in layout.Children.OfType<Label>())
                    label.SetDynamicResource(Label.TextColorProperty, active ? "AccentGreen" : "TextSecondary");
            }
        }
    }
}
