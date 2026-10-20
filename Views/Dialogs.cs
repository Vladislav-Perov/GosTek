using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

/// <summary>Смысл окна: от него зависят цвет и значок.</summary>
public enum DialogKind
{
    /// <summary>Синий кружок «i».</summary>
    Info,
    /// <summary>Зелёная галочка в круге.</summary>
    Success,
    /// <summary>Жёлтый треугольник: «Сохранить с ошибками?».</summary>
    Warning,
    /// <summary>Красный треугольник: ошибки («Не удалось сохранить»).</summary>
    Danger,
    /// <summary>Красная корзина: удаление данных.</summary>
    Delete,
    /// <summary>Синяя дискета: вопросы про сохранение.</summary>
    Question,
}

/// <summary>Вид кнопки окна.</summary>
public enum DialogButtonStyle
{
    /// <summary>Главная: зелёная заливка.</summary>
    Primary,
    /// <summary>Опасная: красная заливка (удаление).</summary>
    Danger,
    /// <summary>Красная рамка без заливки («Выйти без сохранения»).</summary>
    DangerOutline,
    /// <summary>Серая рамка («Отмена», «Остаться»).</summary>
    Secondary,
}

public sealed record DialogButton(string Text, DialogButtonStyle Style = DialogButtonStyle.Secondary);

/// <summary>
/// Окна подтверждения в одном стиле: на телефоне шторка снизу, на ПК карточка по центру.
/// Кнопки на всю ширину, главная сверху, «Отмена» внизу.
/// Окно рисуется поверх текущей страницы и работает одинаково на Android и Windows.
/// </summary>
public static class Dialogs
{
    private const double PhoneMaxWidth = 600;   // уже этого: шторка снизу, шире: карточка по центру
    private const double CardWidth = 380;

    private static readonly SemaphoreSlim Gate = new(1, 1);   // окна показываются по очереди
    private static Action? _dismiss;                          // закрыть текущее окно как «отмена»

    /// <summary>Окно с одной кнопкой. По умолчанию «Понятно».</summary>
    public static Task AlertAsync(string title, string text, DialogKind kind = DialogKind.Info, string ok = "Понятно")
        => ChoiceAsync(title, text, kind, new DialogButton(ok, DialogButtonStyle.Primary));

    /// <summary>Подтверждение: true — нажали главную кнопку. danger = true: красная кнопка (удаление).</summary>
    public static async Task<bool> ConfirmAsync(string title, string text, string accept,
        DialogKind kind = DialogKind.Warning, string cancel = "Отмена", bool danger = false)
    {
        var index = await ChoiceAsync(title, text, kind,
            new DialogButton(accept, danger ? DialogButtonStyle.Danger : DialogButtonStyle.Primary),
            new DialogButton(cancel));

        return index == 0;
    }

    /// <summary>Несколько кнопок. Возвращает номер нажатой или -1, если окно закрыли (фон, «Назад»).</summary>
    public static async Task<int> ChoiceAsync(string title, string text, DialogKind kind, params DialogButton[] buttons)
    {
        await Gate.WaitAsync();
        try {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return -1;

            return await ShowAsync(page, title, text, kind, buttons);
        } finally {
            Gate.Release();
        }
    }

    /// <summary>Для кнопки «Назад» на Android: закрывает окно, если оно открыто. true — окно было.</summary>
    public static bool TryDismiss()
    {
        var dismiss = _dismiss;
        if (dismiss is null)
            return false;

        dismiss();
        return true;
    }

    // =====================================================================
    //  ОТРИСОВКА
    // =====================================================================

    private static async Task<int> ShowAsync(Page page, string title, string text, DialogKind kind, DialogButton[] buttons)
    {
        var root = EnsureGrid(page);
        var phone = (page.Width > 0 ? page.Width : 400) < PhoneMaxWidth;
        var dark = ThemeService.IsDark;

        var cardBg = Res("CardBg", dark ? "#1E293B" : "#FFFFFF");
        var stroke = Res("CardStroke", dark ? "#334155" : "#E2E8F0");
        var textPrimary = Res("TextPrimary", dark ? "#F1F5F9" : "#0F172A");
        var textSecondary = Res("TextSecondary", dark ? "#94A3B8" : "#64748B");
        var (iconBg, iconFg, glyph) = IconStyle(kind, dark);

        var tcs = new TaskCompletionSource<int>();
        var done = false;

        // Объявлены заранее: на них ссылается Close(), который кнопки вызывают позже
        Border card = null!;
        Grid overlay = null!;

        // ----- затемнение -----
        var scrim = new BoxView { Color = Color.FromArgb("#99000000"), Opacity = 0 };

        // ----- содержимое карточки -----
        var content = new VerticalStackLayout { Spacing = 0 };

        if (phone)
            content.Add(new BoxView {
                WidthRequest = 38, HeightRequest = 4, CornerRadius = 2,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Color = stroke,
            });

        var icon = new Border {
            WidthRequest = 44, HeightRequest = 44,
            VerticalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = iconBg,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Content = new Microsoft.Maui.Controls.Shapes.Path {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(glyph)!,
                Stroke = new SolidColorBrush(iconFg),
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Aspect = Stretch.Uniform,
                WidthRequest = 22, HeightRequest = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };

        var message = new VerticalStackLayout { Spacing = 6 };
        message.Add(new Label {
            Text = title, TextColor = textPrimary, FontSize = 19,
            FontFamily = "PTSansBold",
        });

        if (!string.IsNullOrWhiteSpace(text))
            message.Add(new Label {
                Text = text, TextColor = textSecondary, FontSize = 15, LineHeight = 1.25,
            });

        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 14 };
        head.Add(icon, 0, 0);
        head.Add(new ScrollView {
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            MaximumHeightRequest = Math.Max(120, (page.Height > 0 ? page.Height : 700) * 0.45),
            Content = message,
        }, 1, 0);
        content.Add(head);

        // ----- кнопки: главная сверху, серые («Отмена», «Остаться») внизу -----
        var order = buttons
            .Select((b, i) => (Button: b, Index: i))
            .OrderBy(x => x.Button.Style == DialogButtonStyle.Secondary ? 1 : 0)
            .ToList();

        var buttonStack = new VerticalStackLayout { Spacing = 8, Margin = new Thickness(0, 20, 0, 0) };

        foreach (var (button, index) in order) {
            var i = index;
            buttonStack.Add(MakeButton(button, dark, stroke, textSecondary, () => Close(i)));
        }

        content.Add(buttonStack);

        // ----- сама карточка -----
        card = new Border {
            BackgroundColor = cardBg,
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1,
            Padding = phone ? new Thickness(22, 12, 22, 26) : new Thickness(22, 24, 22, 20),
            StrokeShape = new RoundRectangle {
                CornerRadius = phone ? new CornerRadius(24, 24, 0, 0) : new CornerRadius(22),
            },
            HorizontalOptions = phone ? LayoutOptions.Fill : LayoutOptions.Center,
            VerticalOptions = phone ? LayoutOptions.End : LayoutOptions.Center,
            WidthRequest = phone ? -1 : CardWidth,
            Margin = phone ? new Thickness(0, 0, 0, -1) : new Thickness(16),
            Content = content,
        };

        // Нажатие на саму карточку не должно закрывать окно
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        overlay = new Grid { ZIndex = 1000, InputTransparent = false };
        overlay.Add(scrim);
        overlay.Add(card);

        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));

        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Close(-1)) });

        // Начальное положение до анимации
        if (phone)
            card.TranslationY = (page.Height > 0 ? page.Height : 800);
        else {
            card.Opacity = 0;
            card.Scale = 0.94;
        }

        root.Add(overlay);
        _dismiss = () => Close(-1);

        _ = Open();
        return await tcs.Task;

        // ----- анимации -----
        async Task Open()
        {
            try {
                await Task.Yield();

                if (phone)
                    await Task.WhenAll(
                        scrim.FadeToAsync(1, 180, Easing.CubicOut),
                        card.TranslateToAsync(0, 0, 260, Easing.CubicOut));
                else
                    await Task.WhenAll(
                        scrim.FadeToAsync(1, 160, Easing.CubicOut),
                        card.FadeToAsync(1, 160, Easing.CubicOut),
                        card.ScaleToAsync(1, 180, Easing.CubicOut));
            } catch {
                // анимация не должна ломать окно
            }
        }

        async void Close(int result)
        {
            if (done)
                return;

            done = true;
            _dismiss = null;

            try {
                if (phone)
                    await Task.WhenAll(
                        scrim.FadeToAsync(0, 160, Easing.CubicIn),
                        card.TranslateToAsync(0, card.Height + 40, 200, Easing.CubicIn));
                else
                    await Task.WhenAll(
                        scrim.FadeToAsync(0, 140, Easing.CubicIn),
                        card.FadeToAsync(0, 140, Easing.CubicIn),
                        card.ScaleToAsync(0.96, 140, Easing.CubicIn));
            } catch {
                // не мешаем закрытию
            }

            root.Remove(overlay);
            tcs.TrySetResult(result);
        }
    }

    private static View MakeButton(DialogButton button, bool dark, Color stroke, Color secondaryText, Action onTap)
    {
        Color bg = Colors.Transparent, fg, border = Colors.Transparent;

        switch (button.Style) {
            case DialogButtonStyle.Primary:
                bg = Color.FromArgb(dark ? "#34D399" : "#059669");
                fg = Color.FromArgb(dark ? "#04281D" : "#FFFFFF");
                break;
            case DialogButtonStyle.Danger:
                bg = Color.FromArgb(dark ? "#F87171" : "#DC2626");
                fg = Color.FromArgb(dark ? "#3B0A0A" : "#FFFFFF");
                break;
            case DialogButtonStyle.DangerOutline:
                fg = Color.FromArgb(dark ? "#F87171" : "#DC2626");
                border = Color.FromArgb(dark ? "#7F3A3F" : "#FCA5A5");
                break;
            default:
                fg = dark ? Color.FromArgb("#CBD5E1") : Color.FromArgb("#334155");
                border = stroke;
                break;
        }

        var box = new Border {
            BackgroundColor = bg,
            Stroke = new SolidColorBrush(border),
            StrokeThickness = button.Style is DialogButtonStyle.DangerOutline or DialogButtonStyle.Secondary ? 1 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Padding = new Thickness(16, 13),
            Content = new Label {
                Text = button.Text, TextColor = fg, FontSize = 16,
                FontFamily = "PTSansBold",
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
        };

        box.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onTap) });

        // Подсветка под мышью (на телефоне не срабатывает)
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => box.Opacity = 0.88;
        pointer.PointerExited += (_, _) => box.Opacity = 1;
        box.GestureRecognizers.Add(pointer);

        SemanticProperties.SetDescription(box, button.Text);
        return box;
    }

    // Значки как в макете (Tabler, сетка 24x24, линия 2px)
    private const string IconAlert = "M12 9v4M10.363 3.591l-8.106 13.534a1.914 1.914 0 0 0 1.636 2.871h16.214a1.914 1.914 0 0 0 1.636 -2.87l-8.106 -13.536a1.914 1.914 0 0 0 -3.274 0zM12 16h0.01";
    private const string IconTrash = "M4 7h16M10 11v6M14 11v6M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3";
    private const string IconCheck = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M9 12l2 2l4 -4";
    private const string IconSave = "M6 4h10l4 4v10a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2M10 14a2 2 0 1 0 4 0a2 2 0 1 0 -4 0M14 4v4h-6v-4";
    private const string IconInfo = "M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0M12 9h0.01M11 12h1v4h1";

    // Цвет плашки, цвет значка и контур значка для каждого смысла окна
    private static (Color Bg, Color Fg, string Glyph) IconStyle(DialogKind kind, bool dark) => kind switch {
        DialogKind.Warning => (Color.FromArgb(dark ? "#3A2F12" : "#FEF3C7"), Color.FromArgb(dark ? "#FBBF24" : "#B45309"), IconAlert),
        DialogKind.Danger => (Color.FromArgb(dark ? "#3B1F26" : "#FEE2E2"), Color.FromArgb(dark ? "#F87171" : "#DC2626"), IconAlert),
        DialogKind.Delete => (Color.FromArgb(dark ? "#3B1F26" : "#FEE2E2"), Color.FromArgb(dark ? "#F87171" : "#DC2626"), IconTrash),
        DialogKind.Success => (Color.FromArgb(dark ? "#12352B" : "#D1FAE5"), Color.FromArgb(dark ? "#34D399" : "#059669"), IconCheck),
        DialogKind.Question => (Color.FromArgb(dark ? "#1B2F52" : "#DBEAFE"), Color.FromArgb(dark ? "#7DB4FF" : "#2563EB"), IconSave),
        _ => (Color.FromArgb(dark ? "#1B2F52" : "#DBEAFE"), Color.FromArgb(dark ? "#7DB4FF" : "#2563EB"), IconInfo),
    };

    // Цвет из ресурсов темы; если ресурса нет, берём запасной
    private static Color Res(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);

    // Корень страницы должен быть Grid, чтобы положить окно поверх всего. Если нет, оборачиваем.
    private static Grid EnsureGrid(Page page)
    {
        if (page is ContentPage { Content: Grid grid })
            return grid;

        if (page is not ContentPage contentPage)
            throw new InvalidOperationException("Окно можно показать только на ContentPage.");

        var wrapper = new Grid();
        var old = contentPage.Content;
        contentPage.Content = null;

        if (old is not null)
            wrapper.Add(old);

        contentPage.Content = wrapper;
        return wrapper;
    }
}
