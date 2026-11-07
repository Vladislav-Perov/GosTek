namespace GosTek.Services;

/// <summary>
/// Подсветка поля ввода при ошибке: красная рамка и подпись под полем.
/// Общий код для экранов «Поля шаблона» и «Профиль».
/// </summary>
public static class FieldValidationHelper
{
    /// <summary>
    /// Включает или выключает подсветку. message — новый текст подписи (если не null и поле неверное).
    /// При снятии подсветки рамка возвращается к стилю FieldBox, поэтому цвет снова следует за темой.
    /// </summary>
    public static void Mark(Border box, Label error, bool invalid, string? message = null)
    {
        if (invalid) {
            box.Stroke = new SolidColorBrush(ThemeService.ErrorColor);
            box.StrokeThickness = 1.5;
            if (message is not null)
                error.Text = message;
        } else {
            box.ClearValue(Border.StrokeProperty);
            box.ClearValue(Border.StrokeThicknessProperty);
        }

        error.IsVisible = invalid;
    }

    /// <summary>
    /// Подсвечивает поле по тексту ошибки (null = ошибки нет).
    /// Возвращает true, если ошибки нет.
    /// </summary>
    public static bool Apply(Border box, Label error, string? errorText)
    {
        Mark(box, error, errorText is not null, errorText);
        return errorText is null;
    }
}
