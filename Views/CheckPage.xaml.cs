using GosTek.Models;
using GosTek.Services;
using Microsoft.Maui.Controls.Shapes;

// В Shapes есть одноимённый Path
using Path = System.IO.Path;

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

    private List<CheckIssue> _issues = new();                 // что ещё не так
    private readonly List<CheckIssue> _fixedIssues = new();   // что уже исправили (показываем зачёркнутыми)
    private string? _status;

    private bool _busy;

    public CheckPage()
    {
        InitializeComponent();

        RuleChips.ShowArrows = true;
        RuleChips.SetItems(RuleSets.Presets.Select(p => p.Title).ToList());
        RuleChips.SelectionChanged += OnRuleChanged;

        UpdateUi();
    }

    private RuleSet CurrentRules
    {
        get {
            int i = RuleChips.SelectedIndex;
            return i >= 0 && i < RuleSets.Presets.Count ? RuleSets.Presets[i].Rules : RuleSets.Universal;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
    }

    // =====================================================================
    //  ВЫБОР ФАЙЛА
    // =====================================================================

    private async void OnPickTapped(object? sender, TappedEventArgs e) => await PickAsync();

    private async Task PickAsync()
    {
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

            var rules = CurrentRules;
            var issues = await Task.Run(() => DocumentChecker.Check(work, rules));
            var info = await Task.Run(() => DocumentChecker.ReadInfo(work));

            _workPath = work;
            _sourceName = picked.FileName;
            _sourceSize = new FileInfo(work).Length;
            _info = info;
            _issues = issues;
            _fixedIssues.Clear();
            _status = null;
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

    // =====================================================================
    //  ИСПРАВЛЕНИЕ
    // =====================================================================

    private async void OnPrimaryTapped(object? sender, TappedEventArgs e)
    {
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

        // Пока для набора нет своих правил, предупреждаем (все чипы сейчас используют Universal)
        RuleNote.IsVisible = RuleChips.SelectedIndex > 0;

        EmptyPanel.IsVisible = !hasFile;
        ResultPanel.IsVisible = hasFile;

        if (!hasFile) {
            FileNameLabel.Text = "Документ не выбран";
            FileInfoLabel.Text = "Подойдёт файл Word (.docx)";
            PickLabel.Text = "Выбрать";
            PrimaryLabel.Text = "Выбрать документ";
            SaveButton.IsVisible = false;
            Grid.SetColumnSpan(PrimaryButton, 2);
            return;
        }

        FileNameLabel.Text = _sourceName;
        FileInfoLabel.Text = BuildInfoText();
        PickLabel.Text = "Другой";

        StatusLabel.IsVisible = !string.IsNullOrEmpty(_status);
        StatusLabel.Text = _status ?? "";

        BuildSummary();
        BuildIssues();

        int fixable = _issues.Count(i => i.CanFix);
        PrimaryLabel.Text = fixable > 0
            ? $"Исправить всё, что можно ({fixable})"
            : "Выбрать другой документ";

        // «Сохранить копию» нужна, только если что-то исправили
        SaveButton.IsVisible = _fixedIssues.Count > 0;
        Grid.SetColumnSpan(PrimaryButton, SaveButton.IsVisible ? 1 : 2);
    }

    private string BuildInfoText()
    {
        var parts = new List<string> { RuFormat.FileSize(_sourceSize) };

        if (_info.Pages is { } pages)
            parts.Add(RuFormat.Count(pages, "страница", "страницы", "страниц"));
        if (_info.Paragraphs > 0)
            parts.Add(RuFormat.Count(_info.Paragraphs, "абзац", "абзаца", "абзацев"));

        return string.Join(" · ", parts);
    }

    // Плашка с итогом: красная (есть важные), жёлтая (только пожелания), зелёная (всё хорошо)
    private void BuildSummary()
    {
        int total = _issues.Count;
        int important = _issues.Count(i => i.Severity == IssueSeverity.Important);

        var formatted = new FormattedString();
        if (total == 0) {
            formatted.Spans.Add(new Span { Text = "Замечаний нет. Документ можно отправлять." });
            SummaryBox.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
        } else {
            formatted.Spans.Add(new Span { Text = "Осталось замечаний: " });
            formatted.Spans.Add(new Span { Text = total.ToString(), FontAttributes = FontAttributes.Bold });
            formatted.Spans.Add(new Span { Text = ", из них важных: " });
            formatted.Spans.Add(new Span { Text = important.ToString(), FontAttributes = FontAttributes.Bold });
            SummaryBox.SetDynamicResource(VisualElement.BackgroundColorProperty,
                important > 0 ? "BadSoftBg" : "WarnSoftBg");
        }

        SummaryLabel.FormattedText = formatted;
    }

    private void BuildIssues()
    {
        IssuesHost.Children.Clear();

        foreach (var issue in _issues)
            IssuesHost.Children.Add(BuildIssueCard(issue, done: false));

        foreach (var issue in _fixedIssues)
            IssuesHost.Children.Add(BuildIssueCard(issue, done: true));
    }

    // Карточка замечания: цветная точка, текст и справа «Исправить» / «Вручную» / «Исправлено»
    private View BuildIssueCard(CheckIssue issue, bool done)
    {
        var dot = new BoxView {
            WidthRequest = 10,
            HeightRequest = 10,
            CornerRadius = 5,
            VerticalOptions = LayoutOptions.Center,
        };
        dot.SetDynamicResource(BoxView.ColorProperty,
            issue.Severity == IssueSeverity.Important ? "BadColor" : "WarnColor");

        var title = new Label {
            Text = issue.Title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            TextDecorations = done ? TextDecorations.Strikethrough : TextDecorations.None,
        };
        title.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var where = new Label {
            Text = issue.Where,
            FontSize = 12.5,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        where.SetDynamicResource(Label.TextColorProperty, "TextSecondary");

        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        text.Children.Add(title);
        text.Children.Add(where);

        var grid = new Grid {
            ColumnDefinitions = {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 10,
        };
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);

        View action = done ? BuildDoneTag() : issue.CanFix ? BuildFixButton(issue) : BuildManualTag();
        Grid.SetColumn(action, 2);
        grid.Children.Add(action);

        var card = new Border {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12),
            Opacity = done ? 0.6 : 1,
            Content = grid,
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        card.SetDynamicResource(Border.StrokeProperty, "CardStroke");
        return card;
    }

    private View BuildFixButton(CheckIssue issue)
    {
        var label = new Label {
            Text = "Исправить",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#064E3B"),
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
        var label = new Label {
            Text = "✓ Исправлено",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        label.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
        return label;
    }
}
