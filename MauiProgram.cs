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
                .ConfigureFonts(fonts => {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddLogging();

            // OCR на Android (см. Services/OcrService.cs). Файл должен лежать
            // в Resources/Raw с тем же именем — rus.traineddata уже там.
            builder.Services.AddTesseractOcr(files => {
                files.AddFile("rus.traineddata");
            });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
