using GosTek.Models;
using GosTek.Services;
using GosTek.Views.Controls;

namespace GosTek.Views;

public partial class ScanPage
{
    private static bool IsDesktop => DeviceInfo.Platform == DevicePlatform.WinUI;
    private static readonly string[] DesktopExtensions = [".jpg", ".jpeg", ".png"];
    private static readonly FilePickerFileType DesktopImageTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.WinUI] = DesktopExtensions,
    });

    private byte[]? _desktopImage;
    private bool _choosingDesktopImage;

    private async void OnChooseDesktopImageClicked(object? sender, EventArgs e) => await ChooseDesktopImageAsync();

    private async Task ChooseDesktopImageAsync()
    {
        if (!IsDesktop || _busy || _choosingDesktopImage)
            return;
        _choosingDesktopImage = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Выберите фото или скан документа",
                FileTypes = DesktopImageTypes,
            });
            if (file is not null && _isPageVisible)
                await LoadDesktopImageAsync(file.FullPath);
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Не удалось открыть изображение", ex.Message, DialogKind.Danger);
        }
        finally
        {
            _choosingDesktopImage = false;
        }
    }

    private async Task LoadDesktopImageAsync(string path)
    {
        var file = new FileInfo(path);
        if (!DesktopExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Выберите изображение JPG или PNG.");
        if (file.Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Размер изображения должен быть не больше 32 МБ.");

        var bytes = await File.ReadAllBytesAsync(path);
        if (!_isPageVisible)
            return;

        _desktopImage = bytes;
        DesktopImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false));
        DesktopImage.IsVisible = true;
        DesktopDropZone.HeightRequest = 360;
        DesktopZoomButton.IsVisible = true;
        DesktopDropHint.IsVisible = false;
        DesktopFileName.Text = file.Name;
        DesktopFileName.IsVisible = true;
        DesktopImageActions.IsVisible = true;
        DesktopError.IsVisible = false;
        ShowResult(false);
    }

    private void OnDesktopImageTapped(object? sender, TappedEventArgs e) => ShowDesktopImage();

    private void OnDesktopZoomClicked(object? sender, EventArgs e) => ShowDesktopImage();

    private void ShowDesktopImage()
    {
        if (_desktopImage is null)
            return;

        DesktopLargeImage.Source = DesktopImage.Source;
        DesktopImageOverlay.IsVisible = true;
        DesktopZoomCloseButton.Focus();
    }

    private void OnCloseDesktopZoomClicked(object? sender, EventArgs e) => TryCloseDesktopImage();

    internal bool TryCloseDesktopImage()
    {
        if (!DesktopImageOverlay.IsVisible)
            return false;

        DesktopImageOverlay.IsVisible = false;
        DesktopLargeImage.Source = null;
        return true;
    }

    private async void OnRecognizeDesktopClicked(object? sender, EventArgs e)
    {
        if (_desktopImage is not { } bytes || _choosingDesktopImage)
            return;
        await AcquireAndRecognizeAsync("Не удалось открыть изображение",
            _ => Task.FromResult<Stream?>(new MemoryStream(bytes, writable: false)), pausePreview: false);
    }

#if WINDOWS
    private void EnableDesktopDrop()
    {
        if (DesktopDropZone.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement target)
            return;

        target.AllowDrop = true;
        target.DragOver -= OnDesktopDragOver;
        target.DragOver += OnDesktopDragOver;
        target.Drop -= OnDesktopDrop;
        target.Drop += OnDesktopDrop;
    }

    private void OnDesktopDragOver(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (!_busy && !_choosingDesktopImage &&
            e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
    }

    private async void OnDesktopDrop(object sender, Microsoft.UI.Xaml.DragEventArgs e)
    {
        if (_busy || _choosingDesktopImage)
            return;

        var deferral = e.GetDeferral();
        _choosingDesktopImage = true;
        try
        {
            if (e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count != 1 || items[0] is not global::Windows.Storage.StorageFile file)
                    throw new InvalidDataException("Перетащите один файл JPG или PNG.");

                await LoadDesktopImageAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Не удалось открыть изображение", ex.Message, DialogKind.Danger);
        }
        finally
        {
            _choosingDesktopImage = false;
            deferral.Complete();
        }
    }
#endif

    private async void OnExportGostPdfClicked(object? sender, EventArgs e) => await ExportDesktopScanAsync(pdf: true);

    private async Task ExportDesktopScanAsync(bool pdf)
    {
        if (!IsDesktop || _busy)
            return;

        var text = RawTextEditor.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Show("Текст документа пуст", "alert");
            return;
        }

        _busy = true;
        var extension = pdf ? ".pdf" : ".docx";
        var temporaryBase = Path.Combine(FileSystem.CacheDirectory, Guid.NewGuid().ToString("N"));
        var docxPath = temporaryBase + ".docx";
        var exportPath = temporaryBase + extension;
        try
        {
            var title = ExportService.SafeFileName(ScanTemplateBuilder.DetectDocumentTitle(text), "Документ");
            var name = $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var options = BuildGostFormattingOptions();
            ScanLoading.Show("Подготовка документа...", canCancel: false);
            await Task.Run(() =>
            {
                GostDocxExporter.ExportToDocx(docxPath, text, options);
                if (pdf)
                {
                    var paragraphs = TemplateEngine.ReadPreview(docxPath, literalLayout: true);
                    var formatting = new DocumentFormatting(options.MarginLeftMm, options.MarginRightMm,
                        options.MarginTopMm, options.MarginBottomMm, options.FontFamily, null, null, null, null);
                    using var stream = File.Create(exportPath);
                    PdfExportService.GeneratePdf(title, paragraphs, new Dictionary<string, string>(), stream, formatting);
                }
            });
            ScanLoading.Hide();

            var result = await ExportService.SaveCopyAsync(exportPath, name, extension);
            if (result is not null)
            {
                Toast.Show(pdf ? "PDF сохранён" : "Документ Word сохранён", "file");
                await ExportService.AfterSaveAsync(result);
            }
        }
        catch (Exception ex)
        {
            ScanLoading.Hide();
            await Dialogs.AlertAsync("Ошибка экспорта", ex.Message, DialogKind.Danger);
        }
        finally
        {
            _busy = false;
            ScanLoading.Hide();
            RegisterTempFile(docxPath);
            RegisterTempFile(exportPath);
            CleanupTempFiles();
        }
    }
}
