using GosTek.Models;
using GosTek.Views;

namespace GosTek.Services;

public static class ExportService
{
    public static string DefaultFileName(TemplateDef template)
    {
        var name = template.Title.Replace(' ', '_');
        return SettingsService.Name == NameMode.TemplateDate ? $"{name}_{DateTime.Today:yyyy-MM-dd}" : name;
    }

    public static string SafeFileName(string? input, string fallback)
    {
        var raw = (input ?? "").Trim();
        var name = Path.GetFileName(raw);
        while (name.Contains(".."))
            name = name.Replace("..", "");
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];
        name = name.Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    public static Task<SaveResult?> SaveDocxAsync(TemplateDef template, IReadOnlyDictionary<string, string> values, string name)
        => SaveAsync(name + ".docx", (fileName, overwrite) => FileSaveService.Save(template, values, fileName, overwrite));

    public static Task<SaveResult?> SaveCopyAsync(string sourcePath, string name)
        => SaveAsync(name + ".docx", (fileName, overwrite) => FileSaveService.SaveCopy(sourcePath, fileName, overwrite));

    private static async Task<SaveResult?> SaveAsync(string fileName, Func<string, bool, SaveResult> save)
    {
        bool overwrite = false;
        if (FileSaveService.Exists(fileName)) {
            var mode = SettingsService.Conflict;
            if (mode == ConflictMode.Ask) {
                var answer = await Dialogs.ChoiceAsync(
                    $"Файл «{fileName}» уже есть", "Что сделать с новым документом?", DialogKind.Question,
                    new DialogButton("Добавить номер", DialogButtonStyle.Primary),
                    new DialogButton("Заменить", DialogButtonStyle.DangerOutline),
                    new DialogButton("Отмена"));
                if (answer == 0) mode = ConflictMode.Number;
                else if (answer == 1) mode = ConflictMode.Overwrite;
                else return null;
            }
            if (mode == ConflictMode.Overwrite) overwrite = true;
            else fileName = FileSaveService.FreeName(fileName);
        }
        return save(fileName, overwrite);
    }

    public static Task ShareFileAsync(string path)
        => Share.Default.RequestAsync(new ShareFileRequest { Title = "Документ", File = new ShareFile(path) });

    public static Task OpenFileAsync(string path)
        => Launcher.Default.OpenAsync(new OpenFileRequest("Документ", new ReadOnlyFile(path)));

    public static async Task AfterSaveAsync(SaveResult result)
    {
        var action = SettingsService.AfterSave;
        if (action == AfterSaveAction.Ask) {
            var choice = await Dialogs.ChoiceAsync(
                "Документ сохранён", result.Location, DialogKind.Success,
                new DialogButton("Открыть", DialogButtonStyle.Primary),
                new DialogButton("Поделиться"),
                new DialogButton("Закрыть"));
            action = choice switch { 0 => AfterSaveAction.Open, 1 => AfterSaveAction.Share, _ => AfterSaveAction.Nothing };
            if (action == AfterSaveAction.Nothing) return;
        }
        switch (action) {
            case AfterSaveAction.Open: await OpenFileAsync(result.FilePath); break;
            case AfterSaveAction.Share: await ShareFileAsync(result.FilePath); break;
            default: await Dialogs.AlertAsync("Сохранено", result.Location, DialogKind.Success); break;
        }
    }
}
