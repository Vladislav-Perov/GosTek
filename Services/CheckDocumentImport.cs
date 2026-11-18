using System.Diagnostics;
using GosTek.Models;

namespace GosTek.Services;

public sealed record ImportedCheckDocument(string Path, long Size, DocumentInfo Info, List<CheckIssue> Issues);

public static class CheckDocumentImport
{
    public static async Task<ImportedCheckDocument> LoadAsync(
        string cacheDirectory, Func<Task<Stream>> openReadAsync, RuleSet rules)
    {
        var path = Path.Combine(cacheDirectory, $"check_{Guid.NewGuid():N}.docx");
        try
        {
            await using (var input = await openReadAsync())
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true))
            {
                await input.CopyToAsync(output);
            }

            return await Task.Run(() =>
            {
                var (issues, info) = DocumentChecker.CheckWithInfo(path, rules);
                return new ImportedCheckDocument(path, new FileInfo(path).Length, info, issues);
            });
        }
        catch
        {
            DeleteWorkingCopy(path);
            throw;
        }
    }

    public static void DeleteWorkingCopy(string? path)
    {
        if (path is null)
            return;

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[CheckDocumentImport] {ex}");
        }
    }
}
