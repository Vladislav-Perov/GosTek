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
using TesseractOcrMaui.Results;

namespace GosTek.Services;

public static partial class OcrService
{
    // Модель Tesseract распаковывается из пакета приложения один раз и дальше лежит
    // в appdata — защищаем от параллельного повторного вызова.
    private static readonly SemaphoreSlim LoadLock = new(1, 1);
    private static bool _loaded;

    public static partial async Task<string> RecognizeTextAsync(Stream photoStream)
    {
        var tesseract = IPlatformApplication.Current?.Services.GetService<ITesseract>()
            ?? throw new InvalidOperationException(
                "Сервис распознавания текста не зарегистрирован (AddTesseractOcr в MauiProgram.cs).");

        await EnsureLoadedAsync(tesseract);

        using var memory = new MemoryStream();
        await photoStream.CopyToAsync(memory);

        byte[] bytes = memory.ToArray();

        // Предварительная обработка изображения (выравнивание наклона, оттенки серого, бинаризация)
        bytes = await ImagePreprocessor.Instance.PreprocessForOcrAsync(bytes);

        RecognizionResult result = await tesseract.RecognizeTextAsync(bytes);

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
