using System.Diagnostics;
using GosTek.Models;
using GosTek.Views;

#if ANDROID
using Android.Content;
#endif

namespace GosTek.Services;

/// <summary>
/// Экспорт в PDF без сторонних библиотек: готовый .docx открывается во внешней программе,
/// а PDF пользователь сохраняет там сам.
///
/// Windows: файл открывается в Word (программа по умолчанию для .docx), дальше «Файл → Сохранить как → PDF».
/// Android: файл открывается через FileProvider и Intent.ActionView в WPS, Google Документах и т. п.
/// </summary>
public static class PdfExportService
{
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>
    /// Собирает .docx во временную папку (кэш приложения) и открывает его для сохранения в PDF.
    /// Возвращает true, если документ удалось открыть.
    /// </summary>
    public static async Task<bool> ExportAsync(TemplateDef template, IReadOnlyDictionary<string, string> values, string name)
    {
        var fileName = ExportService.SafeFileName(name, ExportService.DefaultFileName(template)) + ".docx";
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);

        await Task.Run(() => TemplateEngine.FillTo(template, values, path));
        return await OpenForPdfAsync(path);
    }

    /// <summary>Открывает готовый .docx во внешней программе. Сначала показывает подсказку, что делать дальше.</summary>
    public static async Task<bool> OpenForPdfAsync(string docxPath)
    {
        await Dialogs.AlertAsync("Сохранение в PDF", Hint);

        try {
            Open(docxPath);
            return true;
        } catch (Exception ex) {
            Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось открыть документ", NoAppMessage, DialogKind.Danger);
            return false;
        }
    }

    private static string Hint =>
#if ANDROID
        "Откроется программа для документов (WPS, Google Документы или другая). Сохраните файл как PDF через её меню: «Экспорт», «Сохранить как PDF» или «Печать».";
#else
        "Откроется Word. Сохраните файл как PDF через Файл → Сохранить как.";
#endif

    private static string NoAppMessage =>
#if ANDROID
        "На устройстве нет программы для файлов Word. Установите WPS Office или Google Документы и повторите.";
#else
        "Не нашли программу для файлов .docx. Установите Word или другой редактор и повторите.";
#endif

    private static void Open(string path)
    {
#if WINDOWS
        // UseShellExecute: Windows сам выбирает программу для .docx (обычно Word)
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
#elif ANDROID
        var context = Platform.AppContext;
        var file = new Java.IO.File(path);

        // Провайдер файлов уже объявлен в MAUI Essentials: «{ApplicationId}.fileProvider», кэш приложения в его путях есть
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, context.PackageName + ".fileProvider", file)
            ?? throw new InvalidOperationException("Не удалось выдать доступ к файлу.");

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, DocxMime);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);

        var chooser = Intent.CreateChooser(intent, "Открыть в…")
            ?? throw new InvalidOperationException("Не удалось открыть выбор программы.");
        chooser.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(chooser);
#else
        throw new PlatformNotSupportedException("Экспорт в PDF доступен на Windows и Android.");
#endif
    }
}
