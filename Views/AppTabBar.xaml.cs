namespace GosTek.Views;

/// <summary>
/// Общий нижний таббар. Страница указывает активную вкладку: ActiveTab="home".
/// Ключи вкладок: home, templates, scan, check, profile.
/// </summary>
public partial class AppTabBar : ContentView
{
    private const double HoverScale = 1.08;

    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(AppTabBar), string.Empty,
        propertyChanged: (bindable, _, _) => ((AppTabBar)bindable).ApplyStates());

    public string ActiveTab
    {
        get => (string)GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    // Вкладки, у которых есть реальный переход. Остальные показывают заглушку.
    private static readonly Dictionary<string, string> Routes = new() {
        ["home"] = "//MainPage",
        ["templates"] = "//TemplatesPage",
    };

    private readonly List<(string Key, Border Border)> _tabs;
    private string? _hoveredKey;

    public AppTabBar()
    {
        InitializeComponent();

        _tabs = new()
        {
            ("home", TabHome),
            ("templates", TabTemplates),
            ("scan", TabScan),
            ("check", TabCheck),
            ("profile", TabProfile),
        };

        foreach (var (key, border) in _tabs)
            AttachGestures(key, border);

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

    // ===== Жесты =====
    private void AttachGestures(string key, Border border)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await OnTabTappedAsync(key);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHover(key);
        pointer.PointerExited += (_, _) => {
            if (_hoveredKey == key)
                SetHover(null);
        };

        border.GestureRecognizers.Add(tap);
        border.GestureRecognizers.Add(pointer);
    }

    private async Task OnTabTappedAsync(string key)
    {
        if (key == ActiveTab)
            return;

        ResetHover();

        if (Routes.TryGetValue(key, out var route))
            await Shell.Current.GoToAsync(route);
        else
            await Stubs.ShowAsync(key);
    }

    private void SetHover(string? key)
    {
        _hoveredKey = key;
        ApplyStates();
    }

    // ===== Внешний вид: обычная / наведённая / активная вкладка =====
    private void ApplyStates()
    {
        // _tabs ещё не создан, пока свойство задаётся из конструктора базового класса
        if (_tabs is null)
            return;

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