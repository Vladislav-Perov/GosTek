/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Кастомный горизонтальный скролл-бар чипов выбора категорий и пресетов
 * СОДЕРЖИМОЕ: ChipBar, ChipItem
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls
 * ==============================================================================
 */
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Controls;

/// <summary>
/// Ряд чипов с выбором одного значения (по макету: активный чип залит, остальные с рамкой).
/// Используется на экранах «Шаблоны» (категории), «Скан» (тип документа) и «Проверка» (набор правил).
///
/// Подписи задаёт SetItems, выбор читается из SelectedIndex и события SelectionChanged.
/// ShowArrows = true: по краям появляются стрелки прокрутки, когда чипы не помещаются
/// (на ПК колесо мыши тоже листает ряд).
/// </summary>
public sealed class ChipBar : ContentView
{
    private const double ScrollStep = 200;

    private readonly ScrollView _scroll;
    private readonly HorizontalStackLayout _host;
    private readonly Border _leftArrow;
    private readonly Border _rightArrow;
    private readonly List<(Border Chip, Label Text)> _chips = new();

    private bool _showArrows;
    private int _selected;

    /// <summary>Выбран другой чип (аргумент — его номер). Не вызывается при SetItems.</summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>Отступы внутри ряда: ChipsPadding="20,0" оставляет поля по краям экрана при прокрутке.</summary>
    public Thickness ChipsPadding
    {
        get => _host.Padding;
        set => _host.Padding = value;
    }

    /// <summary>Показывать стрелки прокрутки по краям.</summary>
    public bool ShowArrows
    {
        get => _showArrows;
        set {
            _showArrows = value;
            _scroll.HorizontalScrollBarVisibility = value ? ScrollBarVisibility.Default : ScrollBarVisibility.Never;
            UpdateArrows();
        }
    }

    public int SelectedIndex
    {
        get => _selected;
        set {
            if (value < 0 || value >= _chips.Count || value == _selected)
                return;

            _selected = value;
            UpdateChips();
        }
    }

    public int Count => _chips.Count;

    public ChipBar()
    {
        _host = new HorizontalStackLayout { Spacing = 8, Padding = new Thickness(0, 0, 0, 6) };
        _scroll = new ScrollView {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _host,
        };

        _leftArrow = CreateArrow("M8,1 L2,8 L8,15", new Thickness(0, 0, 6, 0), "Чипы влево", () => ScrollBy(-ScrollStep));
        _rightArrow = CreateArrow("M2,1 L8,8 L2,15", new Thickness(6, 0, 0, 0), "Чипы вправо", () => ScrollBy(ScrollStep));

        var grid = new Grid {
            ColumnDefinitions = {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        Grid.SetColumn(_leftArrow, 0);
        Grid.SetColumn(_scroll, 1);
        Grid.SetColumn(_rightArrow, 2);
        grid.Children.Add(_leftArrow);
        grid.Children.Add(_scroll);
        grid.Children.Add(_rightArrow);
        Content = grid;

        // Стрелки показываем, только когда есть куда листать
        _scroll.Scrolled += (_, _) => UpdateArrows();
        _scroll.SizeChanged += (_, _) => UpdateArrows();
        _host.SizeChanged += (_, _) => UpdateArrows();
#if WINDOWS
        // На ПК колесо мыши по умолчанию не листает горизонтальный список: включаем
        _scroll.HandlerChanged += (_, _) => HookWheel();
#endif
    }

    /// <summary>Заменить чипы. selected — какой выбрать (если номер неверный, первый).</summary>
    public void SetItems(IReadOnlyList<string> titles, int selected = 0)
    {
        _host.Children.Clear();
        _chips.Clear();

        for (int i = 0; i < titles.Count; i++)
            AddChip(titles[i], i);

        _selected = selected >= 0 && selected < titles.Count ? selected : 0;
        UpdateChips();
        UpdateArrows();
    }

    private void AddChip(string title, int index)
    {
        var label = new Label { Text = title, FontSize = 14 };
        var chip = new Border {
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(16, 8),
            Content = label,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => {
            if (index == _selected)
                return;

            _selected = index;
            UpdateChips();
            SelectionChanged?.Invoke(this, index);
        };
        chip.GestureRecognizers.Add(tap);

        _host.Children.Add(chip);
        _chips.Add((chip, label));
    }

    // Активный чип залит, остальные с рамкой. DynamicResource, чтобы цвета менялись вместе с темой
    private void UpdateChips()
    {
        for (int i = 0; i < _chips.Count; i++) {
            var (chip, label) = _chips[i];
            bool active = i == _selected;

            chip.SetDynamicResource(VisualElement.BackgroundColorProperty, active ? "TextPrimary" : "CardBg");
            chip.SetDynamicResource(Border.StrokeProperty, "CardStroke");
            chip.StrokeThickness = active ? 0 : 1;

            label.SetDynamicResource(Label.TextColorProperty, active ? "PageBg" : "TextPrimary");
            label.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;

            SemanticProperties.SetDescription(chip, active ? $"{label.Text}, выбрано" : label.Text);
        }
    }

    // ===== Стрелки =====

    private Border CreateArrow(string geometry, Thickness margin, string description, Action onTap)
    {
        var path = new Microsoft.Maui.Controls.Shapes.Path {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(geometry)!,
            StrokeThickness = 2.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            WidthRequest = 10,
            HeightRequest = 16,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        path.SetDynamicResource(Shape.StrokeProperty, "TextPrimary");

        var arrow = new Border {
            IsVisible = false,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(12, 4),
            Margin = margin,
            VerticalOptions = LayoutOptions.Center,
            Content = path,
        };
        arrow.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        arrow.SetDynamicResource(Border.StrokeProperty, "CardStroke");
        SemanticProperties.SetDescription(arrow, description);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        arrow.GestureRecognizers.Add(tap);
        return arrow;
    }

    private void UpdateArrows()
    {
        if (!_showArrows) {
            _leftArrow.IsVisible = false;
            _rightArrow.IsVisible = false;
            return;
        }

        double max = _scroll.ContentSize.Width - _scroll.Width;
        _leftArrow.IsVisible = _scroll.ScrollX > 1;
        _rightArrow.IsVisible = max > 1 && _scroll.ScrollX < max - 1;
    }

    private async void ScrollBy(double delta)
    {
        try {
            await _scroll.ScrollToAsync(Math.Max(0, _scroll.ScrollX + delta), 0, true);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"ChipBar.ScrollBy: {ex}");
        }
    }

#if WINDOWS
    private void HookWheel()
    {
        if (_scroll.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer sv)
            return;

        sv.PointerWheelChanged += (_, args) => {
            // Без стрелок ряд не листается, а колесо должно прокручивать страницу
            if (!_showArrows)
                return;

            var delta = args.GetCurrentPoint(sv).Properties.MouseWheelDelta;
            sv.ChangeView(sv.HorizontalOffset - delta, null, null);
            args.Handled = true;
        };
    }
#endif
}
