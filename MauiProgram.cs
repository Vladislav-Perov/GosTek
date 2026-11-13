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
#if WINDOWS
using Microsoft.Maui.LifecycleEvents;
#endif

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

#if WINDOWS
            Microsoft.Maui.Controls.Handlers.ShellHandler.Mapper.AppendToMapping("FixTopLeftCorner", (handler, view) =>
            {
                if (handler.PlatformView is Microsoft.UI.Xaml.FrameworkElement fe)
                {
                    void FixShellVisualTree()
                    {
                        try
                        {
                            var svType = fe.GetType();
                            var rowProp = svType.GetProperty("PaneContentGridToggleButtonRow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (rowProp?.GetValue(fe) is Microsoft.UI.Xaml.Controls.RowDefinition row)
                            {
                                row.Height = new Microsoft.UI.Xaml.GridLength(0);
                                row.MaxHeight = 0;
                                row.MinHeight = 0;
                            }

                            var headerRowProp = svType.GetProperty("PaneHeaderContentBorderRow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (headerRowProp?.GetValue(fe) is Microsoft.UI.Xaml.Controls.RowDefinition headerRow)
                            {
                                headerRow.Height = new Microsoft.UI.Xaml.GridLength(0);
                                headerRow.MaxHeight = 0;
                                headerRow.MinHeight = 0;
                            }

                            var btnGridProp = svType.GetProperty("PaneToggleButtonGrid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (btnGridProp?.GetValue(fe) is Microsoft.UI.Xaml.FrameworkElement btnGrid)
                            {
                                btnGrid.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                                btnGrid.Height = 0;
                                btnGrid.MaxHeight = 0;
                            }

                            var paneContentGridProp = svType.GetProperty("PaneContentGrid", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (paneContentGridProp?.GetValue(fe) is Microsoft.UI.Xaml.Controls.Grid paneContentGrid)
                            {
                                paneContentGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                                paneContentGrid.Margin = new Microsoft.UI.Xaml.Thickness(0);
                                paneContentGrid.Padding = new Microsoft.UI.Xaml.Thickness(0);
                            }

                            var topPaddingProp = svType.GetProperty("ContentPaneTopPadding", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (topPaddingProp?.GetValue(fe) is Microsoft.UI.Xaml.FrameworkElement topPadding)
                            {
                                topPadding.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                                topPadding.Height = 0;
                            }
                        }
                        catch
                        {
                        }
                    }

                    fe.Loaded += (_, _) => FixShellVisualTree();
                    FixShellVisualTree();
                }
            });

            builder.ConfigureLifecycleEvents(events =>
            {
                events.AddWindows(windows => windows.OnWindowCreated(window =>
                {
                    Platforms.Windows.WindowSizing.EnforceMinSize(window);
                    ThemeService.ApplyTitleBarTheme(window);
                    Platforms.Windows.WindowSizing.ConfigureDragRegion(window);
                }));
            });
#endif

            builder.Services.AddLogging();
            builder.Services.AddSingleton<ICustomRulesService, CustomRulesService>();
            builder.Services.AddSingleton<IImagePreprocessor, ImagePreprocessor>();
            builder.Services.AddSingleton<IDocumentClassifier, DocumentClassifier>();

            // OCR на Android (см. Services/OcrService.cs). Файлы должны лежать
            // в Resources/Raw с тем же именем — rus.traineddata и eng.traineddata.
            builder.Services.AddTesseractOcr(files => {
                files.AddFile("rus.traineddata");
                files.AddFile("eng.traineddata");
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
