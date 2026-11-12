/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Помощник микроанимаций (каскадное появление, бесконечная пульсация акцентных кнопок)
 * СОДЕРЖИМОЕ: AnimationHelper
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls
 * ==============================================================================
 */
namespace GosTek.Helpers;

/// <summary>
/// Утилитарный хелпер для легковесных и плавных UI-микроанимаций (Staggered Entrance, Pulse Effect).
/// </summary>
public static class AnimationHelper
{
    /// <summary>
    /// Флаг принудительного отключения анимаций (уменьшение движения / энергосбережение).
    /// </summary>
    public static bool ReduceMotionEnabled { get; set; } = false;

    /// <summary>
    /// Каскадное появление коллекции визуальных элементов (плавный выезд снизу с нарастанием прозрачности).
    /// </summary>
    /// <param name="elements">Коллекция элементов для анимации.</param>
    /// <param name="duration">Длительность анимации каждого элемента в миллисекундах (по умолчанию 300 мс).</param>
    /// <param name="delayMs">Задержка между стартом анимации соседних элементов (по умолчанию 50 мс).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task AnimateStaggeredEntranceAsync(
        IEnumerable<VisualElement?>? elements,
        uint duration = 300,
        uint delayMs = 50,
        CancellationToken cancellationToken = default)
    {
        if (elements is null)
            return;

        var list = elements.Where(e => e is not null).Cast<VisualElement>().ToList();
        if (list.Count == 0)
            return;

        if (ReduceMotionEnabled)
        {
            foreach (var element in list)
            {
                element.Opacity = 1.0;
                element.TranslationY = 0;
            }
            return;
        }

        // 1. Предварительная установка начального скрытого состояния
        foreach (var element in list)
        {
            element.Opacity = 0;
            element.TranslationY = 20;
        }

        // 2. Последовательный каскадный запуск анимаций
        foreach (var element in list)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            _ = Task.WhenAll(
                element.FadeToAsync(1.0, duration, Easing.CubicOut),
                element.TranslateToAsync(0, 0, duration, Easing.CubicOut));

            if (delayMs > 0)
            {
                try
                {
                    await Task.Delay((int)delayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Запускает бесконечную циклическую пульсацию элемента (Scale 1.0 -> 1.03 -> 1.0).
    /// </summary>
    /// <param name="element">Элемент для пульсации.</param>
    /// <param name="cancellationToken">Токен отмены для остановки цикла.</param>
    public static async Task AnimatePulseAsync(
        VisualElement? element,
        CancellationToken cancellationToken = default)
    {
        if (element is null || ReduceMotionEnabled)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await element.ScaleToAsync(1.03, 800, Easing.SinInOut);
                if (cancellationToken.IsCancellationRequested)
                    break;

                await element.ScaleToAsync(1.0, 800, Easing.SinInOut);
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Корректная остановка по токену
        }
        catch
        {
            // Подавление ошибок анимации при отсоединении элемента
        }
        finally
        {
            StopPulse(element);
        }
    }

    /// <summary>
    /// Немедленно останавливает анимацию пульсации и сбрасывает масштаб элемента в исходное состояние (1.0).
    /// </summary>
    /// <param name="element">Элемент, анимации которого необходимо остановить.</param>
    public static void StopPulse(VisualElement? element)
    {
        if (element is null || ReduceMotionEnabled)
            return;

        try
        {
            element.CancelAnimations();
            element.Scale = 1.0;
        }
        catch
        {
            // Подавление ошибок при разрушении визуального дерева
        }
    }
}
