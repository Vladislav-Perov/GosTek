using GosTek.Models;
using Microsoft.Maui.Controls.Shapes;

// В Shapes есть одноимённый Path
using Path = System.IO.Path;

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

    // Абзацный отступ (три длинных пробела)
    private const string Indent = "\u2003\u2003\u2003";

    // Поле на экране: само поле ввода, рамка, красная подпись и контейнер (для прокрутки)
    private sealed record FieldUi(TemplateField Field, InputView Input, Border Box, Label Error, View Container);

    private TemplateDef? _template;
    private readonly List<FieldUi> _fields = new();
    private Dictionary<string, string> _values = new();

    private int _statusVersion;
    private int _layoutVersion;
    private int _renderVersion;
    private bool _isWide;
    private double _renderedWidth;
    private double _font = 13;

    private string _templateId = "";

    // Shell передаёт сюда id из адреса FieldsPage?id=...
    public string TemplateId
    {
        get => _templateId;
        set {
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

        var draft = DraftStore.For(_template.Id);
        _values = draft;

        foreach (var field in _template.Fields) {
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
        if (field.Source != ProfileSource.None)
            labelRow.Children.Add(CreateProfileTag());

        // Рамка + поле ввода
        var box = new Border();
        box.StyleClass = new[] { "FieldBox" };

        InputView input;
        if (field.Multiline) {
            var editor = new Editor {
                Placeholder = field.Placeholder,
                Text = saved ?? "",
                AutoSize = EditorAutoSizeOption.TextChanges,
            };
            editor.StyleClass = new[] { "FieldEditor" };
            box.Padding = new Thickness(16, 8);
            input = editor;
        } else {
            var entry = new Entry {
                Placeholder = field.Placeholder,
                Text = saved ?? "",
            };
            entry.StyleClass = new[] { "FieldEntry" };
            input = entry;
        }
        box.Content = input;

        // Красная подпись под полем
        var error = new Label {
            Text = "Заполните поле",
            FontSize = 12,
            TextColor = ThemeService.ErrorColor,
            IsVisible = false,
        };

        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(labelRow);
        stack.Children.Add(box);
        stack.Children.Add(error);

        var ui = new FieldUi(field, input, box, error, stack);

        // Текст изменился: сохранить в черновик, убрать подсветку, обновить лист
        void Changed(string? text)
        {
            draft[field.Id] = text ?? "";
            if (!IsEmpty(ui))
                Mark(ui, false);
            QueueRender();
        }

        if (input is Editor ed)
            ed.TextChanged += (_, e) => Changed(e.NewTextValue);
        else if (input is Entry en)
            en.TextChanged += (_, e) => Changed(e.NewTextValue);

        // Ушли из пустого поля: подсветить
        input.Unfocused += (_, _) => Mark(ui, IsEmpty(ui));

        return ui;
    }

    private static View CreateProfileTag()
    {
        var text = new Label {
            Text = "из профиля",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
        };
        text.SetDynamicResource(Label.TextColorProperty, "BrandGreen");

        var tag = new Border {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 2),
            VerticalOptions = LayoutOptions.Center,
            Content = text,
        };
        tag.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
        return tag;
    }

    // ===== Пустое поле: подсветка =====
    private static bool IsEmpty(FieldUi f) => string.IsNullOrWhiteSpace(f.Input.Text);

    private static void Mark(FieldUi f, bool invalid)
    {
        if (invalid) {
            f.Box.Stroke = new SolidColorBrush(ThemeService.ErrorColor);
            f.Box.StrokeThickness = 1.5;
        } else {
            // Вернуть рамку из стиля FieldBox (цвет подхватывает тему)
            f.Box.ClearValue(Border.StrokeProperty);
            f.Box.ClearValue(Border.StrokeThicknessProperty);
        }
        f.Error.IsVisible = invalid;
    }

    // ===== Подстановка из профиля =====
    // onlyEmpty = true: заполняем только пустые поля (при открытии окна)
    // onlyEmpty = false: перезаписываем все поля из профиля (кнопка)
    private int FillFromProfile(ProfileData profile, bool onlyEmpty)
    {
        int filled = 0;

        foreach (var f in _fields) {
            if (f.Field.Source == ProfileSource.None)
                continue;
            if (onlyEmpty && !IsEmpty(f))
                continue;

            var value = TemplateCatalog.FromProfile(f.Field.Source, profile);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            f.Input.Text = value;   // TextChanged сохранит значение, уберёт подсветку, обновит лист
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
        if (_template is null)
            return;

        // Подсвечиваем все пустые поля и переходим к первому
        var empty = _fields.Where(IsEmpty).ToList();
        if (empty.Count > 0) {
            foreach (var f in empty)
                Mark(f, true);

            await FormScroll.ScrollToAsync(empty[0].Container, ScrollToPosition.Center, true);
            empty[0].Input.Focus();
            return;
        }

        // Все поля заполнены: открываем шторку экспорта
        FileNameEntry.Text = DefaultFileName();
        ExportOverlay.IsVisible = true;
    }

    // =====================================================================
    //  ЭКСПОРТ
    // =====================================================================

    private void CloseExport() => ExportOverlay.IsVisible = false;

    private void OnExportCloseTapped(object? sender, TappedEventArgs e) => CloseExport();

    // Нажатие на саму шторку не должно её закрывать (жест перехватывает нажатие у подложки)
    private void OnSheetTapped(object? sender, TappedEventArgs e) { }

    // Имя по умолчанию зависит от настройки: «Шаблон» или «Шаблон_2026-10-10»
    private string DefaultFileName()
    {
        var name = _template!.Title.Replace(' ', '_');

        return SettingsService.Name == NameMode.TemplateDate
            ? $"{name}_{DateTime.Today:yyyy-MM-dd}"
            : name;
    }

    private string SafeFileName()
    {
        var name = (FileNameEntry.Text ?? "").Trim();

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];

        return string.IsNullOrWhiteSpace(name) ? DefaultFileName() : name;
    }

    // Собирает и сохраняет .docx с учётом настройки «Если файл уже есть».
    // Возвращает null, если пользователь отказался (шторка остаётся открытой).
    private async Task<SaveResult?> SaveDocxAsync()
    {
        var fileName = SafeFileName() + ".docx";
        bool overwrite = false;

        if (FileSaveService.Exists(fileName)) {
            var mode = SettingsService.Conflict;

            if (mode == ConflictMode.Ask) {
                var answer = await DisplayActionSheetAsync(
                    $"Файл «{fileName}» уже есть", "Отмена", null, "Добавить номер", "Заменить");

                if (answer == "Добавить номер")
                    mode = ConflictMode.Number;
                else if (answer == "Заменить")
                    mode = ConflictMode.Overwrite;
                else
                    return null;
            }

            if (mode == ConflictMode.Overwrite)
                overwrite = true;
            else
                fileName = FileSaveService.FreeName(fileName);
        }

        return FileSaveService.Save(_template!, _values, fileName, overwrite);
    }

    private static Task ShareFileAsync(string path)
        => Share.Default.RequestAsync(new ShareFileRequest {
            Title = "Документ",
            File = new ShareFile(path),
        });

    // Что делать после сохранения: по настройке или спросить
    private async Task AfterSaveAsync(SaveResult result)
    {
        var action = SettingsService.AfterSave;

        if (action == AfterSaveAction.Ask) {
            var choice = await DisplayActionSheetAsync(
                $"✅ Сохранено: {result.Location}", "Закрыть", null, "Открыть", "Поделиться");

            action = choice switch {
                "Открыть" => AfterSaveAction.Open,
                "Поделиться" => AfterSaveAction.Share,
                _ => AfterSaveAction.Nothing,
            };

            if (action == AfterSaveAction.Nothing)
                return;
        }

        switch (action) {
            case AfterSaveAction.Open:
                await Launcher.Default.OpenAsync(new OpenFileRequest("Документ", new ReadOnlyFile(result.FilePath)));
                break;

            case AfterSaveAction.Share:
                await ShareFileAsync(result.FilePath);
                break;

            default:
                await DisplayAlertAsync("✅ Сохранено", result.Location, "Понятно");
                break;
        }
    }

    private async void OnExportDocxTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try {
            var result = await SaveDocxAsync();
            if (result is null)
                return;

            CloseExport();
            await AfterSaveAsync(result);
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось сохранить", ex.Message, "Понятно");
        }
    }

    // «Поделиться»: файл тоже сохраняется (по тем же правилам), затем открывается системное окно отправки
    private async void OnExportShareTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try {
            var result = await SaveDocxAsync();
            if (result is null)
                return;

            CloseExport();
            await ShareFileAsync(result.FilePath);
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось поделиться", ex.Message, "Понятно");
        }
    }

    // Заглушка: PDF пока не делаем
    private async void OnExportPdfTapped(object? sender, EventArgs e)
    {
        await DisplayAlertAsync("📕 PDF",
            "Сохранение в PDF добавим следующим шагом. Пока можно собрать Word и распечатать из него.",
            "Понятно");
    }

    // =====================================================================
    //  ПРЕДПРОСМОТР
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
        sheet.Padding = new Thickness(w * 0.13, w * 0.09, w * 0.06, w * 0.08);   // слева поле шире
        sheet.Content = BuildSheet(w);
    }

    private View BuildSheet(double w)
    {
        double contentW = w * 0.81;
        var rightBlock = new Thickness(contentW * 0.42, 0, 0, _font * 1.4);   // блок «Кому / от кого»

        return _template!.Id switch {
            "explanatory" => BuildExplanatory(rightBlock),
            "ip" => BuildIp(rightBlock),
            "title" => BuildTitle(w),
            _ => new Label { Text = _template.Title, TextColor = Ink },
        };
    }

    private View BuildExplanatory(Thickness rightBlock)
    {
        var stack = new VerticalStackLayout { Spacing = _font * 0.6 };

        var to = Para(TextAlignment.Start, Val("pos"), T("\n"), Val("org"), T("\nот "), Val("fio"));
        to.Margin = rightBlock;
        stack.Children.Add(to);

        stack.Children.Add(Heading("Объяснительная записка"));

        var subject = Val("subject");
        subject.FontAttributes = FontAttributes.Italic;
        stack.Children.Add(Para(TextAlignment.Center, subject));

        // Текст объяснения: каждый абзац отдельно
        _values.TryGetValue("text", out var text);
        var paragraphs = (text ?? "").Split(new[] { '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (paragraphs.Length == 0)
            stack.Children.Add(Para(TextAlignment.Start, T(Indent), Val("text")));
        else
            foreach (var p in paragraphs)
                stack.Children.Add(Para(TextAlignment.Start, T(Indent + p)));

        stack.Children.Add(BuildSignature());
        return stack;
    }

    private View BuildIp(Thickness rightBlock)
    {
        var stack = new VerticalStackLayout { Spacing = _font * 0.6 };

        var to = Para(TextAlignment.Start,
            Val("organ"), T("\nот "), Val("fio"), T("\nпаспорт "), Val("passport"));
        to.Margin = rightBlock;
        stack.Children.Add(to);

        stack.Children.Add(Heading("ЗАЯВЛЕНИЕ"));

        stack.Children.Add(Para(TextAlignment.Start,
            T(Indent + "Прошу зарегистрировать меня в качестве индивидуального предпринимателя. Адрес места жительства: "),
            Val("addr"),
            T(". Предполагаемый вид деятельности: "),
            Val("activity"),
            T(".")));

        stack.Children.Add(BuildSignature());
        return stack;
    }

    private View BuildTitle(double w)
    {
        var g = new Grid { RowSpacing = _font * 0.2 };
        g.MinimumHeightRequest = w * 297 / 210 - w * 0.17;   // высота листа минус верхнее и нижнее поля

        // Строка 4 (тема) занимает всё свободное место и центрируется по высоте
        for (int i = 0; i < 7; i++)
            g.RowDefinitions.Add(new RowDefinition(i == 4 ? GridLength.Star : GridLength.Auto));

        void Put(View view, int row)
        {
            Grid.SetRow(view, row);
            g.Children.Add(view);
        }

        Put(Para(TextAlignment.Center, T("Министерство образования Республики Беларусь")), 0);

        var univ = Val("univ");
        univ.FontAttributes = FontAttributes.Bold;
        Put(Para(TextAlignment.Center, univ), 1);

        Put(Para(TextAlignment.Center, T("Факультет "), Val("faculty")), 2);
        Put(Para(TextAlignment.Center, T("Кафедра "), Val("dept")), 3);

        var mid = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = _font * 0.4 };
        mid.Children.Add(Heading("КУРСОВАЯ РАБОТА"));
        mid.Children.Add(Para(TextAlignment.Center, T("Тема: "), Val("topic")));
        Put(mid, 4);

        var who = Para(TextAlignment.End,
            T("Выполнил: "), Val("student"), T("\nРуководитель: "), Val("head"));
        who.Margin = new Thickness(0, 0, 0, _font * 1.7);
        Put(who, 5);

        Put(Para(TextAlignment.Center, T("Минск, "), Val("year")), 6);
        return g;
    }

    // Дата слева, подпись справа
    private View BuildSignature()
    {
        _values.TryGetValue("fio", out var fio);

        var who = string.IsNullOrWhiteSpace(fio)
            ? new Span { Text = "подпись", TextColor = GapText, BackgroundColor = GapBg }
            : T(TemplateCatalog.ShortName(fio));

        var grid = new Grid { Margin = new Thickness(0, _font * 1.6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var date = Para(TextAlignment.Start, T(DateTime.Today.ToString("dd.MM.yyyy")));
        var sign = Para(TextAlignment.End, T("____________ "), who);
        Grid.SetColumn(sign, 1);

        grid.Children.Add(date);
        grid.Children.Add(sign);
        return grid;
    }

    // ===== Помощники для листа =====

    private TemplateField FieldOf(string id) => _template!.Fields.First(f => f.Id == id);

    // Значение поля. Если пусто, серая плашка с подсказкой
    private Span Val(string id)
    {
        _values.TryGetValue(id, out var v);

        if (!string.IsNullOrWhiteSpace(v))
            return new Span { Text = v.Trim() };

        return new Span {
            Text = FieldOf(id).Placeholder,
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

        return new Label {
            FormattedText = formatted,
            TextColor = Ink,
            FontFamily = SerifFont,
            FontSize = _font,
            HorizontalTextAlignment = align,
            LineBreakMode = LineBreakMode.WordWrap,
        };
    }

    private Label Heading(string text) => new() {
        Text = text,
        FontAttributes = FontAttributes.Bold,
        FontSize = _font + 2,
        CharacterSpacing = 1,
        HorizontalTextAlignment = TextAlignment.Center,
        TextColor = Ink,
        FontFamily = SerifFont,
        Margin = new Thickness(0, _font * 0.5, 0, 0),
    };
}