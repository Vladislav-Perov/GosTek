/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Кроссплатформенный движок модальных диалоговых окон и шторок выбора по дизайн-системе
 * СОДЕРЖИМОЕ: Dialogs, DialogButton, DialogKind, DialogButtonStyle
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.ThemeService
 * ==============================================================================
 */
using Microsoft.Maui.Controls.Shapes;
using GosTek.Resources.Styles;

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
    /// <summary>Главная: изумрудная заливка.</summary>
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
    private const double CardWidth = 400;

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
        try
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return -1;

            return await ShowAsync(page, title, text, kind, buttons);
        }
        finally
        {
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

        var cardBg = Res("CardBg", dark ? "#23282D" : "#FFFFFF");
        var stroke = Res("CardStroke", dark ? "#414950" : "#D4DCE3");
        var textPrimary = Res("TextPrimary", dark ? "#EDF0F2" : "#202B35");
        var textSecondary = Res("TextSecondary", dark ? "#B7BEC5" : "#51606F");
        var (iconBg, iconFg, glyph) = IconStyle(kind, dark);

        var tcs = new TaskCompletionSource<int>();
        var done = false;

        // Объявлены заранее: на них ссылается Close(), который кнопки вызывают позже
        Border card = null!;
        Grid overlay = null!;

        // ----- затемнение -----
        var scrim = new BoxView { Color = Color.FromArgb(SettingsOverlay.IsOpen ? "#40000000" : "#8A000000"), Opacity = 0 };

        // ----- содержимое карточки -----
        var content = new VerticalStackLayout { Spacing = 0 };

        if (phone)
        {
            content.Add(new BoxView
            {
                WidthRequest = 42,
                HeightRequest = 4,
                CornerRadius = 2,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 0, 16),
                Color = stroke,
            });
        }

        var icon = new Border
        {
            WidthRequest = 46,
            HeightRequest = 46,
            VerticalOptions = LayoutOptions.Start,
            StrokeThickness = 0,
            BackgroundColor = iconBg,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = new Microsoft.Maui.Controls.Shapes.Path
            {
                Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(glyph)!,
                Stroke = new SolidColorBrush(iconFg),
                StrokeThickness = 2,
                StrokeLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Aspect = Stretch.Uniform,
                WidthRequest = 22,
                HeightRequest = 22,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };

        var message = new VerticalStackLayout { Spacing = 6 };
        message.Add(new Label
        {
            Text = title,
            TextColor = textPrimary,
            FontSize = 18.5,
            FontAttributes = FontAttributes.Bold,
        });

        if (!string.IsNullOrWhiteSpace(text))
        {
            message.Add(new Label
            {
                Text = text,
                TextColor = textSecondary,
                FontSize = 14,
                LineHeight = 1.25,
            });
        }

        var head = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 14,
        };
        head.Add(icon, 0, 0);
        head.Add(new ScrollView
        {
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

        var buttonStack = new VerticalStackLayout { Spacing = 10, Margin = new Thickness(0, 20, 0, 0) };

        foreach (var (button, index) in order)
        {
            var i = index;
            buttonStack.Add(MakeButton(button, dark, stroke, textPrimary, textSecondary, () => Close(i)));
        }

        content.Add(buttonStack);

        // ----- сама карточка -----
        card = new Border
        {
            BackgroundColor = cardBg,
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1,
            Padding = phone ? new Thickness(22, 14, 22, 28) : new Thickness(24, 24, 24, 22),
            StrokeShape = new RoundRectangle
            {
                CornerRadius = phone ? new CornerRadius(26, 26, 0, 0) : new CornerRadius(22),
            },
            HorizontalOptions = phone ? LayoutOptions.Fill : LayoutOptions.Center,
            VerticalOptions = phone ? LayoutOptions.End : LayoutOptions.Center,
            WidthRequest = phone ? -1 : CardWidth,
            Margin = phone ? new Thickness(0, 0, 0, -1) : new Thickness(16),
            Content = content,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Colors.Black),
                Offset = new Point(0, 8),
                Radius = 24,
                Opacity = 0.22f,
            },
        };

        // Нажатие на саму карточку не должно закрывать окно
        card.GestureRecognizers.Add(new TapGestureRecognizer());

        overlay = new Grid { ZIndex = SettingsOverlay.OverlayZIndex + 1, InputTransparent = false };
        overlay.Add(scrim);
        overlay.Add(card);

        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(overlay, Math.Max(1, root.ColumnDefinitions.Count));

        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Close(-1)) });

        // Начальное положение до анимации
        if (phone)
        {
            card.TranslationY = (page.Height > 0 ? page.Height : 800);
        }
        else
        {
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
            try
            {
                await Task.Yield();

                if (phone)
                {
                    await Task.WhenAll(
                        scrim.FadeToAsync(1, 180, Easing.CubicOut),
                        card.TranslateToAsync(0, 0, 260, Easing.CubicOut));
                }
                else
                {
                    await Task.WhenAll(
                        scrim.FadeToAsync(1, 160, Easing.CubicOut),
                        card.FadeToAsync(1, 160, Easing.CubicOut),
                        card.ScaleToAsync(1, 180, Easing.CubicOut));
                }
            }
            catch (Exception ex)
            {
                // анимация не должна ломать окно
                System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            }
        }

        async void Close(int result)
        {
            if (done)
                return;

            done = true;
            _dismiss = null;

            try
            {
                if (phone)
                {
                    await Task.WhenAll(
                        scrim.FadeToAsync(0, 160, Easing.CubicIn),
                        card.TranslateToAsync(0, card.Height + 40, 200, Easing.CubicIn));
                }
                else
                {
                    await Task.WhenAll(
                        scrim.FadeToAsync(0, 140, Easing.CubicIn),
                        card.FadeToAsync(0, 140, Easing.CubicIn),
                        card.ScaleToAsync(0.96, 140, Easing.CubicIn));
                }
            }
            catch (Exception ex)
            {
                // не мешаем закрытию
                System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            }

            root.Remove(overlay);
            tcs.TrySetResult(result);
        }
    }

    private static View MakeButton(DialogButton button, bool dark, Color stroke, Color primaryText, Color secondaryText, Action onTap)
    {
        Color bg = Colors.Transparent, fg, border = Colors.Transparent;

        switch (button.Style)
        {
            case DialogButtonStyle.Primary:
                bg = Res("ActionBg", dark ? "#324A62" : "#263B52");
                fg = Colors.White;
                break;
            case DialogButtonStyle.Danger:
                bg = Color.FromArgb("#DC2626");
                fg = Colors.White;
                break;
            case DialogButtonStyle.DangerOutline:
                fg = Color.FromArgb(dark ? "#EF4444" : "#DC2626");
                border = Color.FromArgb(dark ? "#7F2A32" : "#FCA5A5");
                break;
            default:
                fg = primaryText;
                border = stroke;
                break;
        }

        var box = new Border
        {
            BackgroundColor = bg,
            Stroke = new SolidColorBrush(border),
            StrokeThickness = button.Style is DialogButtonStyle.DangerOutline or DialogButtonStyle.Secondary ? 1 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(16, 13),
            Content = new Label
            {
                Text = button.Text,
                TextColor = fg,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
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

    // Цвет плашки, цвет значка и контур значка для каждого смысла окна
    private static (Color Bg, Color Fg, string Glyph) IconStyle(DialogKind kind, bool dark) => kind switch
    {
        DialogKind.Warning => (Res("WarnSoftBg", dark ? "#453720" : "#FCEFD6"), Res("WarnColor", dark ? "#F1C778" : "#805003"), Icons.AlertTriangle),
        DialogKind.Danger => (Res("BadSoftBg", dark ? "#4A2625" : "#FBE5E2"), Res("BadColor", dark ? "#FFABA4" : "#B42318"), Icons.AlertTriangle),
        DialogKind.Delete => (Res("BadSoftBg", dark ? "#4A2625" : "#FBE5E2"), Res("BadColor", dark ? "#FFABA4" : "#B42318"), Icons.Trash),
        DialogKind.Success => (Res("IconBoxBg", dark ? "#363F49" : "#E7EDF3"), Res("BrandGreen", dark ? "#BACBD9" : "#263B52"), Icons.Check),
        DialogKind.Question => (Color.FromArgb(dark ? "#162842" : "#DBEAFE"), Color.FromArgb(dark ? "#60A5FA" : "#2563EB"), Icons.Save),
        _ => (Color.FromArgb(dark ? "#162842" : "#DBEAFE"), Color.FromArgb(dark ? "#60A5FA" : "#2563EB"), Icons.Info),
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
