/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для сервиса предобработки изображений ImagePreprocessor
 * СОДЕРЖИМОЕ: ImagePreprocessorTests
 * ЗАВИСИМОСТИ: Xunit, SkiaSharp, GosTek.Services
 * ==============================================================================
 */
using GosTek.Services;
using SkiaSharp;
using Xunit;

namespace GosTek.Tests;

public class ImagePreprocessorTests
{
    private readonly IImagePreprocessor _preprocessor = new ImagePreprocessor();

    [Fact]
    public async Task ToGrayscale_ThrowsOnNullOrEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.ToGrayscale(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.ToGrayscale(Array.Empty<byte>()));
    }

    [Fact]
    public async Task ToGrayscale_ConvertsColorImageToGrayscale()
    {
        byte[] samplePng = CreateSyntheticColorBitmap(20, 20);

        byte[] result = await _preprocessor.ToGrayscale(samplePng);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        using var bitmap = SKBitmap.Decode(result);
        Assert.NotNull(bitmap);
        Assert.Equal(20, bitmap.Width);
        Assert.Equal(20, bitmap.Height);

        // В оттенках серого значения R, G, B равны (с допустимой погрешностью округления 1)
        for (int y = 0; y < bitmap.Height; y++) {
            for (int x = 0; x < bitmap.Width; x++) {
                var color = bitmap.GetPixel(x, y);
                Assert.InRange(Math.Abs(color.Red - color.Green), 0, 1);
                Assert.InRange(Math.Abs(color.Green - color.Blue), 0, 1);
            }
        }
    }

    [Fact]
    public async Task Binarize_ProducesPureBlackAndWhitePixels()
    {
        byte[] samplePng = CreateSyntheticColorBitmap(30, 30);

        byte[] result = await _preprocessor.Binarize(samplePng);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        using var bitmap = SKBitmap.Decode(result);
        Assert.NotNull(bitmap);

        for (int y = 0; y < bitmap.Height; y++) {
            for (int x = 0; x < bitmap.Width; x++) {
                var color = bitmap.GetPixel(x, y);
                bool isBlack = color.Red == 0 && color.Green == 0 && color.Blue == 0;
                bool isWhite = color.Red == 255 && color.Green == 255 && color.Blue == 255;
                Assert.True(isBlack || isWhite, $"Пиксель ({x},{y}) не черно-белый: R={color.Red}, G={color.Green}, B={color.Blue}");
            }
        }
    }

    [Fact]
    public async Task Binarize_ThrowsOnNullOrEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.Binarize(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.Binarize(Array.Empty<byte>()));
    }

    [Fact]
    public void CalculateOtsuThreshold_ComputesOptimumSeparation()
    {
        // Бимодальная гистограмма: пик на темных (50) и светлых (200) значениях
        int[] hist = new int[256];
        hist[40] = 50;
        hist[50] = 100;
        hist[60] = 50;

        hist[190] = 50;
        hist[200] = 100;
        hist[210] = 50;

        int total = 400;
        byte threshold = ImagePreprocessor.CalculateOtsuThreshold(hist, total);

        Assert.InRange(threshold, 50, 190);
    }

    [Fact]
    public async Task Deskew_ThrowsOnNullOrEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.Deskew(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _preprocessor.Deskew(Array.Empty<byte>()));
    }

    [Fact]
    public async Task Deskew_StraightImage_ReturnsValidPng()
    {
        byte[] samplePng = CreateSyntheticDocumentBitmap(60, 60);

        byte[] result = await _preprocessor.Deskew(samplePng);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        using var bitmap = SKBitmap.Decode(result);
        Assert.NotNull(bitmap);
        Assert.Equal(60, bitmap.Width);
        Assert.Equal(60, bitmap.Height);
    }

    [Fact]
    public async Task PreprocessForOcrAsync_PipelineExecution_Success()
    {
        byte[] samplePng = CreateSyntheticDocumentBitmap(40, 40);

        byte[] result = await _preprocessor.PreprocessForOcrAsync(samplePng);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        using var bitmap = SKBitmap.Decode(result);
        Assert.NotNull(bitmap);

        // Результат бинаризован
        var color = bitmap.GetPixel(5, 5);
        bool isPureBw = (color.Red == 0 || color.Red == 255) && color.Red == color.Green && color.Green == color.Blue;
        Assert.True(isPureBw);
    }

    [Fact]
    public async Task PreprocessForOcrAsync_ReturnsEmptyOnNullOrEmptyInput()
    {
        var resultNull = await _preprocessor.PreprocessForOcrAsync(null!);
        Assert.Empty(resultNull);

        var resultEmpty = await _preprocessor.PreprocessForOcrAsync(Array.Empty<byte>());
        Assert.Empty(resultEmpty);
    }

    private static byte[] CreateSyntheticColorBitmap(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) {
            canvas.Clear(SKColors.White);

            using var redPaint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 2f, height, redPaint);

            using var bluePaint = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(width / 2f, 0, width / 2f, height, bluePaint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] CreateSyntheticDocumentBitmap(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap)) {
            canvas.Clear(SKColors.White);

            using var textPaint = new SKPaint { Color = SKColors.Black, StrokeWidth = 2 };
            // Горизонтальные линии, имитирующие строки текста документа
            for (int y = 8; y < height; y += 8) {
                canvas.DrawLine(4, y, width - 4, y, textPaint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
