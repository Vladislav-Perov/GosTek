/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Интерактивная форма заполнения полей выбранного шаблона с живым предпросмотром А4 по ГОСТ
 * СОДЕРЖИМОЕ: FieldsPage
 * ЗАВИСИМОСТИ: GosTek.Models, GosTek.Services, GosTek.TemplateEngine
 * ==============================================================================
 */
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

    // Поле на экране: само поле ввода, рамка, красная подпись, контейнер и бейдж профиля
    private sealed record FieldUi(
        TemplateField Field,
        InputView Input,
        Border Box,
        Label Error,
        View Container,
        View? ProfileTag = null);

    private TemplateDef? _template;
    private RuleSet? _activeRule;
    private List<PreviewPara> _previewParas = new();
    private readonly List<FieldUi> _fields = new();
    private Dictionary<string, string> _values = new();

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

        View? profileTag = null;
        if (field.Source != ProfileSource.None)
        {
            profileTag = CreateProfileTag();
            profileTag.IsVisible = !string.IsNullOrWhiteSpace(saved);
            labelRow.Children.Add(profileTag);
        }

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
            if (ui.ProfileTag is not null)
                ui.ProfileTag.IsVisible = !string.IsNullOrWhiteSpace(text) && field.Source != ProfileSource.None;
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
        var icon = Icons.CreatePath(Icons.User, size: 10, strokeColorResource: "BrandGreen");
        var text = new Label
        {
            Text = "из профиля",
            FontSize = 10,
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

        var tag = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(6, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = stack,
        };
        tag.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
        return tag;
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
            if (f.ProfileTag is not null)
                f.ProfileTag.IsVisible = true;
            filled++;
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

    // ===== Назад =====
    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    // ===== Собрать документ =====
    private async void OnBuildTapped(object? sender, TappedEventArgs e)
    {
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
}
