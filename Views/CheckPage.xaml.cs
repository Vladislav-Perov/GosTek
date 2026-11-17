/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Страница аудита оформления документов Word по стандартам ГОСТ с пакетным исправлением и конструктором правил
 * СОДЕРЖИМОЕ: CheckPage
 * ЗАВИСИМОСТИ: GosTek.Services.DocumentChecker, GosTek.Models.CheckModels, GosTek.Services.ExportService, GosTek.Services.CustomRulesService, GosTek.Helpers
 * ==============================================================================
 */
using System.Collections.ObjectModel;
using System.Windows.Input;
using GosTek.Helpers;
using GosTek.Models;
using GosTek.Resources.Styles;
using GosTek.Services;
using GosTek.Views.Controls;
using Microsoft.Maui.Controls.Shapes;

// В Shapes есть одноимённый Path
using Path = System.IO.Path;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace GosTek.Views;

/// <summary>
/// Экран «Проверка»: выбрать .docx, проверить оформление по набору правил,
/// исправить замечания и сохранить исправленную копию.
///
/// Исходный файл никогда не меняется: проверка и исправления идут по рабочей копии в кэше приложения.
/// </summary>
public partial class CheckPage : ContentPage
{
    private const string WorkFileName = "check_work.docx";

    private static readonly FilePickerFileType DocxFileType = new(new Dictionary<DevicePlatform, IEnumerable<string>> {
        [DevicePlatform.WinUI] = new[] { ".docx" },
        [DevicePlatform.Android] = new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
    });

    private string? _workPath;                 // рабочая копия; null, пока файл не выбран
    private string _sourceName = "";           // имя исходного файла (для имени копии)
    private DocumentInfo _info = new(null, 0);
    private long _sourceSize;

    private List<RuleSet> _allRules = new();
    private List<CheckIssue> _issues = new();                 // что ещё не так
    private readonly List<CheckIssue> _fixedIssues = new();   // что уже исправили
    private readonly HashSet<IssueKind> _rejectedKinds = new(); // отклонённые типы замечаний
    private string? _status;

    private bool _busy;

    public ObservableCollection<CheckIssue> Issues { get; } = new();
    public bool HasIssues => Issues.Count > 0;
    public bool NoIssues => Issues.Count == 0;
    public bool HasFixableIssues => Issues.Any(i => i.CanFix);

    public ICommand AcceptFixCommand { get; }
    public ICommand RejectFixCommand { get; }
    public ICommand AcceptAllCommand { get; }

    public CheckPage()
    {
        InitializeComponent();
        BindingContext = this;

        AcceptFixCommand = new Command<CheckIssue>(async issue => await AcceptFixAsync(issue));
        RejectFixCommand = new Command<CheckIssue>(issue => RejectFix(issue));
        AcceptAllCommand = new Command(async () => await AcceptAllAsync());

        RuleChips.ShowArrows = true;
        _allRules = CustomRulesService.Instance.GetAllRules().ToList();
        RuleChips.SetItems(_allRules.Select(p => p.Title).ToList());
        RuleChips.SelectionChanged += OnRuleChanged;

        UpdateUi();
    }

    private RuleSet CurrentRules
    {
        get {
            int i = RuleChips.SelectedIndex;
            return (i >= 0 && i < _allRules.Count) ? _allRules[i] : RuleSets.Universal;
        }
    }

    private CancellationTokenSource? _pulseCts;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshRulesAsync();
        if (_workPath is null)
            StartChoosePulse();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SettingsOverlay.Dismiss();
        StopChoosePulse();
    }

    private void StartChoosePulse()
    {
        if (_workPath is not null)
            return;

        StopChoosePulse();
        _pulseCts = new CancellationTokenSource();
        _ = AnimationHelper.AnimatePulseAsync(PrimaryButton, _pulseCts.Token);
    }

    private void StopChoosePulse()
    {
        _pulseCts?.Cancel();
        _pulseCts?.Dispose();
        _pulseCts = null;
        AnimationHelper.StopPulse(PrimaryButton);
    }

    private async Task RefreshRulesAsync()
    {
        await CustomRulesService.Instance.LoadRulesAsync();
        _allRules = CustomRulesService.Instance.GetAllRules().ToList();

        Dispatcher.Dispatch(() =>
        {
            int prevIndex = RuleChips.SelectedIndex;
            RuleChips.SetItems(_allRules.Select(r => r.Title).ToList());

            if (!string.IsNullOrEmpty(RuleEditorPage.LastSavedRuleId))
            {
                int savedIndex = _allRules.FindIndex(r => r.Id == RuleEditorPage.LastSavedRuleId);
                RuleEditorPage.LastSavedRuleId = null;
                if (savedIndex >= 0)
                {
                    RuleChips.SelectedIndex = savedIndex;
                    UpdateUi();
                    return;
                }
            }

            if (prevIndex >= 0 && prevIndex < _allRules.Count)
                RuleChips.SelectedIndex = prevIndex;
            else
                RuleChips.SelectedIndex = 0;

            UpdateUi();
        });
    }

    // =====================================================================
    //  ВЫБОР ФАЙЛА
    // =====================================================================

    private async void OnPickTapped(object? sender, TappedEventArgs e) => await PickAsync();

    private async Task PickAsync()
    {
        HapticHelper.Click();
        if (_busy)
            return;

        FileResult? picked;
        try {
            picked = await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Выберите документ Word",
                FileTypes = DocxFileType,
            });
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось выбрать файл", ex.Message, DialogKind.Danger);
            return;
        }

        if (picked is null)
            return;

        if (!picked.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) {
            await Dialogs.AlertAsync("Нужен файл Word",
                "Проверяются только документы в формате .docx. Старые .doc сохраните в Word как .docx.");
            return;
        }

        SetBusy(true);
        try {
            var work = Path.Combine(FileSystem.CacheDirectory, WorkFileName);

            // Рабочая копия: оригинал не трогаем. Поток нужен, потому что на Android путь — это content://
            await using (var input = await picked.OpenReadAsync())
            await using (var output = File.Create(work))
                await input.CopyToAsync(output);

            // Автоопределение правила по категории из имени файла
            var matchedRule = CustomRulesService.Instance.MatchRuleSetForTemplate(picked.FileName);
            int matchIdx = _allRules.FindIndex(r => r.Id.Equals(matchedRule.Id, StringComparison.OrdinalIgnoreCase));
            if (matchIdx >= 0)
                RuleChips.SelectedIndex = matchIdx;

            var rules = (matchIdx >= 0) ? _allRules[matchIdx] : CurrentRules;
            var issues = await Task.Run(() => DocumentChecker.Check(work, rules));
            var info = await Task.Run(() => DocumentChecker.ReadInfo(work));

            _workPath = work;
            _sourceName = picked.FileName;
            _sourceSize = new FileInfo(work).Length;
            _info = info;
            _issues = issues;
            _fixedIssues.Clear();
            _rejectedKinds.Clear();

            Issues.Clear();
            foreach (var issue in issues)
                Issues.Add(issue);

            var category = CustomRulesService.Instance.ExtractCategoryFromFileName(picked.FileName);
            if (!string.IsNullOrWhiteSpace(category) && matchedRule.Id != RuleSets.Universal.Id) {
                _status = $"Автоматически применено правило «{matchedRule.Title}» для категории «{category}».";
            } else {
                _status = null;
            }
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось проверить файл",
                "Файл повреждён или это не документ Word. " + ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
            UpdateUi();
        }
    }

    // =====================================================================
    //  ПРАВИЛА
    // =====================================================================

    // Выбран другой набор правил: перепроверяем текущую рабочую копию
    private async void OnRuleChanged(object? sender, int index)
    {
        UpdateUi();

        if (_workPath is null || _busy)
            return;

        SetBusy(true);
        try {
            var path = _workPath;
            var rules = CurrentRules;
            var raw = await Task.Run(() => DocumentChecker.Check(path, rules));
            _fixedIssues.Clear();
            _issues = raw.Where(i => !_rejectedKinds.Contains(i.Kind)).ToList();

            Issues.Clear();
            foreach (var issue in _issues)
                Issues.Add(issue);

            _status = null;
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось проверить файл", ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void OnEditCurrentRuleTapped(object? sender, EventArgs e)
    {
        var curr = CurrentRules;
        RuleEditorPage.TargetRule = curr;
        RuleEditorPage.BaselineRule = RuleSets.Universal;
        await Shell.Current.GoToAsync("RuleEditorPage");
    }

    // =====================================================================
    //  ИСПРАВЛЕНИЯ И КОМАНДЫ КАРТОЧЕК
    // =====================================================================

    private async Task AcceptFixAsync(CheckIssue? issue)
    {
        if (issue is null || _busy || _workPath is null)
            return;

        if (!issue.CanFix)
        {
            Toast.Show("Это замечание необходимо поправить вручную в Word", "info");
            return;
        }

        SetBusy(true);
        try
        {
            var path = _workPath;
            var rules = CurrentRules;

            int changes = await Task.Run(() => DocumentChecker.Fix(path, rules, new[] { issue.Kind }));

            // Fix работает по Kind, поэтому убираем из списка все замечания этого типа
            var sameKind = Issues.Where(x => x.Kind == issue.Kind).ToList();
            foreach (var i in sameKind)
            {
                _fixedIssues.Add(i);
                Issues.Remove(i);
            }
            _issues = Issues.ToList();

            if (changes > 0)
            {
                HapticHelper.Success();
                Toast.Show("Исправление применено", "check");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось исправить", ex.Message, DialogKind.Danger);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private void RejectFix(CheckIssue? issue)
    {
        if (issue is null)
            return;

        HapticHelper.Click();
        _rejectedKinds.Add(issue.Kind);
        Issues.Remove(issue);
        _issues = Issues.ToList();
        UpdateUi();
    }

    private async Task AcceptAllAsync()
    {
        if (_busy || _workPath is null)
            return;

        var fixable = Issues.Where(i => i.CanFix).ToList();
        if (fixable.Count == 0)
            return;

        SetBusy(true);
        try
        {
            var path = _workPath;
            var rules = CurrentRules;
            var kinds = fixable.Select(t => t.Kind).Distinct().ToList();

            int changes = await Task.Run(() => DocumentChecker.Fix(path, rules, kinds));

            foreach (var f in fixable)
            {
                _fixedIssues.Add(f);
                Issues.Remove(f);
            }
            _issues = Issues.ToList();

            if (changes > 0)
            {
                HapticHelper.Success();
                Toast.Show($"Исправлено замечаний: {changes}", "sparkles");
            }
            _status = changes > 0
                ? $"Исправлено: {RuFormat.Count(changes, "изменение", "изменения", "изменений")}."
                : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось исправить", ex.Message, DialogKind.Danger);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void OnPrimaryTapped(object? sender, TappedEventArgs e)
    {
        StopChoosePulse();
        HapticHelper.Click();
        if (_busy)
            return;

        var fixable = Issues.Where(i => i.CanFix).ToList();
        if (_workPath is not null && fixable.Count > 0)
            await AcceptAllAsync();
        else
            await PickAsync();
    }

    private async void OnSaveCopyTapped(object? sender, TappedEventArgs e)
    {
        if (_busy || _workPath is null)
            return;

        SetBusy(true);
        try {
            var stem = Path.GetFileNameWithoutExtension(_sourceName);
            var name = ExportService.SafeFileName(stem + "_исправлено", "Документ_исправлено");

            var result = await ExportService.SaveCopyAsync(_workPath, name);
            if (result is null)
                return;

            SetBusy(false);
            HapticHelper.Success();
            Toast.Show("Копия сохранена", "save");
            await ExportService.AfterSaveAsync(result);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
        }
    }

    // =====================================================================
    //  ВНЕШНИЙ ВИД
    // =====================================================================

    private void SetBusy(bool busy)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => SetBusy(busy));
            return;
        }

        _busy = busy;
        Loader.IsVisible = busy;
        Loader.IsRunning = busy;
    }

    private void UpdateUi()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(UpdateUi);
            return;
        }

        bool hasFile = _workPath is not null;

        DropzoneBorder.IsVisible = !hasFile;
        EmptyPanel.IsVisible = !hasFile;
        ResultPanel.IsVisible = hasFile;

        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(NoIssues));
        OnPropertyChanged(nameof(HasFixableIssues));

        if (!hasFile) {
            FileNameLabel.Text = "Документ не выбран";
            FileInfoLabel.Text = "Подойдёт файл Word (.docx)";
            PickLabel.Text = "Выбрать";
            PrimaryLabel.Text = "Выбрать документ";
            SaveButton.IsVisible = false;
            Grid.SetColumnSpan(PrimaryButton, 2);
            StartChoosePulse();
            return;
        }

        StopChoosePulse();

        FileNameLabel.Text = _sourceName;
        FileInfoLabel.Text = BuildInfoText();
        PickLabel.Text = "Заменить";

        var fixable = Issues.Where(i => i.CanFix).ToList();

        // Главная кнопка: исправить (если есть что), иначе снова выбрать
        if (fixable.Count > 0) {
            PrimaryLabel.Text = $"Принять всё ({fixable.Count})";
            SaveButton.IsVisible = false;
            Grid.SetColumnSpan(PrimaryButton, 2);
        } else {
            PrimaryLabel.Text = "Выбрать другой";
            SaveButton.IsVisible = true;
            Grid.SetColumnSpan(PrimaryButton, 1);
        }

        // Сообщение о статусе после правки
        StatusLabel.Text = _status;
        StatusLabel.IsVisible = _status is not null;
    }

    private async void OnDropFile(object? sender, DropEventArgs e)
    {
        try
        {
            var text = await e.Data.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text) && File.Exists(text) && text.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                await LoadFromPathAsync(text);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DropFile] Ошибка: {ex.Message}");
        }
    }

    private async Task LoadFromPathAsync(string filePath)
    {
        SetBusy(true);
        try
        {
            var work = Path.Combine(FileSystem.CacheDirectory, WorkFileName);
            File.Copy(filePath, work, true);

            var fileName = Path.GetFileName(filePath);
            var matchedRule = CustomRulesService.Instance.MatchRuleSetForTemplate(fileName);
            int matchIdx = _allRules.FindIndex(r => r.Id.Equals(matchedRule.Id, StringComparison.OrdinalIgnoreCase));
            if (matchIdx >= 0)
                RuleChips.SelectedIndex = matchIdx;

            var rules = (matchIdx >= 0) ? _allRules[matchIdx] : CurrentRules;
            var issues = await Task.Run(() => DocumentChecker.Check(work, rules));
            var info = await Task.Run(() => DocumentChecker.ReadInfo(work));

            _workPath = work;
            _sourceName = fileName;
            _sourceSize = new FileInfo(work).Length;
            _info = info;
            _issues = issues;
            _fixedIssues.Clear();
            _rejectedKinds.Clear();

            Issues.Clear();
            foreach (var issue in issues)
                Issues.Add(issue);

            var category = CustomRulesService.Instance.ExtractCategoryFromFileName(fileName);
            if (!string.IsNullOrWhiteSpace(category) && matchedRule.Id != RuleSets.Universal.Id) {
                _status = $"Автоматически применено правило «{matchedRule.Title}» для категории «{category}».";
            } else {
                _status = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось проверить файл", ex.Message, DialogKind.Danger);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private string BuildInfoText()
    {
        var parts = new List<string> { RuFormat.FileSize(_sourceSize) };

        if (_info.Pages is { } pages)
            parts.Add(RuFormat.Count(pages, "страница", "страницы", "страниц"));
        if (_info.Paragraphs > 0)
            parts.Add(RuFormat.Count(_info.Paragraphs, "абзац", "абзаца", "абзацев"));

        return string.Join(" • ", parts);
    }
}
