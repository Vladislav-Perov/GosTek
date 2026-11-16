/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Платформенная реализация оптического распознавания (OCR) на Android через TesseractOcrMaui
 * СОДЕРЖИМОЕ: OcrEngine
 * ЗАВИСИМОСТИ: TesseractOcrMaui, GosTek.Services.OcrService
 * ==============================================================================
 */
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using TesseractOcrMaui;
using TesseractOcrMaui.Enums;
using TesseractOcrMaui.Results;

namespace GosTek.Services;

public static partial class OcrService
{
    // Модель Tesseract распаковывается из пакета приложения один раз и дальше лежит
    // в appdata — защищаем от параллельного повторного вызова.
    private static readonly SemaphoreSlim LoadLock = new(1, 1);
    private static bool _loaded;

    public static partial async Task<string> RecognizeTextAsync(Stream photoStream, CancellationToken cancellationToken)
    {
        var tesseract = IPlatformApplication.Current?.Services.GetService<ITesseract>()
            ?? throw new InvalidOperationException(
                "Сервис распознавания текста не зарегистрирован (AddTesseractOcr в MauiProgram.cs).");

        await EnsureLoadedAsync(tesseract);

        using var memory = new MemoryStream();
        cancellationToken.ThrowIfCancellationRequested();
        await photoStream.CopyToAsync(memory, cancellationToken);

        byte[] rawBytes = memory.ToArray();

        // Предобработка: контрастирование и масштабирование для качественного OCR
        byte[] preprocessedBytes = await ImagePreprocessor.Instance.PreprocessForOcrAsync(rawBytes);

        cancellationToken.ThrowIfCancellationRequested();

        // КРИТИЧЕСКИ ВАЖНО: сохраняем во временный файл на диске, так как в Android C-библиотеке
        // Leptonica fopenReadFromMemory падает с InvalidImage, а файловый ввод pixReadFromFile работает идеально!
        string tempDir = FileSystem.CacheDirectory;
        string tempFile1 = Path.Combine(tempDir, $"ocr_opt_{Guid.NewGuid():N}.png");
        string tempFile2 = Path.Combine(tempDir, $"ocr_raw_{Guid.NewGuid():N}.png");

        try
        {
            await File.WriteAllBytesAsync(tempFile1, preprocessedBytes, cancellationToken);
            await File.WriteAllBytesAsync(tempFile2, rawBytes, cancellationToken);

            // Конфигурация Tesseract: режим Auto (PSM 3) с распознаванием пробелов
            tesseract.EngineConfiguration = engine => {
                engine.DefaultSegmentationMode = PageSegmentationMode.Auto;
                engine.SetVariable("tessedit_pageseg_mode", "3");
                engine.SetVariable("preserve_interword_spaces", "1");
            };

            // 1. Распознавание через файловый дескриптор оптимизированного кадра
            RecognizionResult result = await tesseract.RecognizeTextAsync(tempFile1);
            cancellationToken.ThrowIfCancellationRequested();

            // 2. Если результат пустой или мал — пробуем оригинальный файл
            if (result.NotSuccess() || string.IsNullOrWhiteSpace(result.RecognisedText) || result.RecognisedText.Trim().Length < 5)
            {
                var rawRetry = await tesseract.RecognizeTextAsync(tempFile2);
                if (!rawRetry.NotSuccess() && !string.IsNullOrWhiteSpace(rawRetry.RecognisedText))
                    result = rawRetry;
            }

            // 3. Fallback: режим SingleBlock (PSM 6) для плотного документа
            if (result.NotSuccess() || string.IsNullOrWhiteSpace(result.RecognisedText) || result.RecognisedText.Trim().Length < 5)
            {
                tesseract.EngineConfiguration = engine => {
                    engine.DefaultSegmentationMode = PageSegmentationMode.SingleBlock;
                    engine.SetVariable("tessedit_pageseg_mode", "6");
                    engine.SetVariable("preserve_interword_spaces", "1");
                };
                var blockRetry = await tesseract.RecognizeTextAsync(tempFile1);
                if (!blockRetry.NotSuccess() && !string.IsNullOrWhiteSpace(blockRetry.RecognisedText))
                    result = blockRetry;
            }

            if (result.NotSuccess() && string.IsNullOrWhiteSpace(result.RecognisedText))
                throw new InvalidOperationException($"Не удалось распознать текст (статус: {result.Status}).");

            return result.RecognisedText ?? "";
        }
        finally
        {
            try { if (File.Exists(tempFile1)) File.Delete(tempFile1); } catch { }
            try { if (File.Exists(tempFile2)) File.Delete(tempFile2); } catch { }
        }
    }

    private static async Task EnsureLoadedAsync(ITesseract tesseract)
    {
        if (_loaded)
            return;

        await LoadLock.WaitAsync();
        try {
            if (!_loaded) {
                await tesseract.LoadTraineddataAsync();
                _loaded = true;
            }
        } finally {
            LoadLock.Release();
        }
    }
}
