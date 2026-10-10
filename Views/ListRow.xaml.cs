namespace GosTek.Views;

/// <summary>
/// Строка списка: Icon, Title, Subtitle и событие Tapped.
/// Используется для шаблонов (главная, «Шаблоны») и для способов экспорта.
/// </summary>
public partial class ListRow : ContentView
{
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).IconLabel.Text = (string)v);

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).TitleLabel.Text = (string)v);

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).SubtitleLabel.Text = (string)v);

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>sender — сама строка, у неё BindingContext = элемент списка.</summary>
    public event EventHandler? Tapped;

    public ListRow()
    {
        InitializeComponent();
    }

    private void OnTapped(object? sender, TappedEventArgs e) => Tapped?.Invoke(this, EventArgs.Empty);
}
