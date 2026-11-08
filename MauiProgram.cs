/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Точка инициализации приложения MAUI, регистрация сервисов, шрифтов, Tesseract OCR и CommunityToolkit
 * СОДЕРЖИМОЕ: MauiProgram
 * ЗАВИСИМОСТИ: Microsoft.Maui, CommunityToolkit.Maui.Camera, TesseractOcrMaui, GosTek.Services
 * ==============================================================================
 */
using CommunityToolkit.Maui;
using GosTek.Services;
using Microsoft.Extensions.Logging;
using TesseractOcrMaui;

namespace GosTek
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkitCamera()
                .ConfigureFonts(fonts => {
                    fonts.AddFont("PTSans-Regular.ttf", "PTSansRegular");
                    fonts.AddFont("PTSans-Bold.ttf", "PTSansBold");
                });

            builder.Services.AddLogging();
            builder.Services.AddSingleton<ICustomRulesService, CustomRulesService>();
            builder.Services.AddSingleton<IImagePreprocessor, ImagePreprocessor>();
            builder.Services.AddSingleton<IDocumentClassifier, DocumentClassifier>();

            // OCR на Android (см. Services/OcrService.cs). Файл должен лежать
            // в Resources/Raw с тем же именем — rus.traineddata уже там.
            builder.Services.AddTesseractOcr(files => {
                files.AddFile("rus.traineddata");
            });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            CustomRulesService.Instance = app.Services.GetRequiredService<ICustomRulesService>();
            ImagePreprocessor.Instance = app.Services.GetRequiredService<IImagePreprocessor>();
            DocumentClassifier.Instance = app.Services.GetRequiredService<IDocumentClassifier>();

            return app;
        }
    }
}
