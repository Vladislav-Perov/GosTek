/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис предварительной обработки изображений для OCR (оттенки серого, бинаризация Оцу, автокоррекция наклона)
 * СОДЕРЖИМОЕ: IImagePreprocessor, ImagePreprocessor
 * ЗАВИСИМОСТИ: SkiaSharp
 * ==============================================================================
 */
using System.Diagnostics;
using SkiaSharp;

namespace GosTek.Services;

/// <summary>
/// Интерфейс предварительной обработки изображений для повышения точности OCR.
/// </summary>
public interface IImagePreprocessor
{
    Task<byte[]> ToGrayscale(byte[] image);
    Task<byte[]> ToGrayscaleAsync(byte[] image);
    Task<byte[]> Binarize(byte[] image);
    Task<byte[]> BinarizeAsync(byte[] image, byte threshold = 0);
    Task<byte[]> Deskew(byte[] image);
    Task<byte[]> DeskewAsync(byte[] image);
    Task<byte[]> PreprocessForOcrAsync(byte[] image);
}

/// <summary>
/// Сервис обработки изображений на базе SkiaSharp.
/// Выполняет перевод в оттенки серого, бинаризацию по алгоритму Оцу и устранение наклона листа (Deskew).
/// </summary>
public class ImagePreprocessor : IImagePreprocessor
{
    public static IImagePreprocessor Instance { get; set; } = new ImagePreprocessor();

    public Task<byte[]> ToGrayscale(byte[] image) => ToGrayscaleAsync(image);

    public async Task<byte[]> ToGrayscaleAsync(byte[] image)
    {
        if (image == null || image.Length == 0)
            throw new ArgumentException("Изображение не может быть пустым.", nameof(image));

        return await Task.Run(() => {
            using var original = SKBitmap.Decode(image);
            if (original == null)
                throw new InvalidOperationException("Не удалось декодировать изображение.");

            var info = new SKImageInfo(original.Width, original.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var grayBitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(grayBitmap);
            using var paint = new SKPaint();

            // Матрица перевода в оттенки серого (Rec. 601 Luma: 0.299 R + 0.587 G + 0.114 B)
            float[] matrix = {
                0.299f, 0.587f, 0.114f, 0, 0,
                0.299f, 0.587f, 0.114f, 0, 0,
                0.299f, 0.587f, 0.114f, 0, 0,
                0,      0,      0,      1, 0
            };
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
            canvas.DrawBitmap(original, 0, 0, SKSamplingOptions.Default, paint);

            using var skImage = SKImage.FromBitmap(grayBitmap);
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        });
    }

    public Task<byte[]> Binarize(byte[] image) => BinarizeAsync(image);

    public async Task<byte[]> BinarizeAsync(byte[] image, byte threshold = 0)
    {
        if (image == null || image.Length == 0)
            throw new ArgumentException("Изображение не может быть пустым.", nameof(image));

        return await Task.Run(() => {
            using var original = SKBitmap.Decode(image);
            if (original == null)
                throw new InvalidOperationException("Не удалось декодировать изображение.");

            int width = original.Width;
            int height = original.Height;
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var binarized = new SKBitmap(info);

            // 1. Построение гистограммы яркости
            int[] hist = new int[256];
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var color = original.GetPixel(x, y);
                    byte gray = (byte)(0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue);
                    hist[gray]++;
                }
            }

            // Вычисление оптимального порога Оцу при threshold == 0
            byte t = threshold > 0 ? threshold : CalculateOtsuThreshold(hist, width * height);

            // 2. Бинаризация: пиксели темнее порога становятся черными (текст), светлее — белыми (фон)
            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    var color = original.GetPixel(x, y);
                    byte gray = (byte)(0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue);
                    var bwColor = gray >= t ? SKColors.White : SKColors.Black;
                    binarized.SetPixel(x, y, bwColor);
                }
            }

            using var skImage = SKImage.FromBitmap(binarized);
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        });
    }

    public Task<byte[]> Deskew(byte[] image) => DeskewAsync(image);

    public async Task<byte[]> DeskewAsync(byte[] image)
    {
        if (image == null || image.Length == 0)
            throw new ArgumentException("Изображение не может быть пустым.", nameof(image));

        return await Task.Run(() => {
            using var original = SKBitmap.Decode(image);
            if (original == null)
                throw new InvalidOperationException("Не удалось декодировать изображение.");

            double angle = DetectSkewAngle(original);
            if (Math.Abs(angle) < 0.25)
                return image; // Наклон незначителен

            using var rotated = RotateBitmap(original, (float)-angle);
            using var skImage = SKImage.FromBitmap(rotated);
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        });
    }

    public async Task<byte[]> PreprocessForOcrAsync(byte[] image)
    {
        if (image == null || image.Length == 0)
            return Array.Empty<byte>();

        try {
            // Комплексный конвейер улучшения для OCR: выравнивание наклона -> градация серого -> бинаризация
            var deskewed = await DeskewAsync(image);
            var grayscaled = await ToGrayscaleAsync(deskewed);
            var binarized = await BinarizeAsync(grayscaled);
            return binarized;
        } catch (Exception ex) {
            Debug.WriteLine($"[ImagePreprocessor] Ошибка предобработки: {ex.Message}. Используется оригинал.");
            return image;
        }
    }

    public static byte CalculateOtsuThreshold(int[] hist, int totalPixels)
    {
        if (totalPixels <= 0)
            return 128;

        float sum = 0;
        for (int i = 0; i < 256; i++)
            sum += i * hist[i];

        float sumB = 0;
        int wB = 0;
        float maxVariance = 0;
        byte threshold = 128;

        for (int i = 0; i < 256; i++) {
            wB += hist[i];
            if (wB == 0)
                continue;
            int wF = totalPixels - wB;
            if (wF == 0)
                break;

            sumB += i * hist[i];
            float mB = sumB / wB;
            float mF = (sum - sumB) / wF;

            float betweenVariance = (float)wB * (float)wF * (mB - mF) * (mB - mF);
            if (betweenVariance > maxVariance) {
                maxVariance = betweenVariance;
                threshold = (byte)i;
            }
        }

        return threshold;
    }

    public static double DetectSkewAngle(SKBitmap bitmap)
    {
        int maxDim = 300;
        float scale = Math.Min(1.0f, (float)maxDim / Math.Max(bitmap.Width, bitmap.Height));
        int w = Math.Max(10, (int)(bitmap.Width * scale));
        int h = Math.Max(10, (int)(bitmap.Height * scale));

        using var thumb = bitmap.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), SKSamplingOptions.Default);
        if (thumb == null)
            return 0.0;

        double bestAngle = 0.0;
        double maxVariance = -1.0;

        for (double angle = -8.0; angle <= 8.0; angle += 1.0) {
            double variance = CalculateProjectionVariance(thumb, angle);
            if (variance > maxVariance) {
                maxVariance = variance;
                bestAngle = angle;
            }
        }

        return bestAngle;
    }

    private static double CalculateProjectionVariance(SKBitmap bitmap, double angleDegrees)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        double rad = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        double centerX = width / 2.0;
        double centerY = height / 2.0;

        int[] profile = new int[height];

        for (int y = 0; y < height; y += 2) {
            for (int x = 0; x < width; x += 2) {
                double srcX = (x - centerX) * cos - (y - centerY) * sin + centerX;
                double srcY = (x - centerX) * sin + (y - centerY) * cos + centerY;

                int px = (int)srcX;
                int py = (int)srcY;

                if (px >= 0 && px < width && py >= 0 && py < height) {
                    var color = bitmap.GetPixel(px, py);
                    byte gray = (byte)(0.299f * color.Red + 0.587f * color.Green + 0.114f * color.Blue);
                    if (gray < 128)
                        profile[y]++;
                }
            }
        }

        // Дисперсия профиля проекции
        double mean = profile.Average();
        double variance = profile.Select(v => (v - mean) * (v - mean)).Average();
        return variance;
    }

    private static SKBitmap RotateBitmap(SKBitmap original, float angleDegrees)
    {
        var rotated = new SKBitmap(original.Width, original.Height);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.White);
        canvas.Translate(original.Width / 2f, original.Height / 2f);
        canvas.RotateDegrees(angleDegrees);
        canvas.Translate(-original.Width / 2f, -original.Height / 2f);
        canvas.DrawBitmap(original, 0, 0, SKSamplingOptions.Default, null);
        return rotated;
    }
}
