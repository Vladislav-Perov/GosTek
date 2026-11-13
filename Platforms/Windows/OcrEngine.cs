/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Платформенная реализация оптического распознавания (OCR) на Windows через WinRT Windows.Media.Ocr
 * СОДЕРЖИМОЕ: OcrEngine
 * ЗАВИСИМОСТИ: Windows.Media.Ocr, Windows.Graphics.Imaging, SkiaSharp, GosTek.Services.OcrService
 * ==============================================================================
 */
using SkiaSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GosTek.Services;

public static partial class OcrService
{
    public static partial async Task<string> RecognizeTextAsync(Stream photoStream, CancellationToken cancellationToken)
    {
        // Для паспортов используем двухзонное распознавание:
        // Верхняя часть (визуальная зона) — русский язык (ru-RU).
        // Нижняя часть (MRZ) — английский язык (en-US).
        var ruEngine = OcrEngine.TryCreateFromLanguage(new Language("ru-RU"))
                     ?? OcrEngine.TryCreateFromLanguage(new Language("ru"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();

        var enEngine = OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                     ?? OcrEngine.TryCreateFromLanguage(new Language("en"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();

        if (ruEngine is null && enEngine is null)
        {
            throw new InvalidOperationException(
                "В Windows не установлен языковой пакет для распознавания текста. " +
                "Параметры → Время и язык → Язык → Русский/Английский → Параметры → «Распознавание текста».");
        }

        using var memory = new MemoryStream();
        cancellationToken.ThrowIfCancellationRequested();
        await photoStream.CopyToAsync(memory, cancellationToken);
        byte[] bytes = memory.ToArray();

        // Предварительная обработка изображения (выравнивание наклона, оттенки серого, бинаризация)
        bytes = await ImagePreprocessor.Instance.PreprocessForOcrAsync(bytes);

        cancellationToken.ThrowIfCancellationRequested();

        using var original = SKBitmap.Decode(bytes);
        if (original is null)
            return string.Empty;

        // Относительное разделение: верхняя зона 0..75% высоты (визуальная), нижняя 75..100% (MRZ)
        int splitY = Math.Max(1, (int)(original.Height * 0.75));
        var topRect = new SKRectI(0, 0, original.Width, splitY);
        var bottomRect = new SKRectI(0, splitY, original.Width, original.Height);

        using var topBitmap = new SKBitmap(topRect.Width, topRect.Height);
        original.ExtractSubset(topBitmap, topRect);

        using var bottomBitmap = new SKBitmap(bottomRect.Width, bottomRect.Height);
        original.ExtractSubset(bottomBitmap, bottomRect);

        string topText = string.Empty;
        if (ruEngine is not null && topBitmap.Width > 0 && topBitmap.Height > 0)
        {
            using var topSoftwareBitmap = await ToSoftwareBitmapAsync(topBitmap);
            var topResult = await ruEngine.RecognizeAsync(topSoftwareBitmap);
            topText = topResult.Text ?? string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        string bottomText = string.Empty;
        var mrzEngine = enEngine ?? ruEngine;
        if (mrzEngine is not null && bottomBitmap.Width > 0 && bottomBitmap.Height > 0)
        {
            using var bottomSoftwareBitmap = await ToSoftwareBitmapAsync(bottomBitmap);
            var bottomResult = await mrzEngine.RecognizeAsync(bottomSoftwareBitmap);
            bottomText = bottomResult.Text ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(topText))
            return bottomText;
        if (string.IsNullOrWhiteSpace(bottomText))
            return topText;

        return $"{topText}\n{bottomText}";
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();

        var randomAccessStream = new InMemoryRandomAccessStream();
        using (var output = randomAccessStream.GetOutputStreamAt(0))
        {
            var writer = new DataWriter(output);
            byte[] buffer = new byte[stream.Length];
            stream.Read(buffer, 0, buffer.Length);
            writer.WriteBytes(buffer);
            await writer.StoreAsync();
            await output.FlushAsync();
        }
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        var decoded = await decoder.GetSoftwareBitmapAsync();

        if (decoded.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            decoded.BitmapAlphaMode == BitmapAlphaMode.Straight)
        {
            var converted = SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            decoded.Dispose();
            return converted;
        }

        return decoded;
    }
}
