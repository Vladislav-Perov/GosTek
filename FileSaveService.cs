using GosTek.Models;

#if ANDROID
using Android.Content;
using Android.Provider;
#endif

namespace GosTek;

/// <summary>
/// Результат сохранения.
/// Location — что показать пользователю («Документы/GosTek/Файл.docx» или путь на ПК).
/// FilePath — файл, который можно открыть или отправить (на Android это копия в кэше).
/// </summary>
public sealed record SaveResult(string Location, string FilePath);

/// <summary>
/// Куда и как сохраняется готовый документ.
/// ПК: выбранная в настройках папка (по умолчанию Документы\GosTek).
/// Android: публичная папка Документы/GosTek через MediaStore (Android 10+, разрешения не нужны).
/// </summary>
public static class FileSaveService
{
    private const string AndroidFolder = "Документы/GosTek";

#if ANDROID
    private const string RelativePath = "Documents/GosTek/";
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
#endif

    /// <summary>Где окажется файл: для строки в настройках и сообщений.</summary>
    public static string LocationHint =>
#if ANDROID
        AndroidFolder;
#else
        SettingsService.SaveFolder;
#endif

    /// <summary>Есть ли уже файл с таким именем (с расширением) в папке сохранения.</summary>
    public static bool Exists(string fileName)
    {
#if ANDROID
        return FindAndroid(fileName) is not null;
#else
        return File.Exists(Path.Combine(ResolveFolder(), fileName));
#endif
    }

    /// <summary>Свободное имя: «Файл.docx» → «Файл (1).docx», «Файл (2).docx» и так далее.</summary>
    public static string FreeName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);

        for (int i = 1; i < 1000; i++) {
            var candidate = $"{stem} ({i}){ext}";
            if (!Exists(candidate))
                return candidate;
        }

        return $"{stem}_{DateTime.Now:HHmmss}{ext}";
    }

    /// <summary>Собирает .docx и сохраняет. overwrite = true: заменить существующий файл.</summary>
    public static SaveResult Save(TemplateDef template, IReadOnlyDictionary<string, string> values,
                                  string fileName, bool overwrite)
    {
#if ANDROID
        return SaveAndroid(template, values, fileName, overwrite);
#else
        var path = Path.Combine(ResolveFolder(), fileName);
        DocxExporter.Save(template, values, path);
        return new SaveResult(path, path);
#endif
    }

    /// <summary>Удаляет временные .docx из кэша приложения. Возвращает, сколько удалено.</summary>
    public static int ClearTemp()
    {
        int count = 0;

        try {
            foreach (var file in Directory.EnumerateFiles(FileSystem.CacheDirectory, "*.docx")) {
                try {
                    File.Delete(file);
                    count++;
                } catch {
                    // Файл занят (например, открыт в другой программе), пропускаем
                }
            }
        } catch {
            // Кэш недоступен: удалять нечего
        }

        return count;
    }

#if !ANDROID
    // Папка из настроек; если создать её нельзя (диск отключён, нет прав), берём папку по умолчанию
    private static string ResolveFolder()
    {
        var folder = SettingsService.SaveFolder;

        try {
            Directory.CreateDirectory(folder);
            return folder;
        } catch {
            var fallback = SettingsService.DefaultFolder;
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
#endif

#if ANDROID
    // Ищем наш файл в Документы/GosTek. Без разрешений Android показывает приложению только его собственные файлы
    private static Android.Net.Uri? FindAndroid(string fileName)
    {
        var resolver = Platform.AppContext.ContentResolver;
        var collection = MediaStore.Files.GetContentUri("external");
        if (resolver is null || collection is null)
            return null;

        using var cursor = resolver.Query(
            collection,
            new[] { "_id" },
            "relative_path=? AND _display_name=?",
            new[] { RelativePath, fileName },
            null);

        if (cursor is not null && cursor.MoveToFirst())
            return ContentUris.WithAppendedId(collection, cursor.GetLong(0));

        return null;
    }

    private static SaveResult SaveAndroid(TemplateDef template, IReadOnlyDictionary<string, string> values,
                                          string fileName, bool overwrite)
    {
        // Копия в кэше: из неё потом открываем и отправляем файл
        var temp = Path.Combine(FileSystem.CacheDirectory, fileName);
        DocxExporter.Save(template, values, temp);

        var resolver = Platform.AppContext.ContentResolver
            ?? throw new InvalidOperationException("Хранилище устройства недоступно.");
        var collection = MediaStore.Files.GetContentUri("external")
            ?? throw new InvalidOperationException("Хранилище устройства недоступно.");

        var target = overwrite ? FindAndroid(fileName) : null;
        bool created = false;

        if (target is null) {
            var info = new ContentValues();
            info.Put("_display_name", fileName);
            info.Put("mime_type", DocxMime);
            info.Put("relative_path", RelativePath);
            info.Put("is_pending", 1);   // пока пишем, файл скрыт от других приложений

            target = resolver.Insert(collection, info)
                ?? throw new InvalidOperationException("Не удалось создать файл в папке «Документы».");
            created = true;
        }

        try {
            // "wt": записать с нуля (при замене старое содержимое не остаётся)
            using var output = resolver.OpenOutputStream(target, "wt")
                ?? throw new InvalidOperationException("Не удалось открыть файл для записи.");
            using var input = File.OpenRead(temp);
            input.CopyTo(output);
        } catch {
            if (created)
                resolver.Delete(target, null, null);   // не оставляем пустой файл
            throw;
        }

        if (created) {
            var done = new ContentValues();
            done.Put("is_pending", 0);
            resolver.Update(target, done, null, null);
        }

        return new SaveResult($"{AndroidFolder}/{fileName}", temp);
    }
#endif
}
