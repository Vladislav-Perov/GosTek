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

        byte[] bytes = memory.ToArray();

        // Предварительная обработка изображения (выравнивание наклона, оттенки серого, бинаризация)
        bytes = await ImagePreprocessor.Instance.PreprocessForOcrAsync(bytes);

        cancellationToken.ThrowIfCancellationRequested();

        // Конфигурация Tesseract: режим PSM 6 (SingleBlock) для четкого распознавания структуры документа
        tesseract.EngineConfiguration = engine => {
            engine.DefaultSegmentationMode = PageSegmentationMode.SingleBlock;
            engine.SetVariable("tessedit_pageseg_mode", "6");
        };
        RecognizionResult result = await tesseract.RecognizeTextAsync(bytes);
        cancellationToken.ThrowIfCancellationRequested();

        // Fallback на режим PSM 3 (Auto), если единый блок текста вернул пустой результат
        if (result.NotSuccess() || string.IsNullOrWhiteSpace(result.RecognisedText)) {
            tesseract.EngineConfiguration = engine => {
                engine.DefaultSegmentationMode = PageSegmentationMode.Auto;
                engine.SetVariable("tessedit_pageseg_mode", "3");
            };
            var retry = await tesseract.RecognizeTextAsync(bytes);
            if (!retry.NotSuccess() && !string.IsNullOrWhiteSpace(retry.RecognisedText))
                result = retry;
        }

        if (result.NotSuccess())
            throw new InvalidOperationException($"Не удалось распознать текст (статус: {result.Status}).");

        return result.RecognisedText ?? "";
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
