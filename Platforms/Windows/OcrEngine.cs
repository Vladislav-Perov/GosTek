using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace GosTek.Services;

public static partial class OcrService
{
    public static partial async Task<string> RecognizeTextAsync(Stream photoStream)
    {
        // Сначала пробуем именно русский язык (точнее для кириллицы), затем —
        // любой язык, установленный в профиле пользователя, как запасной вариант.
        var engine = OcrEngine.TryCreateFromLanguage(new Language("ru"))
                     ?? OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null)
            throw new InvalidOperationException(
                "В Windows не установлен языковой пакет для распознавания текста. " +
                "Параметры → Время и язык → Язык → Русский → Параметры → «Распознавание текста».");

        using var memory = new MemoryStream();
        await photoStream.CopyToAsync(memory);
        byte[] bytes = memory.ToArray();

        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var output = randomAccessStream.GetOutputStreamAt(0)) {
            var writer = new DataWriter(output);
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await output.FlushAsync();
        }
        randomAccessStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using var decoded = await decoder.GetSoftwareBitmapAsync();

        // OcrEngine принимает только Bgra8 + Premultiplied (или Ignore) альфа-канал
        SoftwareBitmap bitmap = decoded;
        if (decoded.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
            decoded.BitmapAlphaMode == BitmapAlphaMode.Straight) {
            bitmap = SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        }

        try {
            var result = await engine.RecognizeAsync(bitmap);
            return result.Text;
        } finally {
            if (!ReferenceEquals(bitmap, decoded))
                bitmap.Dispose();
        }
    }
}
