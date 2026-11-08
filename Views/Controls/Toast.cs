/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Компонент всплывающих уведомлений (Toast Notifications) с векторными иконками и плавной анимацией
 * СОДЕРЖИМОЕ: Toast
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Services.HapticHelper, GosTek.Resources.Styles.Icons
 * ==============================================================================
 */
using GosTek.Resources.Styles;
using GosTek.Services;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views.Controls;

/// <summary>
/// Легковесные всплывающие уведомления (тосты), не блокирующие интерфейс.
/// Появляются снизу экрана, отображают векторную иконку с сообщением и плавно исчезают.
/// </summary>
public static class Toast
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    /// Отображает всплывающее уведомление поверх текущей активной страницы.
    /// </summary>
    public static void Show(string message, string icon = "check_ok", int durationMs = 2200)
    {
        _ = ShowAsync(message, icon, durationMs);
    }

    /// <summary>
    /// Асинхронное отображение всплывающего уведомления с анимацией.
    /// </summary>
    public static async Task ShowAsync(string message, string icon = "check_ok", int durationMs = 2200)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        await Gate.WaitAsync();
        try
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null)
                return;

            var root = EnsureGrid(page);

            HapticHelper.Click();

            var vectorPath = Icons.CreatePath(Icons.ResolvePath(icon), size: 16, strokeColorResource: "BrandGreen");

            var pill = new Border
            {
                BackgroundColor = Color.FromArgb("#0F172A"),
                Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(16, 10),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(20, 0, 20, 36),
                InputTransparent = true,
                ZIndex = 9999,
                Opacity = 0,
                TranslationY = 30,
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Colors.Black),
                    Offset = new Point(0, 4),
                    Radius = 12,
                    Opacity = 0.32f,
                },
                Content = new HorizontalStackLayout
                {
                    Spacing = 10,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        vectorPath,
                        new Label
                        {
                            Text = message,
                            FontSize = 13.5,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            VerticalOptions = LayoutOptions.Center,
                            LineBreakMode = LineBreakMode.TailTruncation,
                        },
                    },
                },
            };

            root.Add(pill);

            // Анимация появления
            await Task.WhenAll(
                pill.FadeToAsync(1, 160, Easing.CubicOut),
                pill.TranslateToAsync(0, 0, 200, Easing.CubicOut));

            await Task.Delay(durationMs);

            // Анимация скрытия
            await Task.WhenAll(
                pill.FadeToAsync(0, 160, Easing.CubicIn),
                pill.TranslateToAsync(0, 20, 180, Easing.CubicIn));

            root.Remove(pill);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Toast] Ошибка отображения: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Grid EnsureGrid(Page page)
    {
        if (page is ContentPage { Content: Grid grid })
            return grid;

        if (page is not ContentPage contentPage)
            throw new InvalidOperationException("Тост можно показать только на ContentPage.");

        var wrapper = new Grid();
        var old = contentPage.Content;
        contentPage.Content = null;

        if (old is not null)
            wrapper.Add(old);

        contentPage.Content = wrapper;
        return wrapper;
    }
}
