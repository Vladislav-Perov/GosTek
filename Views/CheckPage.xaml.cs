/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Страница аудита оформления документов Word по стандартам ГОСТ с пакетным исправлением и конструктором правил
 * СОДЕРЖИМОЕ: CheckPage
 * ЗАВИСИМОСТИ: GosTek.Services.DocumentChecker, GosTek.Models.CheckModels, GosTek.Services.ExportService, GosTek.Services.CustomRulesService, GosTek.Helpers
 * ==============================================================================
 */
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
/// исправить простые ошибки и сохранить исправленную копию.
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
    private readonly List<CheckIssue> _fixedIssues = new();   // что уже исправили (показываем зачёркнутыми)
    private string? _status;

    private bool _busy;

    public CheckPage()
    {
        InitializeComponent();

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
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
        await RefreshRulesAsync();
        if (_workPath is null)
            StartChoosePulse();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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

        int prevIndex = RuleChips.SelectedIndex;
        RuleChips.SetItems(_allRules.Select(r => r.Title).ToList());
        if (prevIndex >= 0 && prevIndex < _allRules.Count)
            RuleChips.SelectedIndex = prevIndex;
        else
            RuleChips.SelectedIndex = 0;

        UpdateCustomRulesList();
        UpdateUi();
    }

    private void UpdateCustomRulesList()
    {
        CustomRulesHost.Children.Clear();
        var customRules = CustomRulesService.Instance.GetCustomRules();

        if (customRules.Count == 0) {
            var emptyLabel = new Label {
                Text = "Пользовательские правила пока не созданы. Нажмите «С нуля» или «На основе...», чтобы добавить своё правило.",
                FontSize = 12,
                Margin = new Thickness(0, 4),
            };
            emptyLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            CustomRulesHost.Children.Add(emptyLabel);
            return;
        }

        foreach (var rule in customRules) {
            var card = new Border {
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12, 8),
            };
            card.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
            card.SetDynamicResource(Border.StrokeProperty, "CardStroke");

            var grid = new Grid {
                ColumnDefinitions = new ColumnDefinitionCollection {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 8,
            };

            var infoStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

            var titleRow = new HorizontalStackLayout { Spacing = 8 };
            var titleLabel = new Label {
                Text = rule.Title,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            };
            titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            titleRow.Children.Add(titleLabel);

            if (!string.IsNullOrWhiteSpace(rule.Category)) {
                var catBorder = new Border {
                    BackgroundColor = Color.FromArgb("#ECFDF5"),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = 6 },
                    Padding = new Thickness(6, 2),
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label {
                        Text = rule.Category,
                        FontSize = 11,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#065F46"),
                    },
                };
                titleRow.Children.Add(catBorder);
            }
            infoStack.Children.Add(titleRow);

            var detailsLabel = new Label {
                Text = $"Поля: {rule.Left.Cm}/{rule.Right.Cm}/{rule.Top.Cm}/{rule.Bottom.Cm} см • {rule.FontName} {rule.FontSizePt}pt • инт. {rule.LineSpacing}",
                FontSize = 12,
            };
            detailsLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            infoStack.Children.Add(detailsLabel);

            grid.Children.Add(infoStack);
            Grid.SetColumn(infoStack, 0);

            var editBtn = new Border {
                StyleClass = new[] { "HoverCard" },
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(10, 6),
                VerticalOptions = LayoutOptions.Center,
            };
            var editLabel = new Label {
                Text = "✏️ Изменить",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
            };
            editLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            editBtn.Content = editLabel;

            var ruleToEdit = rule;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) => {
                RuleEditorPage.TargetRule = ruleToEdit;
                RuleEditorPage.BaselineRule = RuleSets.Universal;
                await Shell.Current.GoToAsync("RuleEditorPage");
            };
            editBtn.GestureRecognizers.Add(tap);

            grid.Children.Add(editBtn);
            Grid.SetColumn(editBtn, 1);

            card.Content = grid;
            CustomRulesHost.Children.Add(card);
        }
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
            _issues = await Task.Run(() => DocumentChecker.Check(path, rules));
            _status = null;
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось проверить файл", ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void OnCreateNewRuleTapped(object? sender, EventArgs e)
    {
        RuleEditorPage.TargetRule = null;
        RuleEditorPage.BaselineRule = RuleSets.Universal;
        await Shell.Current.GoToAsync("RuleEditorPage");
    }

    private async void OnCreateBasedOnRuleTapped(object? sender, EventArgs e)
    {
        var titles = _allRules.Select(r => r.Title).ToArray();
        var chosen = await DisplayActionSheetAsync("Выберите правило за основу", "Отмена", null, titles);
        if (!string.IsNullOrWhiteSpace(chosen) && chosen != "Отмена") {
            var baseRule = _allRules.FirstOrDefault(r => r.Title == chosen) ?? RuleSets.Universal;
            RuleEditorPage.TargetRule = baseRule;
            RuleEditorPage.BaselineRule = baseRule;
            await Shell.Current.GoToAsync("RuleEditorPage");
        }
    }

    private async void OnEditCurrentRuleTapped(object? sender, EventArgs e)
    {
        var curr = CurrentRules;
        RuleEditorPage.TargetRule = curr;
        RuleEditorPage.BaselineRule = curr.IsCustom ? RuleSets.Universal : curr;
        await Shell.Current.GoToAsync("RuleEditorPage");
    }

    // =====================================================================
    //  ИСПРАВЛЕНИЕ
    // =====================================================================

    private async void OnPrimaryTapped(object? sender, TappedEventArgs e)
    {
        StopChoosePulse();
        HapticHelper.Click();
        if (_busy)
            return;

        var fixable = _issues.Where(i => i.CanFix).ToList();
        if (_workPath is not null && fixable.Count > 0)
            await FixAsync(fixable);
        else
            await PickAsync();
    }

    private async Task FixAsync(IReadOnlyList<CheckIssue> targets)
    {
        if (_busy || _workPath is null || targets.Count == 0)
            return;

        SetBusy(true);
        try {
            var path = _workPath;
            var rules = CurrentRules;
            var kinds = targets.Select(t => t.Kind).Distinct().ToList();

            int changes = await Task.Run(() => DocumentChecker.Fix(path, rules, kinds));
            var after = await Task.Run(() => DocumentChecker.Check(path, rules));

            // Исправлено то, чего больше нет в списке замечаний
            var stillThere = after.Select(a => a.Kind).ToHashSet();
            foreach (var t in targets.Where(t => !stillThere.Contains(t.Kind)))
                if (_fixedIssues.All(f => f.Kind != t.Kind))
                    _fixedIssues.Add(t);

            _issues = after;
            if (changes > 0)
            {
                HapticHelper.Success();
                Toast.Show($"Исправлено изменений: {changes}", "sparkles");
            }
            _status = changes > 0
                ? $"Исправлено: {RuFormat.Count(changes, "изменение", "изменения", "изменений")}. Остальное нужно поправить вручную."
                : "Автоматически здесь ничего не изменилось. Поправьте это вручную в Word.";
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
            await Dialogs.AlertAsync("Не удалось исправить", ex.Message, DialogKind.Danger);
        } finally {
            SetBusy(false);
            UpdateUi();
        }
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
        _busy = busy;
        Loader.IsVisible = busy;
        Loader.IsRunning = busy;
    }

    private void UpdateUi()
    {
        bool hasFile = _workPath is not null;

        var desc = CurrentRules.Description;
        if (!string.IsNullOrWhiteSpace(desc)) {
            RuleNote.Text = desc;
            RuleNote.IsVisible = true;
        } else {
            RuleNote.IsVisible = false;
        }

        DropzoneBorder.IsVisible = !hasFile;
        FileSelectedCard.IsVisible = hasFile;
        EmptyPanel.IsVisible = !hasFile;
        ResultPanel.IsVisible = hasFile;

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

        var fixable = _issues.Where(i => i.CanFix).ToList();
        var manual = _issues.Where(i => !i.CanFix).ToList();

        // Главная кнопка: исправить (если есть что), иначе снова выбрать
        if (fixable.Count > 0) {
            PrimaryLabel.Text = $"Исправить всё ({fixable.Count})";
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

        // Плашка-сводка инспектора
        if (_issues.Count == 0 && _fixedIssues.Count == 0)
        {
            SummaryBox.BackgroundColor = ThemeService.IsDark ? Color.FromArgb("#064E3B") : Color.FromArgb("#ECFDF5");
            SummaryBox.Stroke = new SolidColorBrush(Color.FromArgb(ThemeService.IsDark ? "#059669" : "#10B981"));
            SummaryIconCtrl.Icon = "shield";
            SummaryIconCtrl.IconColor = ThemeService.IsDark ? "#34D399" : "#059669";
            SummaryTitle.Text = "Документ идеален по ГОСТ!";
            SummaryTitle.TextColor = Color.FromArgb(ThemeService.IsDark ? "#6EE7B7" : "#065F46");
            SummarySubtitle.Text = "Все параметры оформления (поля страницы, гарнитура, интервалы) полностью соответствуют выбранным правилам.";
        }
        else if (_issues.Count == 0)
        {
            SummaryBox.BackgroundColor = ThemeService.IsDark ? Color.FromArgb("#064E3B") : Color.FromArgb("#ECFDF5");
            SummaryBox.Stroke = new SolidColorBrush(Color.FromArgb(ThemeService.IsDark ? "#059669" : "#10B981"));
            SummaryIconCtrl.Icon = "check";
            SummaryIconCtrl.IconColor = ThemeService.IsDark ? "#34D399" : "#059669";
            SummaryTitle.Text = "Все замечания устранены!";
            SummaryTitle.TextColor = Color.FromArgb(ThemeService.IsDark ? "#6EE7B7" : "#065F46");
            SummarySubtitle.Text = "Документ исправлен по стандарту ГОСТ и готов к сохранению или экспорту.";
        }
        else
        {
            SummaryBox.BackgroundColor = ThemeService.IsDark ? Color.FromArgb("#451A03") : Color.FromArgb("#FEF2F2");
            SummaryBox.Stroke = new SolidColorBrush(Color.FromArgb(ThemeService.IsDark ? "#B45309" : "#F87171"));
            SummaryIconCtrl.Icon = "alerttriangle";
            SummaryIconCtrl.IconColor = ThemeService.IsDark ? "#FBBF24" : "#DC2626";
            SummaryTitle.Text = $"Найдено {_issues.Count} замечаний ({fixable.Count} можно исправить)";
            SummaryTitle.TextColor = Color.FromArgb(ThemeService.IsDark ? "#FDE68A" : "#991B1B");
            SummarySubtitle.Text = fixable.Count > 0 
                ? "Нажмите «Исправить всё», чтобы применить автоматические исправления ГОСТ." 
                : "Все замечания требуют ручной правки в текстовом редакторе Word.";
        }

        // Список замечаний с группировкой в аккордеоны по категориям важности
        IssuesHost.Children.Clear();

        var importantIssues = _issues.Where(i => i.Severity == IssueSeverity.Important).ToList();
        var warningIssues = _issues.Where(i => i.Severity != IssueSeverity.Important).ToList();

        if (importantIssues.Count > 0)
        {
            var cards = importantIssues.Select(i => BuildIssueCard(i, isFixed: false));
            IssuesHost.Children.Add(BuildAccordionGroup("Важные замечания", Icons.AlertTriangle, importantIssues.Count, "#EF4444", cards, isExpandedDefault: true));
        }

        if (warningIssues.Count > 0)
        {
            var cards = warningIssues.Select(i => BuildIssueCard(i, isFixed: false));
            IssuesHost.Children.Add(BuildAccordionGroup("Желательные рекомендации", Icons.Info, warningIssues.Count, "#F59E0B", cards, isExpandedDefault: true));
        }

        if (_fixedIssues.Count > 0)
        {
            var cards = _fixedIssues.Select(i => BuildIssueCard(i, isFixed: true));
            IssuesHost.Children.Add(BuildAccordionGroup("Исправлено автоматически", Icons.Check, _fixedIssues.Count, "#10B981", cards, isExpandedDefault: false));
        }
    }

    private View BuildAccordionGroup(string title, string iconData, int count, string badgeColorHex, IEnumerable<View> itemViews, bool isExpandedDefault = true)
    {
        var container = new VerticalStackLayout
        {
            Spacing = 8,
            IsVisible = isExpandedDefault,
        };

        foreach (var item in itemViews)
            container.Children.Add(item);

        var chevron = new MauiPath
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(isExpandedDefault ? Icons.ChevronDown : Icons.ChevronRight)!,
            WidthRequest = 14,
            HeightRequest = 14,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            VerticalOptions = LayoutOptions.Center,
        };
        chevron.SetDynamicResource(MauiPath.StrokeProperty, "TextSecondary");

        var iconPath = new MauiPath
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(iconData)!,
            WidthRequest = 16,
            HeightRequest = 16,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            VerticalOptions = LayoutOptions.Center,
            Stroke = Color.FromArgb(badgeColorHex),
        };

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var badgeText = new Label
        {
            Text = count.ToString(),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(badgeColorHex),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var badge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(8, 2),
            BackgroundColor = Color.FromArgb(badgeColorHex).WithAlpha(0.16f),
            VerticalOptions = LayoutOptions.Center,
            Content = badgeText,
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center,
        };

        headerGrid.Children.Add(iconPath);
        Grid.SetColumn(iconPath, 0);

        headerGrid.Children.Add(titleLabel);
        Grid.SetColumn(titleLabel, 1);

        headerGrid.Children.Add(badge);
        Grid.SetColumn(badge, 2);

        headerGrid.Children.Add(chevron);
        Grid.SetColumn(chevron, 3);

        var headerCard = new Border
        {
            StyleClass = new[] { "HoverCard" },
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(14, 11),
        };
        headerCard.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        headerCard.SetDynamicResource(Border.StrokeProperty, "CardStroke");
        headerCard.Content = headerGrid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            HapticHelper.Click();
            container.IsVisible = !container.IsVisible;
            chevron.Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(container.IsVisible ? Icons.ChevronDown : Icons.ChevronRight)!;
        };
        headerCard.GestureRecognizers.Add(tap);

        return new VerticalStackLayout
        {
            Spacing = 8,
            Children = { headerCard, container },
        };
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

    private static View BuildGroupHeader(string title, string icon, int count, string badgeColorHex)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 8,
            Margin = new Thickness(2, 6, 2, 2),
            VerticalOptions = LayoutOptions.Center,
        };

        var iconLabel = new Label { Text = icon, FontSize = 14, VerticalOptions = LayoutOptions.Center };
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var badgeText = new Label
        {
            Text = count.ToString(),
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(badgeColorHex),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        var badge = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(8, 2),
            BackgroundColor = Color.FromArgb(badgeColorHex).WithAlpha(0.16f),
            VerticalOptions = LayoutOptions.Center,
            Content = badgeText,
        };

        row.Children.Add(iconLabel);
        Grid.SetColumn(iconLabel, 0);

        row.Children.Add(titleLabel);
        Grid.SetColumn(titleLabel, 1);

        row.Children.Add(badge);
        Grid.SetColumn(badge, 2);

        return row;
    }

    private View BuildIssueCard(CheckIssue issue, bool isFixed)
    {
        var card = new Border {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(14, 12),
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        card.SetDynamicResource(Border.StrokeProperty, "CardStroke");

        var grid = new Grid {
            ColumnDefinitions = new ColumnDefinitionCollection {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
        };

        // Точка важности (или зелёная галка для исправленных)
        View dot = BuildDot(isFixed, issue.Severity);
        grid.Children.Add(dot);
        Grid.SetColumn(dot, 0);

        // Текст
        var textStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

        var title = new Label {
            Text = issue.Title,
            FontSize = 14,
            FontAttributes = isFixed ? FontAttributes.None : FontAttributes.Bold,
            TextDecorations = isFixed ? TextDecorations.Strikethrough : TextDecorations.None,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        title.SetDynamicResource(Label.TextColorProperty, isFixed ? "TextSecondary" : "TextPrimary");
        textStack.Children.Add(title);

        var where = new Label {
            Text = issue.Where,
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        where.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        textStack.Children.Add(where);

        grid.Children.Add(textStack);
        Grid.SetColumn(textStack, 1);

        // Кнопка или бейдж справа
        View right;
        if (isFixed)
            right = BuildDoneTag();
        else if (issue.CanFix)
            right = BuildFixButton(issue);
        else
            right = BuildManualTag();

        grid.Children.Add(right);
        Grid.SetColumn(right, 2);

        card.Content = grid;
        return card;
    }

    private static View BuildDot(bool isFixed, IssueSeverity severity)
    {
        if (isFixed)
            return Icons.CreatePath(Icons.Check, size: 14, strokeColorResource: "BrandGreen");

        var colorHex = severity == IssueSeverity.Important ? "#EF4444" : "#F59E0B";
        return new Border
        {
            WidthRequest = 8,
            HeightRequest = 8,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            BackgroundColor = Color.FromArgb(colorHex),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(4, 0),
        };
    }

    private View BuildFixButton(CheckIssue issue)
    {
        var label = new Label {
            Text = "Исправить",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFFFFF"),
        };

        var button = new Border {
            StyleClass = new[] { "HoverGreen" },
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 7),
            VerticalOptions = LayoutOptions.Center,
            Content = label,
        };
        SemanticProperties.SetDescription(button, $"Исправить: {issue.Title}");

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await FixAsync(new[] { issue });
        button.GestureRecognizers.Add(tap);
        return button;
    }

    private static View BuildManualTag()
    {
        var label = new Label { Text = "Вручную", FontSize = 12, FontAttributes = FontAttributes.Bold };
        label.SetDynamicResource(Label.TextColorProperty, "TextSecondary");

        var tag = new Border {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10, 5),
            VerticalOptions = LayoutOptions.Center,
            Content = label,
        };
        tag.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardHoverBg");
        return tag;
    }

    private static View BuildDoneTag()
    {
        var icon = Icons.CreatePath(Icons.Check, size: 12, strokeColorResource: "BrandGreen");
        var label = new Label {
            Text = "Исправлено",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetDynamicResource(Label.TextColorProperty, "BrandGreen");

        return new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { icon, label },
        };
    }
}
