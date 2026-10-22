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

        RecognizionResult result = await tesseract.RecognizeTextAsync(memory.ToArray());

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
