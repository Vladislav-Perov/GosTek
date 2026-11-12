/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Интерактивная форма заполнения полей выбранного шаблона с живым предпросмотром А4 по ГОСТ
 * СОДЕРЖИМОЕ: FieldsPage
 * ЗАВИСИМОСТИ: GosTek.Models, GosTek.Services, GosTek.TemplateEngine, GosTek.Helpers
 * ==============================================================================
 */
using GosTek.Controls;
using GosTek.Helpers;
using GosTek.Models;
using GosTek.Resources.Styles;
using GosTek.Services;
using GosTek.Views.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

[QueryProperty(nameof(TemplateId), "id")]
public partial class FieldsPage : ContentPage
{
    // Ширина окна от этого значения: ввод слева, предпросмотр справа.
    // Уже: предпросмотр сразу под полями (телефон).
    private const double WideBreakpoint = 800;

    // Лист всегда «бумажный», независимо от темы приложения
    private static readonly Color Ink = Color.FromArgb("#1B1B1F");
    private static readonly Color GapText = Color.FromArgb("#7A8499");
    private static readonly Color GapBg = Color.FromArgb("#EAEEF5");

    // Шрифт с засечками, как в Word: системный на каждой платформе
    private static readonly string SerifFont =
        DeviceInfo.Platform == DevicePlatform.Android ? "serif" : "Times New Roman";

    // Поле на экране: само поле ввода, рамка, красная подпись, контейнер, бейдж профиля и признак источника
    private sealed class FieldUi
    {
        public TemplateField Field { get; }
        public InputView Input { get; }
        public Border Box { get; }
        public Label Error { get; }
        public View Container { get; }
        public View? ProfileTag { get; set; }
        public bool IsFromProfile { get; set; }

        public FieldUi(
            TemplateField field,
            InputView input,
            Border box,
            Label error,
            View container,
            View? profileTag = null,
            bool isFromProfile = false)
        {
            Field = field;
            Input = input;
            Box = box;
            Error = error;
            Container = container;
            ProfileTag = profileTag;
            IsFromProfile = isFromProfile;
        }
    }

    private TemplateDef? _template;
    private RuleSet? _activeRule;
    private List<PreviewPara> _previewParas = new();
    private readonly List<FieldUi> _fields = new();
    private Dictionary<string, string> _values = new();

    private bool _isChatMaximized;
    private double _originalChatHeight = 540;
    private bool _isProgrammaticFilling;
    private string? _targetField;

    private int _statusVersion;
    private int _layoutVersion;
    private int _renderVersion;
    private bool _isWide;
    private double _renderedWidth;
    private double _font = 13;

    private string _templateId = "";

    // Эффективный шрифт документа с учётом ГОСТ-правила и платформы
    private string EffectiveFont
    {
        get
        {
            var font = _activeRule?.FontName;
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                if (string.IsNullOrWhiteSpace(font) || font.Contains("Times", StringComparison.OrdinalIgnoreCase))
                    return "serif";
                if (font.Contains("Arial", StringComparison.OrdinalIgnoreCase))
                    return "sans-serif";
                return font;
            }
            return !string.IsNullOrWhiteSpace(font) ? font : SerifFont;
        }
    }

    // Shell передаёт сюда id из адреса FieldsPage?id=...
    public string TemplateId
    {
        get => _templateId;
        set
        {
            _templateId = value;
            BuildForm();
        }
    }

    public FieldsPage()
    {
        InitializeComponent();
    }

    // Лист рисуется в том месте, которое сейчас видно
    private Border ActivePanel => _isWide ? SidePanel : InlinePanel;
    private Border ActiveSheet => _isWide ? SideSheet : InlineSheet;

    // =====================================================================
    //  РАСКЛАДКА: широко = два столбца, узко = предпросмотр под полями
    //  Ничего не переносится между контейнерами: меняется только видимость
    //  и ширина столбца, и только когда окно перестали тянуть.
    // =====================================================================

    private async void OnPageSizeChanged(object? sender, EventArgs e)
    {
        int version = ++_layoutVersion;

        // Ждём, пока размер окна перестанет меняться
        await Task.Delay(200);
        if (version != _layoutVersion || Width <= 0)
            return;

        bool wide = Width >= WideBreakpoint;
        if (wide == _isWide)
            return;

        _isWide = wide;
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        Body.ColumnDefinitions[1].Width = _isWide ? GridLength.Star : new GridLength(0);

        SideScroll.IsVisible = _isWide;
        PaneDivider.IsVisible = _isWide;
        InlinePreview.IsVisible = !_isWide;

        _renderedWidth = 0;   // в новом месте лист надо нарисовать заново
        QueueRender();
    }

    // =====================================================================
    //  ФОРМА
    // =====================================================================

    private void BuildForm()
    {
        _template = TemplateCatalog.Find(_templateId);
        FieldsHost.Children.Clear();
        _fields.Clear();
        AiMemory.Instance.Clear();

        if (_template is null)
            return;

        TitleLabel.Text = _template.Title;

        // Подбираем правило ГОСТ для шаблона
        _activeRule = !string.IsNullOrWhiteSpace(_template.FilePath)
            ? CustomRulesService.Instance.MatchRuleSetForTemplate(System.IO.Path.GetFileName(_template.FilePath))
            : null;

        var draft = DraftStore.For(_template.Id);
        _values = draft;

        _previewParas = TemplateEngine.ReadPreview(_template.FilePath);

        foreach (var field in _template.Fields)
        {
            // Поля-даты по умолчанию = сегодня
            if (!draft.ContainsKey(field.Id) && TemplateCatalog.DefaultFor(field) is { } def)
                draft[field.Id] = def;

            var ui = CreateField(field, draft);
            _fields.Add(ui);
            FieldsHost.Children.Add(ui.Container);
        }

        QueueRender();

        // Данные из профиля подставляются сами
        _ = AutoFillAsync();
    }

    private FieldUi CreateField(TemplateField field, Dictionary<string, string> draft)
    {
        draft.TryGetValue(field.Id, out var saved);

        // Подпись (+ метка «из профиля»)
        var label = new Label { Text = field.Label, VerticalOptions = LayoutOptions.Center };
        label.StyleClass = new[] { "FieldLabel" };

        var labelRow = new HorizontalStackLayout { Spacing = 8 };
        labelRow.Children.Add(label);

        // Маркер поля, требующего развернутого описания ([Поле|расписать])
        if (field.RequiresExpansion)
        {
            var elaborateBadge = CreateElaborateBadge();
            labelRow.Children.Add(elaborateBadge);
        }

        var profileTag = CreateProfileTag();
        profileTag.IsVisible = false;
        labelRow.Children.Add(profileTag);

        // Рамка + поле ввода
        var box = new Border();
        box.StyleClass = new[] { "FieldBox" };

        InputView input;
        if (field.Multiline)
        {
            var editor = new Editor
            {
                Placeholder = field.Placeholder,
                Text = saved ?? "",
                AutoSize = EditorAutoSizeOption.TextChanges,
            };
            editor.StyleClass = new[] { "FieldEditor" };
            box.Padding = new Thickness(16, 8);
            input = editor;
        }
        else
        {
            var entry = new Entry
            {
                Placeholder = field.Placeholder,
                Text = saved ?? "",
            };
            entry.StyleClass = new[] { "FieldEntry" };
            input = entry;
        }
        box.Content = input;

        // Кнопка ИИ у каждого поля для точечного расширения
        var aiFieldBtn = CreateFieldAiButton(field, input);
        labelRow.Children.Add(aiFieldBtn);

        var keyboard = SmartInputHelper.ResolveKeyboard(field.Label, field.Placeholder, field.Id);
        if (input is Entry ent)
            ent.Keyboard = keyboard;
        else if (input is Editor edt)
            edt.Keyboard = keyboard;

        if (SmartInputHelper.IsDateField(field.Label, field.Placeholder, field.Id))
        {
            var todayChip = CreateTodayChip(() =>
            {
                HapticHelper.Click();
                string today = SmartInputHelper.FormatToday();
                if (input is Editor ed) ed.Text = today;
                else if (input is Entry en) en.Text = today;
            });
            labelRow.Children.Add(todayChip);
        }

        bool isFio = SmartInputHelper.IsFioField(field.Label, field.Placeholder, field.Id);

        // Красная подпись под полем
        var error = new Label
        {
            Text = "Заполните поле",
            FontSize = 12,
            TextColor = ThemeService.ErrorColor,
            IsVisible = false,
        };

        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(labelRow);
        stack.Children.Add(box);
        stack.Children.Add(error);

        var ui = new FieldUi(field, input, box, error, stack, profileTag);

        // Текст изменился: сохранить в черновик, убрать подсветку, обновить лист
        void Changed(string? text)
        {
            if (!_isProgrammaticFilling && ui.IsFromProfile)
            {
                ui.IsFromProfile = false;
                if (ui.ProfileTag is not null)
                    ui.ProfileTag.IsVisible = false;
                SetInputFromProfileStyle(ui.Input, false);
            }

            if (isFio && !string.IsNullOrEmpty(text))
            {
                var titleCased = SmartInputHelper.ToTitleCase(text);
                if (text != titleCased)
                {
                    if (input is Entry en)
                    {
                        int cur = en.CursorPosition;
                        en.Text = titleCased;
                        en.CursorPosition = Math.Min(cur, titleCased.Length);
                        return;
                    }
                }
            }

            draft[field.Id] = text ?? "";
            if (!IsEmpty(ui))
                FieldValidationHelper.Mark(ui.Box, ui.Error, false);
            QueueRender();
        }

        if (input is Editor ed)
            ed.TextChanged += (_, e) => Changed(e.NewTextValue);
        else if (input is Entry en)
            en.TextChanged += (_, e) => Changed(e.NewTextValue);

        // Ушли из пустого поля: подсветить
        input.Unfocused += (_, _) => FieldValidationHelper.Mark(ui.Box, ui.Error, IsEmpty(ui));

        return ui;
    }

    private static View CreateProfileTag()
    {
        var icon = Icons.CreatePath(Icons.User, size: 10, strokeColorResource: "TextSecondary");
        var text = new Label
        {
            Text = "из профиля",
            FontSize = 10,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        text.SetDynamicResource(Label.TextColorProperty, "TextSecondary");

        var stack = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { icon, text },
        };

        var tag = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = stack,
            Opacity = 0.85
        };
        tag.SetDynamicResource(Border.StrokeProperty, "CardStroke");
        tag.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        return tag;
    }

    private static void SetInputFromProfileStyle(InputView input, bool fromProfile)
    {
        var colorKey = fromProfile ? "TextSecondary" : "TextPrimary";
        if (input is Entry en)
            en.SetDynamicResource(Entry.TextColorProperty, colorKey);
        else if (input is Editor ed)
            ed.SetDynamicResource(Editor.TextColorProperty, colorKey);
    }

    private static View CreateTodayChip(Action onTap)
    {
        var icon = Icons.CreatePath(Icons.Calendar, size: 11, strokeColorResource: "BrandGreen");
        var text = new Label
        {
            Text = "Сегодня",
            FontSize = 10.5,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
        };
        text.SetDynamicResource(Label.TextColorProperty, "BrandGreen");

        var stack = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { icon, text },
        };

        var chip = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = stack,
        };
        chip.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        chip.GestureRecognizers.Add(tap);

        return chip;
    }

    // ===== Пустое поле: подсветка =====
    private static bool IsEmpty(FieldUi f) => string.IsNullOrWhiteSpace(f.Input.Text);

    // ===== Подстановка из профиля =====
    // onlyEmpty = true: заполняем только пустые поля (при открытии окна)
    // onlyEmpty = false: перезаписываем все поля из профиля (кнопка)
    private int FillFromProfile(ProfileData profile, bool onlyEmpty)
    {
        int filled = 0;
        _isProgrammaticFilling = true;
        try
        {
            foreach (var f in _fields)
            {
                if (onlyEmpty && !IsEmpty(f))
                    continue;

                string? value = null;
                if (f.Field.Source != ProfileSource.None)
                    value = TemplateCatalog.FromProfile(f.Field.Source, profile);

                if (string.IsNullOrWhiteSpace(value))
                    value = TemplateEngine.ResolveCustomField(f.Field.Label, profile.CustomFields);

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                f.Input.Text = value;   // TextChanged сохранит значение, уберёт подсветку, обновит лист
                f.IsFromProfile = true;
                if (f.ProfileTag is not null)
                    f.ProfileTag.IsVisible = true;
                SetInputFromProfileStyle(f.Input, true);
                filled++;
            }
        }
        finally
        {
            _isProgrammaticFilling = false;
        }

        return filled;
    }

    private async Task AutoFillAsync()
    {
        var profile = await ProfileStorage.LoadAsync();
        int filled = FillFromProfile(profile, onlyEmpty: true);

        if (filled > 0)
            ShowStatus($"Подставлено из профиля: {filled}");
    }

    private async void OnFillTapped(object? sender, TappedEventArgs e)
    {
        if (_template is null)
            return;

        var profile = await ProfileStorage.LoadAsync();
        int filled = FillFromProfile(profile, onlyEmpty: false);

        ShowStatus(filled > 0
            ? $"Подставлено из профиля: {filled}"
            : "В профиле пока нет данных для этого шаблона. Заполните профиль на вкладке «Профиль».");
    }

    // Строка-сообщение под кнопкой, исчезает через несколько секунд
    private async void ShowStatus(string text)
    {
        int version = ++_statusVersion;

        FillStatusLabel.Text = text;
        FillStatusLabel.IsVisible = true;

        await Task.Delay(3500);

        if (version == _statusVersion)
            FillStatusLabel.IsVisible = false;
    }

    private CancellationTokenSource? _pulseCts;

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
        StartBuildPulse();
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
        StopBuildPulse();
    }

    private void StartBuildPulse()
    {
        StopBuildPulse();
        _pulseCts = new CancellationTokenSource();
        _ = AnimationHelper.AnimatePulseAsync(BuildButton, _pulseCts.Token);
    }

    private void StopBuildPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts?.Dispose();
        _pulseCts = null;
        AnimationHelper.StopPulse(BuildButton);
    }

    // ===== Назад =====
    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    // ===== Собрать документ =====
    private async void OnBuildTapped(object? sender, TappedEventArgs e)
    {
        StopBuildPulse();
        HapticHelper.Click();
        if (_template is null)
            return;

        // Подсвечиваем все пустые поля и переходим к первому
        var empty = _fields.Where(IsEmpty).ToList();
        if (empty.Count > 0)
        {
            foreach (var f in empty)
                FieldValidationHelper.Mark(f.Box, f.Error, true);

            await FormScroll.ScrollToAsync(empty[0].Container, ScrollToPosition.Center, true);
            empty[0].Input.Focus();
            return;
        }

        // Все поля заполнены: открываем шторку экспорта
        FileNameEntry.Text = ExportService.DefaultFileName(_template);
        ExportOverlay.IsVisible = true;
    }

    // =====================================================================
    //  ЭКСПОРТ
    // =====================================================================

    private void CloseExport() => ExportOverlay.IsVisible = false;

    private void OnExportCloseTapped(object? sender, TappedEventArgs e) => CloseExport();

    // Нажатие на саму шторку не должно её закрывать (жест перехватывает нажатие у подложки)
    private void OnSheetTapped(object? sender, TappedEventArgs e) { }

    // Имя файла из поля в шторке (с заменой запрещённых символов)
    private string ExportName()
        => ExportService.SafeFileName(FileNameEntry.Text, ExportService.DefaultFileName(_template!));

    private async void OnExportDocxTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try
        {
            var result = await ExportService.SaveDocxAsync(_template, _values, ExportName());
            if (result is null)
                return;

            AiMemory.Instance.Clear();
            CloseExport();
            HapticHelper.Success();
            Toast.Show("Документ Word сохранён", "file");
            await ExportService.AfterSaveAsync(result);
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
        }
    }

    // «Поделиться»: файл тоже сохраняется (по тем же правилам), затем открывается системное окно отправки
    private async void OnExportShareTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try
        {
            var result = await ExportService.SaveDocxAsync(_template, _values, ExportName());
            if (result is null)
                return;

            AiMemory.Instance.Clear();
            CloseExport();
            HapticHelper.Click();
            Toast.Show("Файл готов к отправке", "share");
            await ExportService.ShareFileAsync(result.FilePath);
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Не удалось поделиться", ex.Message, DialogKind.Danger);
        }
    }

    // PDF: нативное сохранение документа в PDF через QuestPDF
    private async void OnExportPdfTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try
        {
            var result = await ExportService.SavePdfAsync(_template, _values, ExportName());
            if (result is null)
                return;

            AiMemory.Instance.Clear();
            CloseExport();
            HapticHelper.Success();
            Toast.Show("Документ PDF сохранён", "file");
            await ExportService.AfterSaveAsync(result);
        }
        catch (Exception ex)
        {
            await Dialogs.AlertAsync("Не удалось сохранить PDF", ex.Message, DialogKind.Danger);
        }
    }

    // =====================================================================

    // Лист перерисовываем не на каждый символ и не на каждый пиксель растягивания,
    // а один раз после короткой паузы
    private async void QueueRender()
    {
        int version = ++_renderVersion;

        await Task.Delay(60);
        if (version != _renderVersion)
            return;

        RenderPreview();
    }

    // Изменилась ширина подложки: перерисовать лист под новую ширину
    private void OnPanelSizeChanged(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, ActivePanel))
            return;

        double w = Math.Min(ActivePanel.Width - 28, 520);
        if (w <= 0 || Math.Abs(w - _renderedWidth) < 1)
            return;

        QueueRender();
    }

    private void RenderPreview()
    {
        if (_template is null)
            return;

        // Ширина листа: ширина подложки минус отступы, не больше 520
        double w = Math.Min(ActivePanel.Width - 28, 520);
        if (w <= 0)
            return;

        _renderedWidth = w;
        _font = Math.Clamp(w / 27, 11, 17);

        var sheet = ActiveSheet;
        sheet.WidthRequest = w;
        sheet.MinimumHeightRequest = w * 297 / 210;   // пропорции А4

        // Масштаб: ширина листа w соответствует 21.0 см листа А4
        double cmScale = w / 21.0;
        double leftCm = _activeRule?.Left.Cm ?? 3.0;
        double rightCm = _activeRule?.Right.Cm ?? 1.5;
        double topCm = _activeRule?.Top.Cm ?? 2.0;
        double bottomCm = _activeRule?.Bottom.Cm ?? 2.0;

        sheet.Padding = new Thickness(
            Math.Max(12, leftCm * cmScale),
            Math.Max(12, topCm * cmScale),
            Math.Max(10, rightCm * cmScale),
            Math.Max(12, bottomCm * cmScale));

        sheet.Content = BuildSheet(w);
    }

    // Лист строится из абзацев самого шаблона: поля заменяются значениями или серыми подсказками
    private View BuildSheet(double w)
    {
        var stack = new VerticalStackLayout { Spacing = _font * 0.25 };

        if (_previewParas.Count == 0)
        {
            stack.Children.Add(new Label { Text = _template!.Title, TextColor = Ink, FontFamily = EffectiveFont });
            return stack;
        }

        foreach (var para in _previewParas)
        {
            if (para.Blank)
            {
                stack.Children.Add(new BoxView { HeightRequest = _font * 0.7, Color = Colors.Transparent });
                continue;
            }

            var spans = para.Parts
                .Select(part => part.FieldId is null ? T(part.Text) : Val(part.FieldId))
                .ToArray();

            var label = Para(para.Align, spans);
            label.FontSize = Math.Clamp(_font * para.Pt / 14, 8, 26);
            if (para.Bold)
                label.FontAttributes |= FontAttributes.Bold;
            if (para.Italic)
                label.FontAttributes |= FontAttributes.Italic;

            stack.Children.Add(label);
        }

        return stack;
    }

    // ===== Помощники для листа =====

    private TemplateField? FieldOf(string id) => _template!.Fields.FirstOrDefault(f => f.Id == id);

    // Значение поля. Если пусто, серая плашка с подсказкой
    private Span Val(string id)
    {
        _values.TryGetValue(id, out var v);

        if (!string.IsNullOrWhiteSpace(v))
            return new Span { Text = v.Trim() };

        return new Span
        {
            Text = FieldOf(id) is { Placeholder.Length: > 0 } fld ? fld.Placeholder : FieldOf(id)?.Label ?? id,
            TextColor = GapText,
            BackgroundColor = GapBg,
        };
    }

    // Обычный текст
    private static Span T(string text) => new() { Text = text };

    // Абзац из кусочков (обычный текст и значения полей)
    private Label Para(TextAlignment align, params Span[] spans)
    {
        var formatted = new FormattedString();
        foreach (var s in spans)
            formatted.Spans.Add(s);

        return new Label
        {
            FormattedText = formatted,
            TextColor = Ink,
            FontFamily = EffectiveFont,
            FontSize = _font,
            HorizontalTextAlignment = align,
            LineBreakMode = LineBreakMode.WordWrap,
        };
    }

    private Label Heading(string text) => new()
    {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        FontSize = _font + 2,
        CharacterSpacing = 1,
        HorizontalTextAlignment = TextAlignment.Center,
        TextColor = Ink,
        FontFamily = EffectiveFont,
        Margin = new Thickness(0, _font * 0.5, 0, 0),
    };

    // =====================================================================
    //  ИИ-ПОМОЩНИК: ЧАТ, ЗАПОЛНЕНИЕ ПОЛЕЙ И МАСКИРОВАНИЕ
    // =====================================================================

    private static Border CreateElaborateBadge()
    {
        var badge = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center
        };
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
        badge.SetDynamicResource(Border.StrokeProperty, "BrandGreen");

        var lbl = new Label
        {
            Text = "✍️ Расписать",
            FontSize = 10.5,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        lbl.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
        badge.Content = lbl;
        return badge;
    }

    private Border CreateFieldAiButton(TemplateField field, InputView input)
    {
        var btn = new Border
        {
            StyleClass = new[] { "HoverCard" },
            WidthRequest = 26,
            HeightRequest = 26,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Padding = 0,
            VerticalOptions = LayoutOptions.Center
        };
        AutomationProperties.SetName(btn, $"Помощь ИИ для {field.Label}");
        SemanticProperties.SetDescription(btn, $"Помощь ИИ для {field.Label}");
        btn.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
        btn.SetDynamicResource(Border.StrokeProperty, "CardStroke");

        var icon = new GosTek.Views.Controls.IconView
        {
            Icon = "sparkles",
            Size = 13,
            IconColor = "BrandGreen",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        btn.Content = icon;

        btn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                HapticHelper.Click();
                await btn.AnimateBounceAsync();
                OpenChatForField(field, input);
            })
        });

        return btn;
    }

    private void OpenChatForField(TemplateField field, InputView input)
    {
        _targetField = field.Label;
        var text = input.Text?.Trim();

        if (!string.IsNullOrWhiteSpace(text))
            ChatInput.Text = $"Расширь и сделай официальным: {text}";
        else
            ChatInput.Text = $"Помоги официально заполнить поле \"{field.Label}\"";

        OpenChat();
    }

    private void OpenChat()
    {
        HapticHelper.Click();
        AiAutoApplySwitch.IsToggled = SettingsService.AiAutoApplyMode;

        _originalChatHeight = DeviceInfo.Platform == DevicePlatform.Android ? 520 : 560;
        if (!_isChatMaximized)
            ChatSheet.HeightRequest = _originalChatHeight;

        if (ChatMessagesStack.Children.Count == 0)
        {
            AddBotMessageBubble("Привет! Расскажите, какой документ и для чего вам нужен. Можете описать ситуацию в свободной форме, а я выделю нужные данные и сам запрошу недостающие.");
        }

        ChatOverlay.IsVisible = true;
        ChatSheet.TranslationY = 700;
        _ = ChatSheet.TranslateToAsync(0, 0, 250, Easing.CubicOut);
        ChatInput.Focus();
    }

    private void OnOpenChatTapped(object? sender, TappedEventArgs e)
    {
        _targetField = null;
        OpenChat();
    }

    private async void OnToggleChatMaximizeTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();

        _isChatMaximized = !_isChatMaximized;
        ChatMaximizeIcon.Icon = _isChatMaximized ? "minimize" : "maximize";

        double startHeight = ChatSheet.HeightRequest > 0 ? ChatSheet.HeightRequest : (ChatSheet.Height > 0 ? ChatSheet.Height : 540);
        double endHeight = _isChatMaximized
            ? (this.Height > 100 ? this.Height * 0.95 : 750)
            : _originalChatHeight;

        var animation = new Animation(v => ChatSheet.HeightRequest = v, startHeight, endHeight, Easing.CubicOut);
        animation.Commit(ChatSheet, "ChatSheetMaximize", length: 250);
    }

    private async void OnCloseChatTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        MemoryPanel.IsVisible = false;
        await ChatSheet.TranslateToAsync(0, 700, 200, Easing.CubicIn);
        ChatOverlay.IsVisible = false;
        if (_isChatMaximized)
        {
            _isChatMaximized = false;
            ChatMaximizeIcon.Icon = "maximize";
            ChatSheet.HeightRequest = _originalChatHeight;
        }
    }

    private async void OnChatOverlayBackgroundTapped(object? sender, TappedEventArgs e)
    {
        MemoryPanel.IsVisible = false;
        await ChatSheet.TranslateToAsync(0, 700, 200, Easing.CubicIn);
        ChatOverlay.IsVisible = false;
        if (_isChatMaximized)
        {
            _isChatMaximized = false;
            ChatMaximizeIcon.Icon = "maximize";
            ChatSheet.HeightRequest = _originalChatHeight;
        }
    }

    private void OnChatSheetTapped(object? sender, TappedEventArgs e)
    {
        // Предотвращаем закрытие шторки при клике на саму панель
    }

    private void OnToggleMemoryTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        MemoryPanel.IsVisible = !MemoryPanel.IsVisible;
        if (MemoryPanel.IsVisible)
            RefreshMemoryUi();
    }

    private void OnCloseMemoryTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        MemoryPanel.IsVisible = false;
    }

    private void OnClearAllMemoryTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        AiMemory.Instance.Clear();
        RefreshMemoryUi();
        ShowStatus("Память ассистента очищена");
    }

    private void RefreshMemoryUi()
    {
        MemoryStack.Children.Clear();
        var facts = AiMemory.Instance.Facts;

        MemoryEmptyLabel.IsVisible = facts.Count == 0;
        ClearMemoryButton.IsVisible = facts.Count > 0;

        foreach (var fact in facts)
        {
            var factBorder = new Border
            {
                StyleClass = new[] { "HoverCard" },
                Padding = new Thickness(10, 8),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 }
            };
            factBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "PageBg");
            factBorder.SetDynamicResource(Border.StrokeProperty, "CardStroke");

            var rowGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8
            };

            var textLabel = new Label
            {
                Text = $"Помню ({fact.Key}): {fact.Value}",
                FontSize = 12,
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Center
            };
            textLabel.SetDynamicResource(Label.TextColorProperty, "TextPrimary");
            Grid.SetColumn(textLabel, 0);
            rowGrid.Children.Add(textLabel);

            var deleteBtn = new Border
            {
                WidthRequest = 24,
                HeightRequest = 24,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                VerticalOptions = LayoutOptions.Center
            };
            deleteBtn.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
            deleteBtn.Content = new Label
            {
                Text = "✕",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#EF4444"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            deleteBtn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    HapticHelper.Click();
                    AiMemory.Instance.RemoveFact(fact.Key);
                    RefreshMemoryUi();
                })
            });

            Grid.SetColumn(deleteBtn, 1);
            rowGrid.Children.Add(deleteBtn);

            factBorder.Content = rowGrid;
            MemoryStack.Children.Add(factBorder);
        }
    }

    private void OnAiAutoApplySwitchToggled(object? sender, ToggledEventArgs e)
    {
        SettingsService.AiAutoApplyMode = e.Value;
    }

    private async void OnChatInputCompleted(object? sender, EventArgs e)
    {
        await SendChatMessageAsync();
    }

    private async void OnSendMessageTapped(object? sender, TappedEventArgs e)
    {
        await SendChatMessageAsync();
    }

    private async Task SendChatMessageAsync()
    {
        var userText = ChatInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(userText))
            return;

        HapticHelper.Click();
        ChatInput.Text = string.Empty;

        AddUserMessageBubble(userText);
        _ = ChatScrollView.ScrollToAsync(ChatMessagesStack, ScrollToPosition.End, true);

        TypingIndicator.IsVisible = true;
        SendButton.IsEnabled = false;

        try
        {
            var fieldNames = _template?.Fields.Select(f => f.Label).ToList() ?? new();
            var currentFormFields = _fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Field.Label))
                .ToDictionary(f => f.Field.Label, f => f.Input.Text?.Trim() ?? string.Empty);

            var profile = await ProfileStorage.LoadAsync();
            var maskedText = PrivacyMasker.Mask(
                userText,
                out var extractedPlaceholders,
                out var suspiciousNames,
                profile: profile,
                numbered: true);

            var response = await AiAssistantService.Instance.ProcessRequestAsync(
                maskedText,
                fieldNames,
                currentFormFields: currentFormFields,
                extractedPlaceholders: extractedPlaceholders,
                targetField: _targetField,
                templateName: _template?.Title,
                memory: AiMemory.Instance.FormatForPrompt());

            _targetField = null;

            TypingIndicator.IsVisible = false;

            if (!string.IsNullOrWhiteSpace(response.Question))
            {
                // Если question не пустой — выводим в чат как обычное сообщение от бота (не создавая карточку с полями)
                AddBotMessageBubble(response.Question);
            }
            else
            {
                var filledFields = response.Fields
                    .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) &&
                                 !string.Equals(kv.Value.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

                if (filledFields.Count > 0)
                {
                    if (SettingsService.AiAutoApplyMode)
                    {
                        int applied = ApplyFieldsToForm(filledFields);
                        AddBotMessageBubble($"✅ Поля заполнены автоматически ({applied} из {filledFields.Count})!");
                    }
                    else
                    {
                        AddProposedFieldsCard(filledFields);
                    }
                }
                else
                {
                    AddBotMessageBubble("Не удалось извлечь поля из текста. Пожалуйста, укажите конкретные данные (например: «ФИО: Иванов Иван, причина: отпуск»).");
                }
            }
        }
        catch (Exception ex)
        {
            TypingIndicator.IsVisible = false;
            AddErrorMessageBubble($"⚠️ Ошибка: {ex.Message}");
        }
        finally
        {
            TypingIndicator.IsVisible = false;
            SendButton.IsEnabled = true;
            _ = ChatScrollView.ScrollToAsync(ChatMessagesStack, ScrollToPosition.End, true);
        }
    }

    /// <summary>
    /// Применяет словарь значений полей, возвращенный ИИ, к UI и DraftStore.
    /// </summary>
    public int ApplyFieldsToForm(Dictionary<string, string> fields)
    {
        int applied = 0;

        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            // Поиск по Label или Id (точное или неполное совпадение)
            var targetField = _fields.FirstOrDefault(f =>
                string.Equals(f.Field.Label, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Field.Id, key, StringComparison.OrdinalIgnoreCase))
                ?? _fields.FirstOrDefault(f =>
                    f.Field.Label.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                    key.Contains(f.Field.Label, StringComparison.OrdinalIgnoreCase));

            if (targetField is not null)
            {
                targetField.Input.Text = value.Trim();
                if (targetField.ProfileTag is not null)
                    targetField.ProfileTag.IsVisible = true;
                applied++;
            }
        }

        if (applied > 0)
        {
            QueueRender();
            ShowStatus($"ИИ применил {applied} полей");
        }

        return applied;
    }

    private void AddUserMessageBubble(string text)
    {
        var label = new Label
        {
            Text = text,
            TextColor = Colors.White,
            FontSize = 13.5,
            LineBreakMode = LineBreakMode.WordWrap
        };

        var border = new Border
        {
            HorizontalOptions = LayoutOptions.End,
            MaximumWidthRequest = 480,
            Padding = new Thickness(14, 10),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16, 16, 2, 16) }
        };
        border.SetDynamicResource(VisualElement.BackgroundProperty, "BrandGreenGradient");
        border.Content = label;

        ChatMessagesStack.Children.Add(border);
    }

    private void AddBotMessageBubble(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 13.5,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetDynamicResource(Label.TextColorProperty, "TextPrimary");

        var border = new Border
        {
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 480,
            Padding = new Thickness(14, 10),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16, 16, 16, 2) }
        };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        border.SetDynamicResource(Border.StrokeProperty, "CardStroke");
        border.Content = label;

        ChatMessagesStack.Children.Add(border);
    }

    private void AddErrorMessageBubble(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12.5,
            TextColor = Color.FromArgb("#EF4444"),
            LineBreakMode = LineBreakMode.WordWrap
        };

        var border = new Border
        {
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 480,
            Padding = new Thickness(12, 9),
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromArgb("#EF4444")),
            BackgroundColor = Color.FromArgb("#15EF4444"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Content = label
        };

        ChatMessagesStack.Children.Add(border);
    }

    private void AddProposedFieldsCard(Dictionary<string, string> fields)
    {
        var filledFields = fields
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) &&
                         !string.Equals(kv.Value.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        if (filledFields.Count == 0)
            return;

        var contentStack = new VerticalStackLayout { Spacing = 10 };

        var headerLabel = new Label
        {
            Text = "Предложенные данные для полей:",
            FontSize = 13.5,
            FontAttributes = FontAttributes.Bold
        };
        headerLabel.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
        contentStack.Children.Add(headerLabel);

        // Блок живого предпросмотра: аккуратный список (каждое поле с новой строки, название поля жирным, значение обычным)
        var previewBox = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 2, 0, 2)
        };
        previewBox.SetDynamicResource(VisualElement.BackgroundColorProperty, "PageBg");
        previewBox.SetDynamicResource(Border.StrokeProperty, "CardStroke");

        var previewStack = new VerticalStackLayout { Spacing = 6 };
        foreach (var (k, v) in filledFields)
        {
            var rowLabel = new Label
            {
                FontSize = 12.5,
                LineBreakMode = LineBreakMode.WordWrap
            };
            var formatted = new FormattedString();

            var boldSpan = new Span
            {
                Text = $"{k}: ",
                FontAttributes = FontAttributes.Bold
            };
            boldSpan.SetDynamicResource(Span.TextColorProperty, "TextPrimary");

            var valSpan = new Span
            {
                Text = v
            };
            valSpan.SetDynamicResource(Span.TextColorProperty, "TextSecondary");

            formatted.Spans.Add(boldSpan);
            formatted.Spans.Add(valSpan);

            rowLabel.FormattedText = formatted;
            previewStack.Children.Add(rowLabel);
        }
        previewBox.Content = previewStack;
        contentStack.Children.Add(previewBox);

        var actionsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var applyButton = new Border
        {
            StyleClass = new[] { "HoverGreen" },
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 8),
            Content = new Label
            {
                Text = "Применить",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center
            }
        };
        applyButton.SetDynamicResource(VisualElement.BackgroundProperty, "BrandGreenGradient");

        var cancelButton = new Border
        {
            StyleClass = new[] { "HoverCard" },
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 8)
        };
        cancelButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        cancelButton.SetDynamicResource(Border.StrokeProperty, "CardStroke");

        var cancelLabel = new Label
        {
            Text = "Отменить",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        };
        cancelLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
        cancelButton.Content = cancelLabel;

        Grid.SetColumn(applyButton, 0);
        Grid.SetColumn(cancelButton, 1);
        actionsGrid.Children.Add(applyButton);
        actionsGrid.Children.Add(cancelButton);

        contentStack.Children.Add(actionsGrid);

        var statusLabel = new Label
        {
            FontSize = 12.5,
            FontAttributes = FontAttributes.Bold,
            IsVisible = false
        };
        contentStack.Children.Add(statusLabel);

        var cardBorder = new Border
        {
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 480,
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = contentStack
        };
        cardBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "CardBg");
        cardBorder.SetDynamicResource(Border.StrokeProperty, "CardStroke");

        var applyTap = new TapGestureRecognizer();
        applyTap.Tapped += async (_, _) =>
        {
            HapticHelper.Click();
            await applyButton.AnimateBounceAsync();
            actionsGrid.IsVisible = false;
            int count = ApplyFieldsToForm(filledFields);
            statusLabel.Text = $"✅ Применено к документу ({count} полей)";
            statusLabel.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
            statusLabel.IsVisible = true;
        };
        applyButton.GestureRecognizers.Add(applyTap);

        var cancelTap = new TapGestureRecognizer();
        cancelTap.Tapped += async (_, _) =>
        {
            HapticHelper.Click();
            await cancelButton.AnimateBounceAsync();
            actionsGrid.IsVisible = false;
            statusLabel.Text = "✕ Применение отменено";
            statusLabel.SetDynamicResource(Label.TextColorProperty, "TextSecondary");
            statusLabel.IsVisible = true;
        };
        cancelButton.GestureRecognizers.Add(cancelTap);

        ChatMessagesStack.Children.Add(cardBorder);
    }

    /// <summary>Закрыть открытые оверлеи (панель памяти, чат ИИ, окно экспорта). true, если что-то было открыто.</summary>
    public bool TryDismissOverlays()
    {
        if (MemoryPanel.IsVisible)
        {
            MemoryPanel.IsVisible = false;
            return true;
        }

        if (ChatOverlay.IsVisible)
        {
            MemoryPanel.IsVisible = false;
            ChatOverlay.IsVisible = false;
            if (_isChatMaximized)
            {
                _isChatMaximized = false;
                ChatMaximizeIcon.Icon = "maximize";
                ChatSheet.HeightRequest = _originalChatHeight;
            }
            return true;
        }

        if (ExportOverlay.IsVisible)
        {
            CloseExport();
            return true;
        }

        return false;
    }

    protected override bool OnBackButtonPressed()
        => TryDismissOverlays() || base.OnBackButtonPressed();
}
