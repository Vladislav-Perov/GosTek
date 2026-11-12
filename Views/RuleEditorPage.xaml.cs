/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Логика конструктора правил ГОСТ, валидация полей, интерактивный предпросмотр А4 «Было / Стало»
 * СОДЕРЖИМОЕ: RuleEditorPage
 * ЗАВИСИМОСТИ: GosTek.Models.RuleSet, GosTek.Services.CustomRulesService, GosTek.Services.RuFormat
 * ==============================================================================
 */
using GosTek.Models;
using GosTek.Services;

namespace GosTek.Views;

public partial class RuleEditorPage : ContentPage
{
    private static readonly string[] AvailableFonts = { "Times New Roman", "Arial", "Calibri", "PT Sans" };
    private static readonly double[] AvailableSpacings = { 1.0, 1.15, 1.5, 2.0 };
    private static readonly (string Name, string Value)[] Alignments = {
        ("По ширине (Justify)", "Justify"),
        ("По левому краю (Left)", "Left"),
        ("По центру (Center)", "Center"),
        ("По правому краю (Right)", "Right"),
    };

    public static RuleSet? TargetRule { get; set; }
    public static RuleSet? BaselineRule { get; set; }

    private RuleSet? _editingRule;

    public RuleEditorPage()
    {
        InitializeComponent();
        InitPickers();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        Shell.SetTabBarIsVisible(this, false);
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Disabled;
            shell.FlyoutWidth = 0;
        }
#endif
        _editingRule = TargetRule;
        LoadRuleData(_editingRule);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Locked;
            shell.FlyoutWidth = Preferences.Get("rail_collapsed", false) ? 64 : 220;
        }
#endif
    }

    private void InitPickers()
    {
        FontPicker.ItemsSource = AvailableFonts;
        SpacingPicker.ItemsSource = AvailableSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        AlignPicker.ItemsSource = Alignments.Select(a => a.Name).ToList();
    }

    private void LoadRuleData(RuleSet? rule)
    {
        var r = rule ?? RuleSets.Universal;

        PageTitleLabel.Text = rule is { IsCustom: true }
            ? "Редактирование правила"
            : (rule != null ? $"Правило на основе «{rule.Title}»" : "Новое правило ГОСТ");

        TitleEntry.Text = rule is { IsCustom: true } ? rule.Title : (rule != null ? $"{rule.Title} (копия)" : "");
        CategoryEntry.Text = r.Category ?? "";
        DescriptionEntry.Text = r.Description ?? "";

        LeftMarginEntry.Text = RuFormat.Double(r.Left.Cm);
        RightMarginEntry.Text = RuFormat.Double(r.Right.Cm);
        TopMarginEntry.Text = RuFormat.Double(r.Top.Cm);
        BottomMarginEntry.Text = RuFormat.Double(r.Bottom.Cm);

        int fontIdx = Array.FindIndex(AvailableFonts, f => f.Equals(r.FontName, StringComparison.OrdinalIgnoreCase));
        FontPicker.SelectedIndex = fontIdx >= 0 ? fontIdx : 0;

        FontSizeEntry.Text = RuFormat.Double(r.FontSizePt);

        int spacingIdx = Array.FindIndex(AvailableSpacings, s => Math.Abs(s - r.LineSpacing) < 0.05);
        SpacingPicker.SelectedIndex = spacingIdx >= 0 ? spacingIdx : 2; // по умолчанию 1.5

        FirstLineIndentEntry.Text = RuFormat.Double(r.FirstLineIndentCm);

        int alignIdx = Array.FindIndex(Alignments, a => a.Value.Equals(r.Alignment, StringComparison.OrdinalIgnoreCase));
        AlignPicker.SelectedIndex = alignIdx >= 0 ? alignIdx : 0;

        RequirePageNumbersSwitch.IsToggled = r.RequirePageNumbers;

        DeleteButton.IsVisible = rule is { IsCustom: true };
    }

    private RuleSet BuildRuleFromForm()
    {
        double left = RuFormat.ParseDouble(LeftMarginEntry.Text, 3.0);
        double right = RuFormat.ParseDouble(RightMarginEntry.Text, 1.0);
        double top = RuFormat.ParseDouble(TopMarginEntry.Text, 2.0);
        double bottom = RuFormat.ParseDouble(BottomMarginEntry.Text, 2.0);

        string font = FontPicker.SelectedIndex >= 0 && FontPicker.SelectedIndex < AvailableFonts.Length
            ? AvailableFonts[FontPicker.SelectedIndex]
            : "Times New Roman";

        double fontSize = RuFormat.ParseDouble(FontSizeEntry.Text, 14.0);

        double spacing = SpacingPicker.SelectedIndex >= 0 && SpacingPicker.SelectedIndex < AvailableSpacings.Length
            ? AvailableSpacings[SpacingPicker.SelectedIndex]
            : 1.5;

        double indent = RuFormat.ParseDouble(FirstLineIndentEntry.Text, 1.25);

        string alignment = AlignPicker.SelectedIndex >= 0 && AlignPicker.SelectedIndex < Alignments.Length
            ? Alignments[AlignPicker.SelectedIndex].Value
            : "Justify";

        string id = _editingRule is { IsCustom: true } ? _editingRule.Id : Guid.NewGuid().ToString("N")[..8];
        string title = TitleEntry.Text?.Trim() ?? "Пользовательское правило";
        string? category = string.IsNullOrWhiteSpace(CategoryEntry.Text) ? null : CategoryEntry.Text.Trim();
        string? description = string.IsNullOrWhiteSpace(DescriptionEntry.Text) ? null : DescriptionEntry.Text.Trim();

        return new RuleSet(
            Id: id,
            Title: title,
            Left: new MarginRule(Math.Max(0.5, left), true),
            Right: new MarginRule(Math.Max(0.5, right), false),
            Top: new MarginRule(Math.Max(0.5, top), false),
            Bottom: new MarginRule(Math.Max(0.5, bottom), false),
            FontName: font,
            FontSizePt: Math.Max(6.0, fontSize),
            LineSpacing: Math.Max(0.8, spacing),
            RequirePageNumbers: RequirePageNumbersSwitch.IsToggled,
            RequiredSections: _editingRule?.RequiredSections ?? Array.Empty<SectionRule>(),
            SectionsMinParagraphs: _editingRule?.SectionsMinParagraphs ?? 100,
            FirstLineIndentCm: Math.Max(0.0, indent),
            Alignment: alignment,
            Category: category,
            IsCustom: true,
            Description: description);
    }

    // =====================================================================
    //  ПРЕДПРОСМОТР (БЫЛО / СТАЛО)
    // =====================================================================

    private void OnPreviewTapped(object? sender, EventArgs e)
    {
        var currentRule = BuildRuleFromForm();
        var baseline = BaselineRule ?? RuleSets.Universal;

        RenderPreviewSheet(BeforeSheet, baseline, BeforeBadgeLabel, "БЫЛО");
        RenderPreviewSheet(AfterSheet, currentRule, AfterBadgeLabel, "СТАЛО");

        PreviewOverlay.IsVisible = true;
    }

    private void OnClosePreviewTapped(object? sender, EventArgs e)
    {
        PreviewOverlay.IsVisible = false;
    }

    private void RenderPreviewSheet(Border sheetBorder, RuleSet rule, Label badgeLabel, string badgePrefix)
    {
        badgeLabel.Text = $"{badgePrefix}: поля {rule.Left.Cm:0.#}/{rule.Right.Cm:0.#}/{rule.Top.Cm:0.#}/{rule.Bottom.Cm:0.#} см, {rule.FontName} {rule.FontSizePt:0.#}pt, инт. {rule.LineSpacing:0.##}";

        double w = 260.0;
        double h = w * 297.0 / 210.0; // Пропорции листа А4
        sheetBorder.WidthRequest = w;
        sheetBorder.HeightRequest = h;

        double cmScale = w / 21.0;
        double ptScale = w / 595.0;

        double padLeft = Math.Max(4, rule.Left.Cm * cmScale);
        double padRight = Math.Max(4, rule.Right.Cm * cmScale);
        double padTop = Math.Max(4, rule.Top.Cm * cmScale);
        double padBottom = Math.Max(4, rule.Bottom.Cm * cmScale);

        sheetBorder.Padding = new Thickness(padLeft, padTop, padRight, padBottom);

        double bodyFontSize = Math.Clamp(rule.FontSizePt * ptScale, 6.0, 11.0);
        double titleFontSize = Math.Clamp((rule.FontSizePt + 2) * ptScale, 7.0, 13.0);
        double indentPx = Math.Max(0, rule.FirstLineIndentCm * cmScale);

        var layout = new Grid {
            RowDefinitions = new RowDefinitionCollection {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };

        var contentStack = new VerticalStackLayout { Spacing = bodyFontSize * 0.4 };

        var titleLabel = new Label {
            Text = "ОТЧЕТ О ВЫПОЛНЕНИИ РАБОТ",
            FontSize = titleFontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black,
            HorizontalTextAlignment = TextAlignment.Center,
            FontFamily = rule.FontName,
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(titleLabel);

        var align = rule.Alignment.ToLowerInvariant() switch {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.End,
            _ => TextAlignment.Start,
        };

        var p1 = new Label {
            Text = "Настоящий отчет подготовлен в строгом соответствии с требованиями государственного стандарта. Параметры страницы и абзацев проверяются программным аудитором GosTek.",
            FontSize = bodyFontSize,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = align,
            FontFamily = rule.FontName,
            LineHeight = Math.Clamp(rule.LineSpacing, 0.9, 2.0),
            Margin = new Thickness(indentPx, 0, 0, 0),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(p1);

        var p2 = new Label {
            Text = "Все элементы оформления документа, включая гарнитуру шрифта, кегль, поля страницы и красную строку, приведены к согласованному стандарту ГОСТ.",
            FontSize = bodyFontSize,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = align,
            FontFamily = rule.FontName,
            LineHeight = Math.Clamp(rule.LineSpacing, 0.9, 2.0),
            Margin = new Thickness(indentPx, 0, 0, 0),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(p2);

        layout.Children.Add(contentStack);
        Grid.SetRow(contentStack, 0);

        if (rule.RequirePageNumbers) {
            var pageNum = new Label {
                Text = "— 1 —",
                FontSize = Math.Max(5.5, bodyFontSize - 1),
                TextColor = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.End,
                FontFamily = rule.FontName,
            };
            layout.Children.Add(pageNum);
            Grid.SetRow(pageNum, 1);
        }

        sheetBorder.Content = layout;
    }

    // =====================================================================
    //  СОХРАНЕНИЕ И УДАЛЕНИЕ
    // =====================================================================

    private async void OnSaveTapped(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleEntry.Text)) {
            await Dialogs.AlertAsync("Укажите название", "Пожалуйста, введите название правила оформления.", DialogKind.Warning);
            TitleEntry.Focus();
            return;
        }

        try {
            var rule = BuildRuleFromForm();
            bool success = await CustomRulesService.Instance.SaveRuleAsync(rule);
            if (success) {
                await Dialogs.AlertAsync("Правило сохранено", $"Правило «{rule.Title}» успешно сохранено.", DialogKind.Success);
                await Shell.Current.GoToAsync("..");
            } else {
                await Dialogs.AlertAsync("Ошибка сохранения", "Не удалось сохранить правило. Попробуйте снова.", DialogKind.Danger);
            }
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Ошибка сохранения", ex.Message, DialogKind.Danger);
        }
    }

    private async void OnDeleteTapped(object? sender, EventArgs e)
    {
        if (_editingRule == null || !_editingRule.IsCustom)
            return;

        bool confirm = await Dialogs.ConfirmAsync(
            "Удаление правила",
            $"Вы действительно хотите удалить правило «{_editingRule.Title}»?",
            "Удалить",
            DialogKind.Delete,
            "Отмена",
            danger: true);

        if (!confirm)
            return;

        try {
            bool deleted = await CustomRulesService.Instance.DeleteRuleAsync(_editingRule.Id);
            if (deleted) {
                await Dialogs.AlertAsync("Правило удалено", "Правило успешно удалено из хранилища.", DialogKind.Success);
                await Shell.Current.GoToAsync("..");
            }
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Ошибка удаления", ex.Message, DialogKind.Danger);
        }
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
