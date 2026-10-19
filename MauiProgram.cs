using Microsoft.Extensions.Logging;

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
                    fonts.AddFont("PTSans-Regular.ttf", "PTSansRegular");
                    fonts.AddFont("PTSans-Bold.ttf", "PTSansBold");
                });

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
