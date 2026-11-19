/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Логика конструктора правил ГОСТ, валидация полей, двухстраничный живой предпросмотр А4 (Live Preview) с масштабированием
 * СОДЕРЖИМОЕ: RuleEditorPage, RenderSheet1, RenderSheet2, Zoom
 * ЗАВИСИМОСТИ: GosTek.Models.RuleSet, GosTek.Services.CustomRulesService, GosTek.Services.RuFormat
 * ==============================================================================
 */
using GosTek.Models;
using GosTek.Services;
using GosTek.Views.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

public partial class RuleEditorPage : ContentPage
{
    private static readonly string[] AvailableFonts = { "Times New Roman", "Arial", "Calibri", "PT Sans" };
    private static readonly double[] AvailableSpacings = { 1.0, 1.15, 1.5, 2.0 };
    private static readonly string[] AvailableCodeFonts = { "Consolas", "Courier New" };
    private static readonly double[] AvailableCodeSpacings = { 1.0, 1.15, 1.25, 1.5, 2.0 };
    private static readonly (string Name, string Value)[] Alignments = {
        ("По ширине (Justify)", "Justify"),
        ("По левому краю (Left)", "Left"),
        ("По центру (Center)", "Center"),
        ("По правому краю (Right)", "Right"),
    };
    private static readonly (string Name, string Value)[] ItemAlignments = {
        ("По центру (Center)", "Center"),
        ("По левому краю (Left)", "Left"),
        ("По правому краю (Right)", "Right"),
    };

    public static RuleSet? TargetRule { get; set; }
    public static RuleSet? BaselineRule { get; set; }
    public static string? LastSavedRuleId { get; set; }

    private RuleSet? _editingRule;
    private bool _isInitializing;
    private double _previewScale = 1.0;

    private bool _isSettingsVisible = true;
    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set
        {
            if (_isSettingsVisible != value)
            {
                _isSettingsVisible = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isPreviewVisible;
    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        set
        {
            if (_isPreviewVisible != value)
            {
                _isPreviewVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public RuleEditorPage()
    {
        InitializeComponent();
        BindingContext = this;
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
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            SwitchEditorTab(showPreview: false);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SettingsOverlay.Dismiss();
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Locked;
            shell.FlyoutWidth = global::GosTek.Views.AppTabBar.WindowsRailWidth;
        }
#endif
    }

    private void InitPickers()
    {
        FontPicker.ItemsSource = AvailableFonts;
        SpacingPicker.ItemsSource = AvailableSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        AlignPicker.ItemsSource = Alignments.Select(a => a.Name).ToList();

        TableFontPicker.ItemsSource = AvailableFonts;
        TableLineSpacingPicker.ItemsSource = AvailableCodeSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        TableCaptionAlignPicker.ItemsSource = ItemAlignments.Select(a => a.Name).ToList();
        TableCaptionLineSpacingPicker.ItemsSource = AvailableCodeSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();

        FigureAlignPicker.ItemsSource = ItemAlignments.Select(a => a.Name).ToList();
        CaptionAlignPicker.ItemsSource = ItemAlignments.Select(a => a.Name).ToList();
        CaptionLineSpacingPicker.ItemsSource = AvailableCodeSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();

        CodeFontPicker.ItemsSource = AvailableCodeFonts;
        CodeLineSpacingPicker.ItemsSource = AvailableCodeSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();

        HeadingFontPicker.ItemsSource = AvailableFonts;
        HeadingAlignPicker.ItemsSource = ItemAlignments.Select(a => a.Name).ToList();
        HeadingLineSpacingPicker.ItemsSource = AvailableCodeSpacings.Select(s => s.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)).ToList();
    }

    // =====================================================================
    //  МАСШТАБИРОВАНИЕ И ЖИВОЙ ПРЕДПРОСМОТР (LIVE PREVIEW)
    // =====================================================================

    private void OnZoomInTapped(object? sender, EventArgs e)
    {
        if (_previewScale < 2.0)
        {
            _previewScale = Math.Min(2.0, Math.Round(_previewScale + 0.15, 2));
            UpdateZoomLabel();
            UpdateLivePreview();
        }
    }

    private void OnZoomOutTapped(object? sender, EventArgs e)
    {
        if (_previewScale > 0.6)
        {
            _previewScale = Math.Max(0.6, Math.Round(_previewScale - 0.15, 2));
            UpdateZoomLabel();
            UpdateLivePreview();
        }
    }

    private void OnPreviewPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status == GestureStatus.Running)
        {
            double newScale = Math.Clamp(_previewScale * e.Scale, 0.6, 2.0);
            if (Math.Abs(newScale - _previewScale) > 0.04)
            {
                _previewScale = Math.Round(newScale, 2);
                UpdateZoomLabel();
                UpdateLivePreview();
            }
        }
    }

    private void UpdateZoomLabel()
    {
        if (ZoomPercentLabel != null)
            ZoomPercentLabel.Text = $"{Math.Round(_previewScale * 100)}%";
    }

    private void OnSettingChanged(object? sender, EventArgs e)
    {
        if (_isInitializing)
            return;

        UpdateLivePreview();
    }

    private void UpdateLivePreview()
    {
        try
        {
            var currentRule = BuildRuleFromForm();
            RenderDocumentSheet(DocumentSheetBorder, currentRule);

            AfterBadgeLabel.Text = $"ГОСТ: поля {currentRule.Left.Cm:0.#}/{currentRule.Right.Cm:0.#}/{currentRule.Top.Cm:0.#}/{currentRule.Bottom.Cm:0.#} см, {currentRule.FontName} {currentRule.FontSizePt:0.#}pt, инт. {currentRule.LineSpacing:0.##}";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LivePreview] {ex.Message}");
        }
    }

    private void LoadRuleData(RuleSet? rule)
    {
        _isInitializing = true;
        try
        {
            var r = rule ?? RuleSets.MasterRule;
            bool isMaster = r.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
                            r.Id.Equals("universal", StringComparison.OrdinalIgnoreCase);

            PageTitleLabel.Text = isMaster ? "Настройка «Основной ГОСТ»" : "Настройка профиля ГОСТ";
            TitleEntry.Text = isMaster ? "Основной ГОСТ" : r.Title;
            TitleEntry.IsReadOnly = isMaster;
            TitleEntry.Opacity = isMaster ? 0.75 : 1.0;

            if (DeleteButton != null && SaveButton != null)
            {
                DeleteButton.IsVisible = !isMaster;
                if (isMaster)
                {
                    Grid.SetColumn(SaveButton, 0);
                    Grid.SetColumnSpan(SaveButton, 2);
                }
                else
                {
                    Grid.SetColumn(SaveButton, 1);
                    Grid.SetColumnSpan(SaveButton, 1);
                }
            }

            // Поля страницы
            LeftMarginEntry.Text = RuFormat.Double(r.Left.Cm);
            RightMarginEntry.Text = RuFormat.Double(r.Right.Cm);
            TopMarginEntry.Text = RuFormat.Double(r.Top.Cm);
            BottomMarginEntry.Text = RuFormat.Double(r.Bottom.Cm);

            // Основной текст
            int fontIdx = Array.FindIndex(AvailableFonts, f => f.Equals(r.FontName, StringComparison.OrdinalIgnoreCase));
            FontPicker.SelectedIndex = fontIdx >= 0 ? fontIdx : 0;

            FontSizeEntry.Text = RuFormat.Double(r.FontSizePt);

            int spIdx = Array.FindIndex(AvailableSpacings, s => Math.Abs(s - r.LineSpacing) < 0.05);
            SpacingPicker.SelectedIndex = spIdx >= 0 ? spIdx : 2; // Default 1.5

            FirstLineIndentEntry.Text = RuFormat.Double(r.FirstLineIndentCm);

            int alignIdx = Array.FindIndex(Alignments, a => a.Value.Equals(r.Alignment, StringComparison.OrdinalIgnoreCase));
            AlignPicker.SelectedIndex = alignIdx >= 0 ? alignIdx : 0;

            RequirePageNumbersSwitch.IsToggled = r.RequirePageNumbers;

            // Оформление таблиц
            FormatTablesSwitch.IsToggled = r.CheckTables;
            int tableFontIdx = Array.FindIndex(AvailableFonts, f => f.Equals(r.TableFontFamily, StringComparison.OrdinalIgnoreCase));
            TableFontPicker.SelectedIndex = tableFontIdx >= 0 ? tableFontIdx : 0;
            TableFontSizeEntry.Text = RuFormat.Double(r.TableFontSizePt);
            int tableSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - r.TableLineSpacing) < 0.05);
            TableLineSpacingPicker.SelectedIndex = tableSpIdx >= 0 ? tableSpIdx : 0; // Default 1.0
            TableNoIndentSwitch.IsToggled = r.TableRequireNoIndent;

            // Подпись таблицы
            int tCapAlignIdx = Array.FindIndex(ItemAlignments, a => a.Value.Equals(r.TableCaptionAlignment, StringComparison.OrdinalIgnoreCase));
            TableCaptionAlignPicker.SelectedIndex = tCapAlignIdx >= 0 ? tCapAlignIdx : 1; // Default Left (Index 1)
            TableCaptionSpacingBeforeEntry.Text = RuFormat.Double(r.TableCaptionSpacingBeforePt);
            TableCaptionSpacingAfterEntry.Text = RuFormat.Double(r.TableCaptionSpacingAfterPt);
            int tCapSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - r.TableCaptionLineSpacing) < 0.05);
            TableCaptionLineSpacingPicker.SelectedIndex = tCapSpIdx >= 0 ? tCapSpIdx : 0;
            TableCaptionNoIndentSwitch.IsToggled = r.TableCaptionRequireNoIndent;

            // Рисунки и подписи
            FormatFiguresSwitch.IsToggled = r.FigureRequireNoIndent || r.CaptionRequireNoIndent;
            int figAlignIdx = Array.FindIndex(ItemAlignments, a => a.Value.Equals(r.FigureAlignment, StringComparison.OrdinalIgnoreCase));
            FigureAlignPicker.SelectedIndex = figAlignIdx >= 0 ? figAlignIdx : 0;
            FigureNoIndentSwitch.IsToggled = r.FigureRequireNoIndent;
            FigureSpacingBeforeEntry.Text = RuFormat.Double(r.FigureSpacingBeforePt);
            FigureSpacingAfterEntry.Text = RuFormat.Double(r.FigureSpacingAfterPt);

            int capAlignIdx = Array.FindIndex(ItemAlignments, a => a.Value.Equals(r.CaptionAlignment, StringComparison.OrdinalIgnoreCase));
            CaptionAlignPicker.SelectedIndex = capAlignIdx >= 0 ? capAlignIdx : 0;
            int capSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - r.CaptionLineSpacing) < 0.05);
            CaptionLineSpacingPicker.SelectedIndex = capSpIdx >= 0 ? capSpIdx : 0;
            CaptionSpacingBeforeEntry.Text = RuFormat.Double(r.CaptionSpacingBeforePt);
            CaptionSpacingAfterEntry.Text = RuFormat.Double(r.CaptionSpacingAfterPt);

            // Листинги кода
            FormatCodeSwitch.IsToggled = r.CodeRequireNoIndent;
            int codeFontIdx = Array.FindIndex(AvailableCodeFonts, f => f.Equals(r.CodeFontFamily, StringComparison.OrdinalIgnoreCase));
            CodeFontPicker.SelectedIndex = codeFontIdx >= 0 ? codeFontIdx : 0;
            CodeFontSizeEntry.Text = RuFormat.Double(r.CodeFontSizePt);
            int codeSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - r.CodeLineSpacing) < 0.05);
            CodeLineSpacingPicker.SelectedIndex = codeSpIdx >= 0 ? codeSpIdx : 0;
            CodeNoIndentSwitch.IsToggled = r.CodeRequireNoIndent;

            // Заголовки
            FormatHeadingsSwitch.IsToggled = r.HeadingRequireBold || r.HeadingRequireNoIndent || r.HeadingRemoveTrailingDot;
            int hFontIdx = Array.FindIndex(AvailableFonts, f => f.Equals(r.HeadingFontFamily, StringComparison.OrdinalIgnoreCase));
            HeadingFontPicker.SelectedIndex = hFontIdx >= 0 ? hFontIdx : 0;
            HeadingFontSizeEntry.Text = RuFormat.Double(r.HeadingFontSizePt);
            int hAlignIdx = Array.FindIndex(ItemAlignments, a => a.Value.Equals(r.HeadingAlignment, StringComparison.OrdinalIgnoreCase));
            HeadingAlignPicker.SelectedIndex = hAlignIdx >= 0 ? hAlignIdx : 1; // Default Left (Index 1)
            int hSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - r.HeadingLineSpacing) < 0.05);
            int defHSpIdx = Array.FindIndex(AvailableCodeSpacings, s => Math.Abs(s - 1.5) < 0.05);
            HeadingLineSpacingPicker.SelectedIndex = hSpIdx >= 0 ? hSpIdx : (defHSpIdx >= 0 ? defHSpIdx : 0);
            HeadingSpacingBeforeEntry.Text = RuFormat.Double(r.HeadingSpacingBeforePt);
            HeadingSpacingAfterEntry.Text = RuFormat.Double(r.HeadingSpacingAfterPt);
            HeadingRequireBoldSwitch.IsToggled = r.HeadingRequireBold;
            HeadingRequireNoIndentSwitch.IsToggled = r.HeadingRequireNoIndent;
            HeadingRemoveTrailingDotSwitch.IsToggled = r.HeadingRemoveTrailingDot;

            UpdateZoomLabel();
        }
        finally
        {
            _isInitializing = false;
        }

        UpdateLivePreview();
    }

    private static string GetSelectedFont(Picker? picker, string[] available, string fallback)
    {
        if (picker?.SelectedItem is string s && available.Contains(s, StringComparer.OrdinalIgnoreCase))
            return s;
        if (picker?.SelectedIndex is >= 0 and int idx && idx < available.Length)
            return available[idx];
        return fallback;
    }

    private static double GetSelectedSpacing(Picker? picker, double[] available, double fallback)
    {
        if (picker?.SelectedItem is string s)
        {
            double parsed = RuFormat.ParseDouble(s, -1);
            if (parsed > 0) return parsed;
        }
        if (picker?.SelectedIndex is >= 0 and int idx && idx < available.Length)
            return available[idx];
        return fallback;
    }

    private static string GetSelectedAlignment(Picker? picker, (string Name, string Value)[] alignments, string fallback)
    {
        if (picker?.SelectedItem is string s)
        {
            var match = alignments.FirstOrDefault(a => a.Name.Equals(s, StringComparison.OrdinalIgnoreCase) || a.Value.Equals(s, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null) return match.Value;
        }
        if (picker?.SelectedIndex is >= 0 and int idx && idx < alignments.Length)
            return alignments[idx].Value;
        return fallback;
    }

    private RuleSet BuildRuleFromForm()
    {
        double left = RuFormat.ParseDouble(LeftMarginEntry?.Text, 3.0);
        double right = RuFormat.ParseDouble(RightMarginEntry?.Text, 1.0);
        double top = RuFormat.ParseDouble(TopMarginEntry?.Text, 2.0);
        double bottom = RuFormat.ParseDouble(BottomMarginEntry?.Text, 2.0);

        string font = GetSelectedFont(FontPicker, AvailableFonts, "Times New Roman");
        double fontSize = RuFormat.ParseDouble(FontSizeEntry?.Text, 14.0);
        double spacing = GetSelectedSpacing(SpacingPicker, AvailableSpacings, 1.5);
        double indent = RuFormat.ParseDouble(FirstLineIndentEntry?.Text, 1.25);
        string alignment = GetSelectedAlignment(AlignPicker, Alignments, "Justify");

        bool isMaster = _editingRule != null &&
            (_editingRule.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
             _editingRule.Id.Equals("universal", StringComparison.OrdinalIgnoreCase));

        string id = isMaster ? RuleSets.MasterRuleId : (_editingRule?.Id ?? Guid.NewGuid().ToString("N")[..8]);
        string title = isMaster ? "Основной ГОСТ" : (TitleEntry?.Text?.Trim() ?? "Новое правило");
        if (string.IsNullOrWhiteSpace(title)) title = isMaster ? "Основной ГОСТ" : "Новое правило";
        string? category = _editingRule?.Category;
        string? description = isMaster ? "Стандартные требования ГОСТ 7.32 / ГОСТ 2.105" : _editingRule?.Description;

        bool tables = FormatTablesSwitch?.IsToggled ?? true;
        string tableFont = GetSelectedFont(TableFontPicker, AvailableFonts, "Times New Roman");
        double tableFontSize = RuFormat.ParseDouble(TableFontSizeEntry?.Text, 12.0);
        double tableLineSpacing = GetSelectedSpacing(TableLineSpacingPicker, AvailableCodeSpacings, 1.0);
        bool tableNoIndent = TableNoIndentSwitch?.IsToggled ?? true;

        string tCapAlign = GetSelectedAlignment(TableCaptionAlignPicker, ItemAlignments, "Left");
        double tCapBefore = RuFormat.ParseDouble(TableCaptionSpacingBeforeEntry?.Text, 6.0);
        double tCapAfter = RuFormat.ParseDouble(TableCaptionSpacingAfterEntry?.Text, 3.0);
        double tCapLineSpacing = GetSelectedSpacing(TableCaptionLineSpacingPicker, AvailableCodeSpacings, 1.0);
        bool tCapNoIndent = TableCaptionNoIndentSwitch?.IsToggled ?? true;

        bool figs = FormatFiguresSwitch?.IsToggled ?? true;
        string figAlign = GetSelectedAlignment(FigureAlignPicker, ItemAlignments, "Center");
        double figBefore = RuFormat.ParseDouble(FigureSpacingBeforeEntry?.Text, 12.0);
        double figAfter = RuFormat.ParseDouble(FigureSpacingAfterEntry?.Text, 12.0);
        bool figNoIndent = FigureNoIndentSwitch?.IsToggled ?? true;

        string capAlign = GetSelectedAlignment(CaptionAlignPicker, ItemAlignments, "Center");
        double capBefore = RuFormat.ParseDouble(CaptionSpacingBeforeEntry?.Text, 0.0);
        double capAfter = RuFormat.ParseDouble(CaptionSpacingAfterEntry?.Text, 0.0);
        double capLineSpacing = GetSelectedSpacing(CaptionLineSpacingPicker, AvailableCodeSpacings, 1.0);

        bool code = FormatCodeSwitch?.IsToggled ?? true;
        string codeFont = GetSelectedFont(CodeFontPicker, AvailableCodeFonts, "Consolas");
        double codeFontSize = RuFormat.ParseDouble(CodeFontSizeEntry?.Text, 12.0);
        double codeLineSpacing = GetSelectedSpacing(CodeLineSpacingPicker, AvailableCodeSpacings, 1.0);
        bool codeNoIndent = CodeNoIndentSwitch?.IsToggled ?? true;

        bool heads = FormatHeadingsSwitch?.IsToggled ?? true;
        string headFont = GetSelectedFont(HeadingFontPicker, AvailableFonts, "Times New Roman");
        double headFontSize = RuFormat.ParseDouble(HeadingFontSizeEntry?.Text, 16.0);
        string headAlign = GetSelectedAlignment(HeadingAlignPicker, ItemAlignments, "Left");
        double headLineSpacing = GetSelectedSpacing(HeadingLineSpacingPicker, AvailableCodeSpacings, 1.5);
        double headBefore = RuFormat.ParseDouble(HeadingSpacingBeforeEntry?.Text, 12.0);
        double headAfter = RuFormat.ParseDouble(HeadingSpacingAfterEntry?.Text, 6.0);
        bool headBold = HeadingRequireBoldSwitch?.IsToggled ?? true;
        bool headNoIndent = HeadingRequireNoIndentSwitch?.IsToggled ?? true;
        bool headDot = HeadingRemoveTrailingDotSwitch?.IsToggled ?? true;

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
            RequirePageNumbers: RequirePageNumbersSwitch?.IsToggled ?? true,
            RequiredSections: _editingRule?.RequiredSections ?? Array.Empty<SectionRule>(),
            SectionsMinParagraphs: _editingRule?.SectionsMinParagraphs ?? 100,
            FirstLineIndentCm: Math.Max(0.0, indent),
            Alignment: alignment,
            Category: category,
            IsCustom: !isMaster,
            Description: description)
        {
            FigureAlignment = figAlign,
            FigureSpacingBeforePt = Math.Max(0.0, figBefore),
            FigureSpacingAfterPt = Math.Max(0.0, figAfter),
            FigureRequireNoIndent = figs && figNoIndent,

            CaptionAlignment = capAlign,
            CaptionSpacingBeforePt = Math.Max(0.0, capBefore),
            CaptionSpacingAfterPt = Math.Max(0.0, capAfter),
            CaptionLineSpacing = Math.Max(0.5, capLineSpacing),
            CaptionRequireNoIndent = figs && figNoIndent,

            CheckTables = tables,
            TableFontFamily = tableFont,
            TableFontSizePt = Math.Max(6.0, tableFontSize),
            TableLineSpacing = Math.Max(0.5, tableLineSpacing),
            TableRequireNoIndent = tables && tableNoIndent,

            TableCaptionAlignment = tCapAlign,
            TableCaptionSpacingBeforePt = Math.Max(0.0, tCapBefore),
            TableCaptionSpacingAfterPt = Math.Max(0.0, tCapAfter),
            TableCaptionLineSpacing = Math.Max(0.5, tCapLineSpacing),
            TableCaptionRequireNoIndent = tables && tCapNoIndent,

            CodeFontFamily = codeFont,
            CodeFontSizePt = Math.Max(6.0, codeFontSize),
            CodeLineSpacing = Math.Max(0.5, codeLineSpacing),
            CodeRequireNoIndent = code && codeNoIndent,

            HeadingFontFamily = headFont,
            HeadingFontSizePt = Math.Max(6.0, headFontSize),
            HeadingLineSpacing = Math.Max(0.5, headLineSpacing),
            HeadingAlignment = headAlign,
            HeadingSpacingBeforePt = Math.Max(0.0, headBefore),
            HeadingSpacingAfterPt = Math.Max(0.0, headAfter),
            HeadingRequireBold = heads && headBold,
            HeadingRequireNoIndent = heads && headNoIndent,
            HeadingRemoveTrailingDot = heads && headDot,
        };
    }

    // =====================================================================
    //  ЕДИНЫЙ ВИРТУАЛЬНЫЙ ЛИСТ ДОКУМЕНТА (LIVE PREVIEW)
    // =====================================================================

    /// <summary>
    /// Имитация первой строки (красной строки) через неразрывные пробелы.
    /// В MAUI у Label нет FirstLineIndent, поэтому Margin сдвигал весь текст.
    /// Префикс из \u00A0 сдвигает строго первую строку, а последующие строки переносятся к левому краю.
    /// </summary>
    private static string ApplyFirstLineIndent(string text, double indentCm)
    {
        if (indentCm <= 0.05)
            return text;

        int spaceCount = Math.Clamp((int)Math.Round(indentCm * 5.0), 1, 25);
        return new string('\u00A0', spaceCount) + text;
    }

    private void RenderDocumentSheet(Border sheetBorder, RuleSet rule)
    {
        double baseWidth = 580.0;
#if ANDROID
        double screenW = Application.Current?.Windows.FirstOrDefault()?.Page?.Width ?? 360.0;
        if (screenW <= 0) screenW = 360.0;
        baseWidth = Math.Clamp(screenW - 56.0, 280.0, 420.0);
#endif
        double w = baseWidth * _previewScale;
        sheetBorder.WidthRequest = w;
        sheetBorder.ClearValue(VisualElement.HeightRequestProperty);

        double cmScale = w / 21.0;
        double ptScale = w / 595.0;

        double padLeft = Math.Clamp(rule.Left.Cm * cmScale, 8.0, w / 2 - 10);
        double padRight = Math.Clamp(rule.Right.Cm * cmScale, 8.0, w / 2 - 10);
        double padTop = Math.Clamp(rule.Top.Cm * cmScale, 8.0, 120.0);
        double padBottom = Math.Clamp(rule.Bottom.Cm * cmScale, 8.0, 120.0);

        sheetBorder.Padding = new Thickness(padLeft, padTop, padRight, padBottom);

        double bodyFontSize = Math.Clamp(rule.FontSizePt * ptScale, 8.0, 36.0);

        // Параметры заголовков (Headings) из правил
        double headFontSize = Math.Clamp(rule.HeadingFontSizePt * ptScale, 9.0, 48.0);
        string headFont = !string.IsNullOrEmpty(rule.HeadingFontFamily) ? rule.HeadingFontFamily : rule.FontName;
        var headAlign = rule.HeadingAlignment.ToLowerInvariant() switch {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.End,
            _ => TextAlignment.Start,
        };
        double headBefore = Math.Clamp(rule.HeadingSpacingBeforePt * ptScale, 0.0, 40.0);
        double headAfter = Math.Clamp(rule.HeadingSpacingAfterPt * ptScale, 0.0, 40.0);
        double headLineHeight = Math.Clamp(rule.HeadingLineSpacing, 0.8, 2.0);

        var contentStack = new VerticalStackLayout { Spacing = bodyFontSize * 0.5 };

        // 1. Заголовок раздела 1 с учетом настроек ГОСТ
        string h1Text = "1. ВВЕДЕНИЕ И ПОСТАНОВКА ЗАДАЧИ";
        if (rule.HeadingRemoveTrailingDot && h1Text.EndsWith('.'))
            h1Text = h1Text.TrimEnd('.');
        if (!rule.HeadingRequireNoIndent)
            h1Text = ApplyFirstLineIndent(h1Text, rule.FirstLineIndentCm);

        var h1 = new Label {
            Text = h1Text,
            FontSize = headFontSize,
            FontAttributes = rule.HeadingRequireBold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Colors.Black,
            HorizontalTextAlignment = headAlign,
            FontFamily = headFont,
            LineHeight = headLineHeight,
            Margin = new Thickness(0, headBefore, 0, headAfter),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(h1);

        var align = rule.Alignment.ToLowerInvariant() switch {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.End,
            _ => TextAlignment.Start,
        };

        // 2. Абзацы основного текста (красная строка только на первой строке)
        string p1Raw = "Настоящий отчет подготовлен в строгом соответствии с требованиями государственного стандарта. Все параметры страницы и абзацев проверяются программным аудитором GosTek.";
        var p1 = new Label {
            Text = ApplyFirstLineIndent(p1Raw, rule.FirstLineIndentCm),
            FontSize = bodyFontSize,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = align,
            FontFamily = rule.FontName,
            LineHeight = Math.Clamp(rule.LineSpacing, 0.9, 2.0),
            Margin = new Thickness(0),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(p1);

        string p2Raw = "Элементы оформления документа, включая шрифт, кегль, поля страницы и первую строку абзаца, синхронизированы в режиме реального времени. При аудите исключаются нерегламентированные отступы.";
        var p2 = new Label {
            Text = ApplyFirstLineIndent(p2Raw, rule.FirstLineIndentCm),
            FontSize = bodyFontSize,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = align,
            FontFamily = rule.FontName,
            LineHeight = Math.Clamp(rule.LineSpacing, 0.9, 2.0),
            Margin = new Thickness(0),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(p2);

        // 3. Заголовок раздела 2
        string h2Text = "2. РЕЗУЛЬТАТЫ И ИЛЛЮСТРАЦИИ";
        if (rule.HeadingRemoveTrailingDot && h2Text.EndsWith('.'))
            h2Text = h2Text.TrimEnd('.');
        if (!rule.HeadingRequireNoIndent)
            h2Text = ApplyFirstLineIndent(h2Text, rule.FirstLineIndentCm);

        var h2 = new Label {
            Text = h2Text,
            FontSize = headFontSize,
            FontAttributes = rule.HeadingRequireBold ? FontAttributes.Bold : FontAttributes.None,
            TextColor = Colors.Black,
            HorizontalTextAlignment = headAlign,
            FontFamily = headFont,
            LineHeight = headLineHeight,
            Margin = new Thickness(0, headBefore, 0, headAfter),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(h2);

        // 4. Таблица (настройки берутся динамически из rule)
        var tableStack = new VerticalStackLayout { Spacing = 4 * _previewScale, Margin = new Thickness(0, 2 * _previewScale, 0, 6 * _previewScale) };

        // Подпись таблицы (Table Caption)
        double tCapBefore = Math.Clamp(rule.TableCaptionSpacingBeforePt * ptScale, 0.0, 30.0);
        double tCapAfter = Math.Clamp(rule.TableCaptionSpacingAfterPt * ptScale, 0.0, 30.0);
        var tCapAlign = rule.TableCaptionAlignment.ToLowerInvariant() switch {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.End,
            _ => TextAlignment.Start,
        };
        double tCapIndent = rule.TableCaptionRequireNoIndent ? 0 : Math.Clamp(rule.FirstLineIndentCm * cmScale, 0.0, w / 2);

        var tableCaption = new Label {
            Text = "Таблица 1 — Параметры аудита оформления",
            FontSize = Math.Max(8.0, (rule.TableFontSizePt > 0 ? rule.TableFontSizePt : 12.0) * ptScale),
            FontFamily = !string.IsNullOrEmpty(rule.TableFontFamily) ? rule.TableFontFamily : rule.FontName,
            TextColor = Color.FromArgb("#374151"),
            HorizontalTextAlignment = tCapAlign,
            LineHeight = Math.Clamp(rule.TableCaptionLineSpacing, 0.8, 1.8),
            Margin = new Thickness(tCapIndent, tCapBefore, 0, tCapAfter),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        tableStack.Children.Add(tableCaption);

        double tableFontSize = Math.Clamp(rule.TableFontSizePt * ptScale, 7.0, 28.0);
        string tableFontFamily = !string.IsNullOrEmpty(rule.TableFontFamily) ? rule.TableFontFamily : rule.FontName;
        double tableLineHeight = Math.Clamp(rule.TableLineSpacing, 0.8, 2.0);

        var tableGrid = new Grid {
            ColumnDefinitions = new ColumnDefinitionCollection {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowDefinitions = new RowDefinitionCollection {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };

        void AddCell(string text, int col, int row, bool bold = false)
        {
            var cellBorder = new Border {
                Stroke = Color.FromArgb("#D1D5DB"),
                StrokeThickness = 1,
                Padding = new Thickness(6 * _previewScale, 4 * _previewScale),
                BackgroundColor = bold ? Color.FromArgb("#F3F4F6") : Colors.White,
                Content = new Label {
                    Text = text,
                    FontSize = bold ? Math.Max(7.5, tableFontSize) : tableFontSize,
                    FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
                    FontFamily = tableFontFamily,
                    LineHeight = tableLineHeight,
                    TextColor = Color.FromArgb("#1F2937"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                },
            };
            tableGrid.Children.Add(cellBorder);
            Grid.SetColumn(cellBorder, col);
            Grid.SetRow(cellBorder, row);
        }

        AddCell("Параметр", 0, 0, bold: true);
        AddCell("ГОСТ", 1, 0, bold: true);
        AddCell("Статус", 2, 0, bold: true);

        AddCell("Таблица/Кегль", 0, 1);
        AddCell($"{rule.TableFontSizePt:0.#} pt / {rule.TableLineSpacing:0.##}", 1, 1);
        AddCell("Соответствует", 2, 1);

        tableStack.Children.Add(tableGrid);
        contentStack.Children.Add(tableStack);

        // 5. Имитация рисунка (Figure) с отступами сверху/снизу и выравниванием
        double figBefore = Math.Clamp(rule.FigureSpacingBeforePt * ptScale, 0.0, 40.0);
        double figAfter = Math.Clamp(rule.FigureSpacingAfterPt * ptScale, 0.0, 40.0);

        var figAlignOptions = rule.FigureAlignment.ToLowerInvariant() switch {
            "left" => LayoutOptions.Start,
            "right" => LayoutOptions.End,
            _ => LayoutOptions.Center,
        };

        double figIndent = rule.FigureRequireNoIndent ? 0 : Math.Clamp(rule.FirstLineIndentCm * cmScale, 0.0, w / 2);
        double figBoxWidth = Math.Clamp(280.0 * _previewScale, 160.0, w - 40);
        double figBoxHeight = Math.Clamp(65.0 * _previewScale, 40.0, 120.0);

        var figBox = new Border {
            WidthRequest = figBoxWidth,
            HeightRequest = figBoxHeight,
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            Stroke = Color.FromArgb("#CBD5E1"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            HorizontalOptions = figAlignOptions,
            Margin = new Thickness(figIndent, figBefore, 0, figAfter),
            Content = new HorizontalStackLayout {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Spacing = 8,
                Children = {
                    new IconView { Icon = "ruler", Size = Math.Max(12.0, 16.0 * _previewScale), IconColor = "PreviewInk" },
                    new Label {
                        Text = "Иллюстрация ГОСТ (Рис. 1)",
                        FontSize = Math.Max(8.0, 12.0 * ptScale),
                        TextColor = Color.FromArgb("#4B5563"),
                        VerticalOptions = LayoutOptions.Center,
                        FontFamily = rule.FontName,
                    },
                },
            },
        };
        contentStack.Children.Add(figBox);

        // 6. Подпись к рисунку (Caption) с отступами сверху/снизу, выравниванием и интервалом
        double capBefore = Math.Clamp(rule.CaptionSpacingBeforePt * ptScale, 0.0, 40.0);
        double capAfter = Math.Clamp(rule.CaptionSpacingAfterPt * ptScale, 0.0, 40.0);

        var capAlign = rule.CaptionAlignment.ToLowerInvariant() switch {
            "left" => TextAlignment.Start,
            "right" => TextAlignment.End,
            _ => TextAlignment.Center,
        };

        double capIndent = rule.CaptionRequireNoIndent ? 0 : Math.Clamp(rule.FirstLineIndentCm * cmScale, 0.0, w / 2);

        var captionLabel = new Label {
            Text = "Рисунок 1 — Структурная схема модулей и компонентов системы",
            FontSize = Math.Max(8.0, 12.0 * ptScale),
            FontFamily = rule.FontName,
            TextColor = Color.FromArgb("#374151"),
            HorizontalTextAlignment = capAlign,
            LineHeight = Math.Clamp(rule.CaptionLineSpacing, 0.9, 1.8),
            Margin = new Thickness(capIndent, capBefore, 0, capAfter),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(captionLabel);

        // 7. Листинг программного кода
        double codeFontSize = Math.Clamp(rule.CodeFontSizePt * ptScale, 7.5, 26.0);
        double codeIndent = rule.CodeRequireNoIndent ? 0 : Math.Clamp(rule.FirstLineIndentCm * cmScale, 0.0, w / 2);

        var codeBorder = new Border {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(10 * _previewScale, 8 * _previewScale),
            Margin = new Thickness(codeIndent, 6 * _previewScale, 0, 4 * _previewScale),
            Content = new Label {
                Text = "int main() {\n    // Проверка листинга по ГОСТ\n    printf(\"Стандарт оформления применен\\n\");\n    return 0;\n}",
                FontFamily = rule.CodeFontFamily,
                FontSize = codeFontSize,
                LineHeight = Math.Clamp(rule.CodeLineSpacing, 0.9, 1.8),
                TextColor = Color.FromArgb("#0F172A"),
            },
        };
        contentStack.Children.Add(codeBorder);

        // 8. Заключительный абзац
        string p3Raw = "Таким образом, структура документа полностью отвечает заданным правилам форматирования и ГОСТ.";
        var p3 = new Label {
            Text = ApplyFirstLineIndent(p3Raw, rule.FirstLineIndentCm),
            FontSize = bodyFontSize,
            TextColor = Color.FromArgb("#1F2937"),
            HorizontalTextAlignment = align,
            FontFamily = rule.FontName,
            LineHeight = Math.Clamp(rule.LineSpacing, 0.9, 2.0),
            Margin = new Thickness(0, 4 * _previewScale, 0, 0),
            LineBreakMode = LineBreakMode.WordWrap,
        };
        contentStack.Children.Add(p3);

        // 9. Номер страницы (если включен)
        if (rule.RequirePageNumbers) {
            var pageNum = new Label {
                Text = "— 1 —",
                FontSize = Math.Max(8.0, bodyFontSize - 2),
                TextColor = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16 * _previewScale, 0, 0),
                FontFamily = rule.FontName,
            };
            contentStack.Children.Add(pageNum);
        }

        sheetBorder.Content = contentStack;
    }

    // =====================================================================
    //  СОХРАНЕНИЕ И УДАЛЕНИЕ
    // =====================================================================

    private async void OnSaveTapped(object? sender, EventArgs e)
    {
        bool isMaster = _editingRule != null &&
            (_editingRule.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
             _editingRule.Id.Equals("universal", StringComparison.OrdinalIgnoreCase));

        if (!isMaster && string.IsNullOrWhiteSpace(TitleEntry.Text)) {
            await Dialogs.AlertAsync("Укажите название", "Пожалуйста, введите название правила оформления.", DialogKind.Warning);
            TitleEntry.Focus();
            return;
        }

        try {
            var rule = BuildRuleFromForm();
            bool success = await CustomRulesService.Instance.SaveRuleAsync(rule);
            if (success) {
                LastSavedRuleId = rule.Id;
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
        if (_editingRule == null ||
            _editingRule.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
            _editingRule.Id.Equals("universal", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool confirm = await Dialogs.ConfirmAsync(
            "Удалить правило?",
            $"Вы действительно хотите удалить правило «{_editingRule.Title}»? Это действие нельзя отменить.",
            "Удалить",
            DialogKind.Delete,
            "Отмена",
            danger: true);

        if (!confirm)
            return;

        try {
            bool success = await CustomRulesService.Instance.DeleteRuleAsync(_editingRule.Id);
            if (success) {
                LastSavedRuleId = RuleSets.MasterRuleId;
                await Dialogs.AlertAsync("Правило удалено", $"Правило «{_editingRule.Title}» удалено.", DialogKind.Success);
                await Shell.Current.GoToAsync("..");
            } else {
                await Dialogs.AlertAsync("Ошибка удаления", "Не удалось удалить правило.", DialogKind.Danger);
            }
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Ошибка удаления", ex.Message, DialogKind.Danger);
        }
    }

    private async void OnCreateBasedOnThisTapped(object? sender, EventArgs e)
    {
        try {
            var currentFormRule = BuildRuleFromForm();
            var newRule = CustomRulesService.Instance.CreateNewRule();
            var derivedRule = currentFormRule with {
                Id = newRule.Id,
                Title = newRule.Title,
                IsCustom = true,
                Description = "Пользовательское правило"
            };

            bool success = await CustomRulesService.Instance.SaveRuleAsync(derivedRule);
            if (success) {
                _editingRule = derivedRule;
                LoadRuleData(_editingRule);
                Toast.Show("Создано правило на основе текущего", "check");
            }
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Ошибка", ex.Message, DialogKind.Danger);
        }
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private void OnTabSettingsTapped(object? sender, EventArgs e)
    {
        SwitchEditorTab(showPreview: false);
    }

    private void OnTabPreviewTapped(object? sender, EventArgs e)
    {
        SwitchEditorTab(showPreview: true);
    }

    private void SwitchEditorTab(bool showPreview)
    {
        if (DeviceInfo.Platform != DevicePlatform.Android)
            return;

        HapticHelper.Click();

        IsSettingsVisible = !showPreview;
        IsPreviewVisible = showPreview;
        SettingsScrollView.IsVisible = !showPreview;
        PreviewPanelBorder.IsVisible = showPreview;

        if (showPreview)
        {
            TabPreviewBtn.SetDynamicResource(VisualElement.BackgroundColorProperty, "BrandGreen");
            TabPreviewLabel.TextColor = Colors.White;
            TabPreviewIcon.IconColor = "White";

            TabSettingsBtn.BackgroundColor = Colors.Transparent;
            TabSettingsLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            TabSettingsIcon.IconColor = "TextSecondary";

            UpdateLivePreview();
        }
        else
        {
            TabSettingsBtn.SetDynamicResource(VisualElement.BackgroundColorProperty, "BrandGreen");
            TabSettingsLabel.TextColor = Colors.White;
            TabSettingsIcon.IconColor = "White";

            TabPreviewBtn.BackgroundColor = Colors.Transparent;
            TabPreviewLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            TabPreviewIcon.IconColor = "TextSecondary";
        }
    }
}
