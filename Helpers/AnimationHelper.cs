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
    /// Ограничено первыми maxAnimatedCount элементами, чтобы в длинных списках элементы ниже экрана не оставались скрытыми при быстром скролле.
    /// </summary>
    /// <param name="elements">Коллекция элементов для анимации.</param>
    /// <param name="duration">Длительность анимации каждого элемента в миллисекундах (по умолчанию 250 мс).</param>
    /// <param name="delayMs">Задержка между стартом анимации соседних элементов (по умолчанию 35 мс).</param>
    /// <param name="maxAnimatedCount">Максимум анимируемых карточек в поле видимости экрана (по умолчанию 8).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public static async Task AnimateStaggeredEntranceAsync(
        IEnumerable<VisualElement?>? elements,
        uint duration = 250,
        uint delayMs = 35,
        int maxAnimatedCount = 8,
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

        // 1. Для элементов дальше границы первого экрана сразу гарантируем видимость,
        // чтобы при быстром скролле вниз пользователь не видел пустые серые пробелы.
        for (int i = maxAnimatedCount; i < list.Count; i++)
        {
            list[i].Opacity = 1.0;
            list[i].TranslationY = 0;
        }

        // 2. Для первых элементов (в пределах экрана) задаем стартовую позицию для плавного выезда
        int animCount = Math.Min(list.Count, maxAnimatedCount);
        for (int i = 0; i < animCount; i++)
        {
            list[i].Opacity = 0;
            list[i].TranslationY = 16;
        }

        // 3. Последовательный быстрый каскадный запуск анимаций
        for (int i = 0; i < animCount; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // При отмене мгновенно возвращаем нормальную видимость оставшимся карточкам
                for (int j = i; j < animCount; j++)
                {
                    list[j].Opacity = 1.0;
                    list[j].TranslationY = 0;
                }
                break;
            }

            var element = list[i];
            _ = Task.WhenAll(
                element.FadeToAsync(1.0, duration, Easing.CubicOut),
                element.TranslateToAsync(0, 0, duration, Easing.CubicOut));

            if (delayMs > 0 && i < animCount - 1)
            {
                try
                {
                    await Task.Delay((int)delayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    for (int j = i + 1; j < animCount; j++)
                    {
                        list[j].Opacity = 1.0;
                        list[j].TranslationY = 0;
                    }
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
