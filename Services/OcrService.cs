namespace GosTek.Services;

/// <summary>
/// Распознавание текста с фото (OCR). Общий метод объявлен здесь (partial, без тела),
/// реализация — разная под каждую платформу, в Platforms/Windows/OcrEngine.cs
/// и Platforms/Android/OcrEngine.cs (MAUI сам подключает нужный файл по TFM).
///
/// - Windows: Windows.Media.Ocr, встроен в ОС, NuGet-пакет не нужен. Требует, чтобы
///   в Windows был установлен языковой пакет «Русский» с распознаванием текста
///   (Параметры → Время и язык → Язык → Русский → Параметры → «Распознавание текста»).
///
/// - Android: пакет TesseractOcrMaui (нативный Tesseract) + модель rus.traineddata
///   (Resources/Raw). Google ML Kit Text Recognition v2 в режиме «на устройстве»
///   НЕ поддерживает русский/кириллицу (проверено по официальной таблице языков:
///   https://developers.google.com/ml-kit/vision/text-recognition/v2/languages —
///   русского нет ни в Supported, ни в Experimental, ни в Mapped). Облачный режим
///   ML Kit кириллицу поддерживает, но требует интернет, что противоречит
///   приватности проекта (документы не должны уходить с устройства). Поэтому на
///   Android используется Tesseract — тоже полностью локально, без сети.
/// </summary>
public static partial class OcrService
{
    /// <summary>
    /// Распознать текст на фотографии (поток содержимого файла изображения, как есть).
    /// Возвращает распознанный текст (может быть пустым, если ничего не нашли).
    /// Бросает исключение с понятным сообщением, если распознавание не удалось технически
    /// (например, на Windows не установлен языковой пакет).
    /// </summary>
    public static partial Task<string> RecognizeTextAsync(Stream photoStream);
}
