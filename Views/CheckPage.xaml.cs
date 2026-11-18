/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Страница аудита оформления документов Word по стандартам ГОСТ с пакетным и точечным исправлением
 * СОДЕРЖИМОЕ: CheckPage
 * ЗАВИСИМОСТИ: GosTek.Services.DocumentChecker, GosTek.Models.CheckModels, GosTek.Views.CheckGroupVm, GosTek.Services.ExportService, GosTek.Services.CustomRulesService, GosTek.Helpers
 * ==============================================================================
 */
using System.Collections.ObjectModel;
using System.Diagnostics;
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
/// исправить замечания (целиком или точечно) и сохранить исправленную копию.
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
    private List<CheckIssue> _rawIssues = new(); // полный результат последней проверки
    private readonly HashSet<(IssueKind Kind, int Index, string Text)> _rejectedIssues = new(); // отклонённые элементы и карточки
    private string? _status;

    private bool _busy;

    public ObservableCollection<CheckGroupVm> IssueGroups { get; } = new();
    public ObservableCollection<CheckGroupVm> Issues => IssueGroups;
    public bool HasIssues => IssueGroups.Count > 0;
    public bool NoIssues => _workPath != null && IssueGroups.Count == 0;
    public bool HasFixableIssues => IssueGroups.Any(i => i.CanFix);

    public ICommand ToggleExpandCommand { get; }
    public ICommand LoadMoreCommand { get; }
    public ICommand AcceptItemCommand { get; }
    public ICommand RejectItemCommand { get; }
    public ICommand AcceptGroupCommand { get; }
    public ICommand RejectGroupCommand { get; }
    public ICommand AcceptAllCommand { get; }

    // Для обратной совместимости
    public ICommand AcceptFixCommand => AcceptGroupCommand;
    public ICommand RejectFixCommand => RejectGroupCommand;

    public CheckPage()
    {
        InitializeComponent();
        BindingContext = this;

        ToggleExpandCommand = new Command<CheckGroupVm>(ToggleExpand);
        LoadMoreCommand = new Command<CheckGroupVm>(group =>
        {
            if (_busy || group == null) return;
            HapticHelper.Click();
            group.LoadMore(CheckGroupVm.PageSize);
        });
        AcceptItemCommand = new Command<CheckItemVm>(async item => await AcceptItemAsync(item));
        RejectItemCommand = new Command<CheckItemVm>(RejectItem);
        AcceptGroupCommand = new Command<CheckGroupVm>(async group => await AcceptGroupAsync(group));
        RejectGroupCommand = new Command<CheckGroupVm>(RejectGroup);
        AcceptAllCommand = new Command(async () => await AcceptAllAsync());

        RuleChips.ShowArrows = true;
        _allRules = CustomRulesService.Instance.GetAllRules().ToList();
        var chipTitles = _allRules.Select(p => p.Title).ToList();
        chipTitles.Add("+");
        RuleChips.SetItems(chipTitles);
        RuleChips.SelectionChanged += OnRuleChanged;

        UpdateUi();
    }

    private RuleSet CurrentRules
    {
        get {
            int i = RuleChips.SelectedIndex;
            return (i >= 0 && i < _allRules.Count) ? _allRules[i] : (_allRules.Count > 0 ? _allRules[0] : RuleSets.MasterRule);
        }
    }

    private CancellationTokenSource? _pulseCts;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        WireCardsContainer();
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
            string targetId = RuleEditorPage.LastSavedRuleId ?? Preferences.Get("active_rule_id", RuleSets.MasterRuleId);
            RuleEditorPage.LastSavedRuleId = null;

            // Миграция старых Id в MasterRuleId
            targetId = CustomRulesService.NormalizeRuleId(targetId);

            int targetIndex = _allRules.FindIndex(r => r.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
                targetIndex = 0; // Если правило было удалено, переключаем на «Основной ГОСТ»

            var chipTitles = _allRules.Select(r => r.Title).ToList();
            chipTitles.Add("+");

            RuleChips.SetItems(chipTitles, targetIndex);
            Preferences.Set("active_rule_id", CurrentRules.Id);

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
            {
                RuleChips.SelectedIndex = matchIdx;
                Preferences.Set("active_rule_id", matchedRule.Id);
            }

            var rules = (matchIdx >= 0) ? _allRules[matchIdx] : CurrentRules;
            var swCheck = Stopwatch.StartNew();
            var issues = await Task.Run(() => DocumentChecker.Check(work, rules));
            swCheck.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Check (PickAsync): {swCheck.ElapsedMilliseconds} ms, found {issues.Count} issues");

            var swInfo = Stopwatch.StartNew();
            var info = await Task.Run(() => DocumentChecker.ReadInfo(work));
            swInfo.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] ReadInfo (PickAsync): {swInfo.ElapsedMilliseconds} ms, pages: {info.Pages}, paragraphs: {info.Paragraphs}");

            _workPath = work;
            _sourceName = picked.FileName;
            _sourceSize = new FileInfo(work).Length;
            _info = info;
            _rawIssues = issues;
            _rejectedIssues.Clear();

            UpdateIssueGroupsInPlace(issues);

            var category = CustomRulesService.Instance.ExtractCategoryFromFileName(picked.FileName);
            if (!string.IsNullOrWhiteSpace(category) && matchedRule.Id != RuleSets.MasterRuleId) {
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
        if (index == _allRules.Count)
        {
            // Нажат чип «+» — восстанавливаем выбор и открываем редактор с новым правилом
            string activeId = Preferences.Get("active_rule_id", RuleSets.MasterRuleId);
            int restoreIndex = _allRules.FindIndex(r => r.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase));
            if (restoreIndex < 0) restoreIndex = 0;
            RuleChips.SelectedIndex = restoreIndex;

            await CreateNewRuleAndOpenEditorAsync();
            return;
        }

        Preferences.Set("active_rule_id", CurrentRules.Id);
        UpdateUi();

        if (_workPath is null || _busy)
            return;

        SetBusy(true);
        try {
            var path = _workPath;
            var rules = CurrentRules;
            var swCheck = Stopwatch.StartNew();
            var raw = await Task.Run(() => DocumentChecker.Check(path, rules));
            swCheck.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Check (OnRuleChanged): {swCheck.ElapsedMilliseconds} ms, found {raw.Count} issues");

            _rejectedIssues.Clear();
            _rawIssues = raw;
            _status = null;

            UpdateIssueGroupsInPlace(raw);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось проверить файл", ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async Task CreateNewRuleAndOpenEditorAsync()
    {
        if (_busy)
            return;

        HapticHelper.Click();
        var newRule = CustomRulesService.Instance.CreateNewRule();
        await CustomRulesService.Instance.SaveRuleAsync(newRule);
        RuleEditorPage.TargetRule = newRule;
        RuleEditorPage.LastSavedRuleId = newRule.Id;
        await Shell.Current.GoToAsync("RuleEditorPage");
    }

    private async void OnEditCurrentRuleTapped(object? sender, EventArgs e)
    {
        if (_busy) return;
        var curr = CurrentRules;
        RuleEditorPage.TargetRule = curr;
        RuleEditorPage.BaselineRule = RuleSets.MasterRule;
        await Shell.Current.GoToAsync("RuleEditorPage");
    }

    // =====================================================================
    //  ИСПРАВЛЕНИЯ И КОМАНДЫ КАРТОЧЕК
    // =====================================================================

    private void ToggleExpand(CheckGroupVm? group)
    {
        if (_busy || group == null)
            return;

        HapticHelper.Click();
        group.IsExpanded = !group.IsExpanded;
    }

    private void RejectItem(CheckItemVm? item)
    {
        if (_busy || item == null)
            return;

        HapticHelper.Click();
        _rejectedIssues.Add((item.Parent.Issue.Kind, item.Index, item.Text));
        Toast.Show("Скрыто, вернётся при повторной проверке", "info");
        ApplyRejectionFilter();
        UpdateUi();
    }

    private void RejectGroup(CheckGroupVm? group)
    {
        if (_busy || group == null)
            return;

        HapticHelper.Click();
        if (group.Issue.Items != null && group.Issue.Items.Count > 0)
        {
            foreach (var it in group.Issue.Items)
            {
                _rejectedIssues.Add((group.Issue.Kind, it.Index, it.Text));
            }
        }
        else
        {
            _rejectedIssues.Add((group.Issue.Kind, -1, group.Issue.Title));
        }

        Toast.Show("Скрыто, вернётся при повторной проверке", "info");
        ApplyRejectionFilter();
        UpdateUi();
    }

    private async Task AcceptItemAsync(CheckItemVm? item)
    {
        if (item == null || _busy || _workPath == null)
            return;

        if (!item.CanFix)
        {
            Toast.Show("Это замечание необходимо поправить вручную в Word", "info");
            return;
        }

        SetBusy(true);
        try
        {
            var path = _workPath;
            var rules = CurrentRules;
            var kind = item.Parent.Issue.Kind;
            var indices = new HashSet<int> { item.Index };
            var samples = new Dictionary<int, string> { [item.Index] = item.Text };

            var swFix = Stopwatch.StartNew();
            int changed = await Task.Run(() => DocumentChecker.FixItems(path, rules, kind, indices, samples));
            swFix.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] FixItems ({kind}, {indices.Count} items): {swFix.ElapsedMilliseconds} ms, changed: {changed}");

            if (changed > 0)
            {
                HapticHelper.Success();
                Toast.Show("Исправление применено", "check");
            }
            await RecheckAsync();
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

    private async Task AcceptGroupAsync(CheckGroupVm? group)
    {
        if (group == null || _busy || _workPath == null)
            return;

        if (!group.CanFix)
        {
            Toast.Show("Это замечание необходимо поправить вручную в Word", "info");
            return;
        }

        SetBusy(true);
        try
        {
            var path = _workPath;
            var rules = CurrentRules;
            var issue = group.Issue;
            int changed;
            var swFix = Stopwatch.StartNew();

            if (issue.Items != null && issue.Items.Count > 0)
            {
                var indices = issue.Items.Select(it => it.Index).ToHashSet();
                var samples = issue.Items.ToDictionary(it => it.Index, it => it.Text);
                changed = await Task.Run(() => DocumentChecker.FixItems(path, rules, issue.Kind, indices, samples));
            }
            else
            {
                changed = await Task.Run(() => DocumentChecker.Fix(path, rules, new[] { issue.Kind }));
            }
            swFix.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Fix/FixItems ({issue.Kind}): {swFix.ElapsedMilliseconds} ms, changed: {changed}");

            if (changed > 0)
            {
                HapticHelper.Success();
                Toast.Show("Исправление применено", "check");
            }
            await RecheckAsync();
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

    private async Task AcceptAllAsync()
    {
        if (_busy || _workPath is null)
            return;

        var fixable = IssueGroups.Where(i => i.CanFix).ToList();
        if (fixable.Count == 0)
            return;

        SetBusy(true);
        try
        {
            var path = _workPath;
            var rules = CurrentRules;
            var kinds = fixable.Select(t => t.Issue.Kind).Distinct().ToList();

            var swFix = Stopwatch.StartNew();
            int changes = await Task.Run(() => DocumentChecker.Fix(path, rules, kinds));
            swFix.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Fix ({string.Join(", ", kinds)}): {swFix.ElapsedMilliseconds} ms, changed: {changes}");

            if (changes > 0)
            {
                HapticHelper.Success();
                Toast.Show($"Исправлено замечаний: {changes}", "sparkles");
            }
            _status = changes > 0
                ? $"Исправлено: {RuFormat.Count(changes, "изменение", "изменения", "изменений")}."
                : null;

            await RecheckAsync();
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

    private async Task RecheckAsync()
    {
        if (_workPath == null)
            return;

        var path = _workPath;
        var rules = CurrentRules;
        var swCheck = Stopwatch.StartNew();
        var raw = await Task.Run(() => DocumentChecker.Check(path, rules));
        swCheck.Stop();
        System.Diagnostics.Debug.WriteLine($"[PERF] Check (RecheckAsync): {swCheck.ElapsedMilliseconds} ms, found {raw.Count} issues");

        _rawIssues = raw;
        // В RecheckAsync не вызываем ReadInfo (сохраняем размер и число страниц прежними)
        System.Diagnostics.Debug.WriteLine("[PERF] ReadInfo skipped in RecheckAsync (preserved size & pages)");

        ApplyRejectionFilter();
    }

    private void ApplyRejectionFilter()
    {
        var filtered = CheckIssueFilter.FilterIssues(_rawIssues, _rejectedIssues);
        UpdateIssueGroupsInPlace(filtered);
    }

    private void UpdateIssueGroupsInPlace(IReadOnlyList<CheckIssue> newIssues)
    {
        var sw = Stopwatch.StartNew();

        // 1. Удаляем группы, которых больше нет в замечаниях
        var newKindSet = new HashSet<IssueKind>(newIssues.Select(i => i.Kind));
        for (int i = IssueGroups.Count - 1; i >= 0; i--)
        {
            if (!newKindSet.Contains(IssueGroups[i].Issue.Kind))
            {
                IssueGroups.RemoveAt(i);
            }
        }

        // 2. Обновляем оставшиеся и добавляем новые, соблюдая порядок
        var existingByKind = IssueGroups.ToDictionary(g => g.Issue.Kind);

        for (int i = 0; i < newIssues.Count; i++)
        {
            var issue = newIssues[i];
            if (existingByKind.TryGetValue(issue.Kind, out var existingGroup))
            {
                // Обновляем на месте — свойство IsExpanded сохраняется
                existingGroup.UpdateIssue(issue);

                int currentIndex = IssueGroups.IndexOf(existingGroup);
                if (currentIndex != i)
                {
                    IssueGroups.Move(currentIndex, i);
                }
            }
            else
            {
                // Новая группа замечаний
                var newGroup = new CheckGroupVm(issue);
                IssueGroups.Insert(i, newGroup);
                existingByKind[issue.Kind] = newGroup;
            }
        }

        sw.Stop();
        System.Diagnostics.Debug.WriteLine($"[PERF] Update IssueGroups in-place: {sw.ElapsedMilliseconds} ms, count: {IssueGroups.Count}");

        WireCardsContainer();
    }

    private async void OnPrimaryTapped(object? sender, TappedEventArgs e)
    {
        StopChoosePulse();
        HapticHelper.Click();
        if (_busy)
            return;

        var fixable = IssueGroups.Where(i => i.CanFix).ToList();
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

        ActionBar.IsEnabled = !busy;
        PrimaryButton.IsEnabled = !busy;
        PrimaryButton.Opacity = busy ? 0.6 : 1.0;
        SaveButton.IsEnabled = !busy;
        SaveButton.Opacity = busy ? 0.6 : 1.0;

        ResultPanel.IsEnabled = !busy;
        ResultPanel.Opacity = busy ? 0.65 : 1.0;

        DropzoneBorder.IsEnabled = !busy;
        FileSelectedCard.IsEnabled = !busy;
        RuleChips.IsEnabled = !busy;
        EditCurrentRuleLabel.IsEnabled = !busy;
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

        OnPropertyChanged(nameof(IssueGroups));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(NoIssues));
        OnPropertyChanged(nameof(HasFixableIssues));

        RuleNote.Text = CurrentRules.Description;
        RuleNote.IsVisible = !string.IsNullOrWhiteSpace(CurrentRules.Description);

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

        var fixable = IssueGroups.Where(i => i.CanFix).ToList();

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
        if (_busy) return;
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
            {
                RuleChips.SelectedIndex = matchIdx;
                Preferences.Set("active_rule_id", matchedRule.Id);
            }

            var rules = (matchIdx >= 0) ? _allRules[matchIdx] : CurrentRules;
            var swCheck = Stopwatch.StartNew();
            var issues = await Task.Run(() => DocumentChecker.Check(work, rules));
            swCheck.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] Check (LoadFromPath): {swCheck.ElapsedMilliseconds} ms, found {issues.Count} issues");

            var swInfo = Stopwatch.StartNew();
            var info = await Task.Run(() => DocumentChecker.ReadInfo(work));
            swInfo.Stop();
            System.Diagnostics.Debug.WriteLine($"[PERF] ReadInfo (LoadFromPath): {swInfo.ElapsedMilliseconds} ms, pages: {info.Pages}, paragraphs: {info.Paragraphs}");

            _workPath = work;
            _sourceName = fileName;
            _sourceSize = new FileInfo(work).Length;
            _info = info;
            _rawIssues = issues;
            _rejectedIssues.Clear();

            UpdateIssueGroupsInPlace(issues);

            var category = CustomRulesService.Instance.ExtractCategoryFromFileName(fileName);
            if (!string.IsNullOrWhiteSpace(category) && matchedRule.Id != RuleSets.MasterRuleId) {
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

    private void WireCardsContainer()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(WireCardsContainer);
            return;
        }

        if (ResultPanel == null) return;

        foreach (var child in ResultPanel.Children)
        {
            if (child is VerticalStackLayout vsl && BindableLayout.GetItemsSource(vsl) == IssueGroups)
            {
                vsl.ChildAdded -= OnCardViewAdded;
                vsl.ChildAdded += OnCardViewAdded;

                foreach (var card in vsl.Children)
                {
                    if (card is Element elem)
                        WireCardView(elem);
                }
                break;
            }
        }
    }

    private void OnCardViewAdded(object? sender, ElementEventArgs e)
    {
        WireCardView(e.Element);
    }

    private void WireCardView(Element element)
    {
        FindAndWireMoreLabel(element);
    }

    private void FindAndWireMoreLabel(Element parent)
    {
        if (parent is Label lbl)
        {
            if (lbl.FontAttributes == FontAttributes.Italic &&
                (lbl.HorizontalOptions.Alignment == LayoutAlignment.Center || lbl.HorizontalOptions == LayoutOptions.Center))
            {
                if (lbl.GestureRecognizers.Count == 0)
                {
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (s, e) =>
                    {
                        if (_busy) return;
                        if (lbl.BindingContext is CheckGroupVm group)
                        {
                            HapticHelper.Click();
                            group.LoadMore(CheckGroupVm.PageSize);
                        }
                    };
                    lbl.GestureRecognizers.Add(tap);
                    lbl.TextDecorations = TextDecorations.Underline;
                    lbl.TextColor = Color.FromArgb("#16A34A");
                }
            }
        }

        if (parent is Border border && border.Content != null)
        {
            FindAndWireMoreLabel(border.Content);
        }
        else if (parent is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element elemChild)
                    FindAndWireMoreLabel(elemChild);
            }
        }
        else if (parent is ContentView cv && cv.Content != null)
        {
            FindAndWireMoreLabel(cv.Content);
        }
    }
}
