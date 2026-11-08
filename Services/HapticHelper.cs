/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Безопасный кроссплатформенный помощник тактильного виброотклика (Haptic Feedback)
 * СОДЕРЖИМОЕ: HapticHelper
 * ЗАВИСИМОСТИ: Microsoft.Maui.Devices
 * ==============================================================================
 */
namespace GosTek.Services;

/// <summary>
/// Безопасный вызов виброотклика на поддерживаемых устройствах (Android).
/// На Windows и неподдерживаемых платформах методы выполняются как no-op без исключений.
/// </summary>
public static class HapticHelper
{
    /// <summary>Включение/отключение тактильного отклика (для тестирования или настроек).</summary>
    public static bool IsEnabled { get; set; } = true;

    /// <summary>Лёгкий тактильный клик при нажатии кнопок, переключении вкладок и чипов.</summary>
    public static void Click()
    {
        if (!IsEnabled)
            return;

        try
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
                HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // Подавление ошибок на устройствах без вибромотора
        }
    }

    /// <summary>Уверенная тактильная отдача при успешном завершении действия (сохранение, экспорт).</summary>
    public static void Success()
    {
        if (!IsEnabled)
            return;

        try
        {
            if (DeviceInfo.Platform == DevicePlatform.Android)
                HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch
        {
            // Подавление ошибок
        }
    }
}
