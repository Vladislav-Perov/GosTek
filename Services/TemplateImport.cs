using DocumentFormat.OpenXml.Packaging;

namespace GosTek.Services;

public static class TemplateImport
{
    public static async Task<string> SaveAsync(string catalogPath, string fileName, Stream source)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Для шаблона нужен файл DOCX.");

        var folder = Path.Combine(catalogPath, "Мои шаблоны");
        Directory.CreateDirectory(folder);
        var name = ExportService.SafeFileName(fileName, "Шаблон");
        var temporary = Path.Combine(folder, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await source.CopyToAsync(output).ConfigureAwait(false);
            }

            return await Task.Run(() =>
            {
                using (var doc = WordprocessingDocument.Open(temporary, false))
                {
                    if (doc.MainDocumentPart?.Document?.Body is null)
                        throw new InvalidDataException("В файле отсутствует документ Word.");
                }

                for (var number = 1; ; number++)
                {
                    var stem = number == 1 ? name : $"{name} ({number})";
                    var target = Path.Combine(folder, $"Мои шаблоны_{stem}.docx");
                    try
                    {
                        File.Move(temporary, target);
                        return target;
                    }
                    catch (IOException) when (File.Exists(target))
                    {
                        // Keep the existing template and try the next filename.
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
