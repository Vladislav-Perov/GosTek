/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Универсальный компонент гибридного ввода дат (ручной ввод с маской ДД.ММ.ГГГГ + нативный календарь)
 * СОДЕРЖИМОЕ: DateInputField (ContentView)
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Services.SmartInputHelper, GosTek.Services.HapticHelper
 * ==============================================================================
 */
using GosTek.Services;
using Microsoft.Maui.Controls;
using MauiDatePicker = Microsoft.Maui.Controls.DatePicker;
using MauiDateChangedEventArgs = Microsoft.Maui.Controls.DateChangedEventArgs;

namespace GosTek.Views.Controls;

/// <summary>
/// Поле гибридного ввода даты:
/// 1. Текстовый ввод (Entry) с автоматической маской «ДД.ММ.ГГГГ» и числовой клавиатурой.
/// 2. Кнопка календаря (📅), открывающая системный DatePicker на выбранной дате.
/// 3. Двусторонняя синхронизация ввода и выбора даты.
/// </summary>
public class DateInputField : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(DateInputField),
            string.Empty,
            BindingMode.TwoWay,
            propertyChanged: (b, _, n) => ((DateInputField)b).OnTextPropertyChanged((string?)n));

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(
            nameof(Placeholder),
            typeof(string),
            typeof(DateInputField),
            "ДД.ММ.ГГГГ",
            propertyChanged: (b, _, n) => ((DateInputField)b)._entry.Placeholder = (string?)n);

    public static readonly BindableProperty DateProperty =
        BindableProperty.Create(
            nameof(Date),
            typeof(DateTime?),
            typeof(DateInputField),
            null,
            BindingMode.TwoWay,
            propertyChanged: (b, _, n) => ((DateInputField)b).OnDatePropertyChanged((DateTime?)n));

    public event EventHandler<TextChangedEventArgs>? TextChanged;
    public event EventHandler<MauiDateChangedEventArgs>? DateSelected;

    private readonly Entry _entry;
    private readonly MauiDatePicker _picker;
    private readonly Border _calendarBtn;
    private readonly Border _box;

    private bool _isFormatting;
    private string _previousText = "";

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public DateTime? Date
    {
        get => (DateTime?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public Entry Entry => _entry;
    public MauiDatePicker Picker => _picker;
    public Border Box => _box;

    public DateInputField()
    {
        _entry = new Entry
        {
            Placeholder = "ДД.ММ.ГГГГ",
            Keyboard = Keyboard.Numeric,
            MaxLength = 10,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };
        _entry.StyleClass = new[] { "FieldEntry" };

        _entry.TextChanged += OnEntryTextChanged;

        _picker = new MauiDatePicker
        {
            Format = "dd.MM.yyyy",
            Opacity = 0.011,
            WidthRequest = 36,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        _picker.DateSelected += OnPickerDateSelected;

        var calIcon = new IconView
        {
            Icon = "calendar",
            Size = 19,
            IconColor = "BrandGreen",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        _calendarBtn = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(6, 4),
            Content = calIcon,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            HapticHelper.Click();
            if (SmartInputHelper.TryParseDate(_entry.Text, out var dt))
                _picker.Date = dt;
            else
                _picker.Date = DateTime.Today;

            OpenPicker(_picker);
        };
        _calendarBtn.GestureRecognizers.Add(tap);

        var calHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { _calendarBtn, _picker },
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { _entry, calHost },
        };

        Grid.SetColumn(_entry, 0);
        Grid.SetColumn(calHost, 1);

        _box = new Border
        {
            Padding = new Thickness(16, 0),
            Content = grid,
            HorizontalOptions = LayoutOptions.Fill,
        };
        _box.StyleClass = new[] { "FieldBox" };

        Content = _box;
    }

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isFormatting)
            return;

        _isFormatting = true;
        try
        {
            string formatted = SmartInputHelper.ApplyDateMask(e.NewTextValue, _previousText);
            if (formatted != e.NewTextValue)
            {
                int cursor = _entry.CursorPosition;
                _entry.Text = formatted;
                _entry.CursorPosition = Math.Min(cursor + (formatted.Length - (e.NewTextValue?.Length ?? 0)), formatted.Length);
            }

            _previousText = formatted;

            if (Text != formatted)
                Text = formatted;

            if (SmartInputHelper.TryParseDate(formatted, out var dt))
            {
                if (Date != dt)
                    Date = dt;
            }
            else
            {
                if (Date != null)
                    Date = null;
            }

            TextChanged?.Invoke(this, e);
        }
        finally
        {
            _isFormatting = false;
        }
    }

    private void OnPickerDateSelected(object? sender, MauiDateChangedEventArgs e)
    {
        DateTime? selectedDate = _picker.Date;
        string formatted = _picker.Date?.ToString("dd.MM.yyyy") ?? "";
        if (!string.IsNullOrEmpty(formatted) && _entry.Text != formatted)
        {
            _entry.Text = formatted;
        }

        if (selectedDate.HasValue && Date != selectedDate)
            Date = selectedDate;

        DateSelected?.Invoke(this, e);
    }

    private void OnTextPropertyChanged(string? newText)
    {
        if (_entry.Text != newText)
        {
            _entry.Text = newText ?? "";
        }
    }

    private void OnDatePropertyChanged(DateTime? newDate)
    {
        if (newDate.HasValue)
        {
            string formatted = newDate.Value.ToString("dd.MM.yyyy");
            if (_entry.Text != formatted)
                _entry.Text = formatted;
            if (_picker.Date != newDate.Value)
                _picker.Date = newDate.Value;
        }
    }

    /// <summary>
    /// Программно открывает системный DatePicker на текущей платформе.
    /// </summary>
    public static void OpenPicker(MauiDatePicker picker)
    {
        try
        {
#if WINDOWS
            if (picker.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.CalendarDatePicker winPicker)
            {
                winPicker.IsCalendarOpen = true;
                return;
            }
#elif ANDROID
            if (picker.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText androidEdit)
            {
                androidEdit.PerformClick();
                return;
            }
#endif
            picker.Focus();
        }
        catch
        {
            picker.Focus();
        }
    }
}
