/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Унифицированный компонент строки списка с иконкой, заголовком, избранным и подсветкой поиска
 * СОДЕРЖИМОЕ: ListRow
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Services.HapticHelper, GosTek.Controls.BounceEffect
 * ==============================================================================
 */
using GosTek.Controls;
using GosTek.Services;

namespace GosTek.Views;

/// <summary>
/// Строка списка: Icon, Title, Subtitle, IsFavorite, ShowFavorite и событие Tapped.
/// Используется для шаблонов (главная, «Шаблоны») и для способов экспорта.
/// </summary>
public partial class ListRow : ContentView
{
    private bool _justToggledFavorite;

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).IconLabel.Text = (string)v);

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).UpdateTitle((string)v));

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(ListRow), "",
        propertyChanged: (b, _, v) => ((ListRow)b).SubtitleLabel.Text = (string)v);

    public static readonly BindableProperty ShowFavoriteProperty = BindableProperty.Create(
        nameof(ShowFavorite), typeof(bool), typeof(ListRow), false,
        propertyChanged: (b, _, v) => ((ListRow)b).FavoriteButton.IsVisible = (bool)v);

    public static readonly BindableProperty IsFavoriteProperty = BindableProperty.Create(
        nameof(IsFavorite), typeof(bool), typeof(ListRow), false,
        propertyChanged: (b, _, v) => ((ListRow)b).UpdateFavoriteState((bool)v));

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

    public bool ShowFavorite
    {
        get => (bool)GetValue(ShowFavoriteProperty);
        set => SetValue(ShowFavoriteProperty, value);
    }

    public bool IsFavorite
    {
        get => (bool)GetValue(IsFavoriteProperty);
        set => SetValue(IsFavoriteProperty, value);
    }

    /// <summary>sender — сама строка, у неё BindingContext = элемент списка.</summary>
    public event EventHandler? Tapped;

    /// <summary>Событие нажатия на кнопку избранного.</summary>
    public event EventHandler? FavoriteTapped;

    public ListRow()
    {
        InitializeComponent();
    }

    private void UpdateTitle(string title)
    {
        TitleLabel.FormattedText = null;
        TitleLabel.Text = title;
    }

    private void UpdateFavoriteState(bool isFav)
    {
        FavoriteLabel.Text = isFav ? "★" : "☆";
        if (isFav)
            FavoriteLabel.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
        else
            FavoriteLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
    }

    /// <summary>
    /// Подсвечивает совпадение поискового запроса в заголовке с помощью FormattedString.
    /// </summary>
    public void SetHighlightedTitle(string fullTitle, string query, Color highlightColor)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            UpdateTitle(fullTitle);
            return;
        }

        int index = fullTitle.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
        {
            UpdateTitle(fullTitle);
            return;
        }

        var fs = new FormattedString();

        if (index > 0)
        {
            var beforeSpan = new Span { Text = fullTitle.Substring(0, index) };
            beforeSpan.SetDynamicResource(Span.TextColorProperty, "TextPrimary");
            fs.Spans.Add(beforeSpan);
        }

        var matchSpan = new Span
        {
            Text = fullTitle.Substring(index, query.Length),
            TextColor = highlightColor,
            FontAttributes = FontAttributes.Bold,
        };
        fs.Spans.Add(matchSpan);

        if (index + query.Length < fullTitle.Length)
        {
            var afterSpan = new Span { Text = fullTitle.Substring(index + query.Length) };
            afterSpan.SetDynamicResource(Span.TextColorProperty, "TextPrimary");
            fs.Spans.Add(afterSpan);
        }

        TitleLabel.FormattedText = fs;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (_justToggledFavorite)
        {
            _justToggledFavorite = false;
            return;
        }

        await this.AnimateBounceAsync();
        Tapped?.Invoke(this, EventArgs.Empty);
    }

    private void OnFavoriteTapped(object? sender, TappedEventArgs e)
    {
        _justToggledFavorite = true;
        HapticHelper.Click();
        FavoriteTapped?.Invoke(this, EventArgs.Empty);
    }
}
