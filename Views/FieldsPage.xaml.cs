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

    private FieldUi? _generationField;
    private string? _lastGeneratedText;
    private string? _selectedRevision;
    private string? _pendingDescription;
    private bool _consentDialogOpen;
    private bool _isChatMaximized;
    private double _originalChatHeight = 540;
    private bool _isProgrammaticFilling;
    private bool _insertingScannedText;

    private int _statusVersion;
    private int _layoutVersion;
    private int _renderVersion;
    private bool _isWide;
    private double _renderedWidth;
    private double _font = 13;

    private string _templateId = "";
    private DocumentFormatting? _formatting;

    // Эффективный шрифт документа с учётом ГОСТ-правила и платформы
    private string EffectiveFont
    {
        get
        {
            var font = _formatting?.FontFamily ?? _activeRule?.FontName;
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
        _chatRequestCts?.Cancel();
        _generationField = null;
        _lastGeneratedText = null;
        _selectedRevision = null;
        _pendingDescription = null;
        ChatMessagesStack.Children.Clear();
        _template = TemplateCatalog.Find(_templateId);
        FieldsHost.Children.Clear();
        _fields.Clear();


        if (_template is null)
            return;

        ResetFormatting();
        FormattingPanel.IsVisible = false;
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
        var labelText = GetDisplayLabel(field.Label);
        var label = new Label { Text = labelText, VerticalOptions = LayoutOptions.Center };
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
            box.Content = input;
        }
        else if (SmartInputHelper.IsDateField(field.Label, field.Placeholder, field.Id))
        {
            var entry = new Entry
            {
                Placeholder = string.IsNullOrWhiteSpace(field.Placeholder) ? "ДД.ММ.ГГГГ" : field.Placeholder,
                Text = saved ?? "",
                Keyboard = Keyboard.Numeric,
                MaxLength = 10,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
            };
            entry.StyleClass = new[] { "FieldEntry" };
            input = entry;

            // Навешиваем маску даты ДД.ММ.ГГГГ на ввод
            string prevDate = entry.Text ?? "";
            bool isFormattingDate = false;
            entry.TextChanged += (s, e) =>
            {
                if (isFormattingDate) return;
                isFormattingDate = true;
                try
                {
                    string formatted = SmartInputHelper.ApplyDateMask(e.NewTextValue, prevDate);
                    if (formatted != e.NewTextValue)
                    {
                        int cursor = entry.CursorPosition;
                        entry.Text = formatted;
                        entry.CursorPosition = Math.Min(cursor + (formatted.Length - (e.NewTextValue?.Length ?? 0)), formatted.Length);
                    }
                    prevDate = formatted;
                }
                finally
                {
                    isFormattingDate = false;
                }
            };

            // Системный DatePicker и кнопка вызова календаря
            var picker = new Microsoft.Maui.Controls.DatePicker
            {
                Format = "dd.MM.yyyy",
                Opacity = 0.011,
                WidthRequest = 36,
                HeightRequest = 36,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            picker.DateSelected += (s, e) =>
            {
                string formatted = picker.Date?.ToString("dd.MM.yyyy") ?? "";
                if (!string.IsNullOrEmpty(formatted) && entry.Text != formatted)
                    entry.Text = formatted;
            };

            var calIcon = new Views.Controls.IconView
            {
                Icon = "calendar",
                Size = 19,
                IconColor = "BrandGreen",
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            var calBtn = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(6, 4),
                Content = calIcon,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            var calTap = new TapGestureRecognizer();
            calTap.Tapped += (_, _) =>
            {
                HapticHelper.Click();
                if (SmartInputHelper.TryParseDate(entry.Text, out var dt))
                    picker.Date = dt;
                else
                    picker.Date = DateTime.Today;

                Views.Controls.DateInputField.OpenPicker(picker);
            };
            calBtn.GestureRecognizers.Add(calTap);

            var calHost = new Grid
            {
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children = { calBtn, picker },
            };

            var hybridGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
                ColumnSpacing = 8,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Center,
                Children = { entry, calHost },
            };
            Grid.SetColumn(entry, 0);
            Grid.SetColumn(calHost, 1);

            box.Padding = new Thickness(16, 0);
            box.Content = hybridGrid;
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
            box.Content = input;
        }

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
        if (field.TextGeneration is not null)
        {
            var sparkIcon = new IconView
            {
                Icon = "chat",
                IconColor = "BrandGreen",
                Size = 14,
                VerticalOptions = LayoutOptions.Center
            };
            var sparkLabel = new Label
            {
                Text = "Сгенерировать текст",
                FontSize = 12.5,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current!.Resources["BrandGreen"],
                VerticalOptions = LayoutOptions.Center
            };
            sparkLabel.SetDynamicResource(Label.TextColorProperty, "BrandGreen");

            var generateButton = new Border
            {
                StyleClass = new[] { "HoverCard" },
                BackgroundColor = (Color)Application.Current!.Resources["IconBoxBg"],
                Stroke = (Color)Application.Current!.Resources["CardStroke"],
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Padding = new Thickness(12, 7),
                HorizontalOptions = LayoutOptions.Start,
                Content = new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children = { sparkIcon, sparkLabel }
                }
            };
            generateButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
            generateButton.SetDynamicResource(Border.StrokeProperty, "CardStroke");

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => OpenChat(ui);
            generateButton.GestureRecognizers.Add(tap);

            stack.Children.Insert(1, generateButton);
        }

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

            draft[field.Id] = SmartInputHelper.IsDateField(field.Label, field.Placeholder, field.Id)
                ? SmartInputHelper.FormatDateForStorage(text)
                : (text ?? "");
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
                    value = TemplateEngine.ResolveFromProfile(f.Field.Label, profile);

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

    /// <summary>
    /// Возвращает понятную русскоязычную подпись для стандартных токенов документа.
    /// </summary>
    public static string GetDisplayLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "";

        var trimmed = label.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "fullname" => "ФИО (FullName)",
            "name" => "Имя (Name)",
            "surname" => "Фамилия (Surname)",
            "group" => "Учебная группа (Group)",
            "specialty" => "Специальность (Specialty)",
            "faculty" => "Факультет (Faculty)",
            "course" => "Курс (Course)",
            "studyform" => "Форма обучения (StudyForm)",
            "issuereason" => "Место предъявления (IssueReason)",
            "date" => "Дата документа (Date)",
            "reason" => "Причина / основание (Reason)",
            "deanname" => "Декану / Ректору (DeanName)",
            "rectorname" => "Ректору университета (RectorName)",
            "phone" => "Контактный телефон (Phone)",
            "homeaddress" => "Адрес проживания (HomeAddress)",
            "privilegecategory" => "Льготная категория (PrivilegeCategory)",
            "academicyear" => "Учебный год (AcademicYear)",
            "startdate" => "Дата начала (StartDate)",
            "enddate" => "Дата окончания (EndDate)",
            "attacheddocs" => "Прилагаемые документы (AttachedDocs)",
            "misseddates" => "Период пропусков (MissedDates)",
            "missedclasses" => "Пропущенные занятия (MissedClasses)",
            "scheduledetails" => "График сдачи дисциплин (ScheduleDetails)",
            "deadlinedate" => "Срок ликвидации задолженности (DeadlineDate)",
            "commissionhead" => "Председатель комиссии (CommissionHead)",
            "university" => "Учреждение образования (University)",
            "semester" => "Учебный семестр (Semester)",
            _ => trimmed
        };
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

        if (!_insertingScannedText && DraftStore.PendingOcrText is { Length: > 0 } ocrText)
            _ = InsertScannedTextAsync(ocrText);
    }

    private async Task InsertScannedTextAsync(string text)
    {
        _insertingScannedText = true;
        try
        {
            var fields = _fields.Where(field => field.Input is Editor).ToArray();
            if (fields.Length == 0)
            {
                await Dialogs.AlertAsync("В шаблоне нет многострочного поля",
                    "Распознанный текст готов к вставке в другой шаблон.");
                return;
            }

            var choices = fields.Select((field, index) => $"{index + 1}. {GetDisplayLabel(field.Field.Label)}").ToArray();
            var selected = await DisplayActionSheetAsync("Куда вставить текст скана?", "Отмена", null, choices);
            var index = Array.IndexOf(choices, selected);
            if (index < 0 || !IsCurrentTransfer())
                return;

            var target = fields[index];
            if (!_fields.Contains(target))
                return;

            if (!string.IsNullOrWhiteSpace(target.Input.Text) &&
                !await Dialogs.ConfirmAsync("Заменить введённый текст?",
                    "Распознанный текст заменит текущее значение поля.", "Заменить"))
                return;

            if (!IsCurrentTransfer() || !_fields.Contains(target))
                return;

            target.Input.Text = text;
            DraftStore.PendingOcrText = null;
        }
        finally
        {
            _insertingScannedText = false;
        }

        bool IsCurrentTransfer() => ReferenceEquals(Shell.Current?.CurrentPage, this)
            && ReferenceEquals(DraftStore.PendingOcrText, text);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _chatRequestCts?.Cancel();
        SettingsOverlay.Dismiss();
#if WINDOWS
        if (Shell.Current is { } shell)
        {
            shell.FlyoutBehavior = FlyoutBehavior.Locked;
            shell.FlyoutWidth = global::GosTek.Views.AppTabBar.WindowsRailWidth;
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

        if (FormattingError.IsVisible)
        {
            FormattingPanel.IsVisible = true;
            await FormScroll.ScrollToAsync(FormattingPanel, ScrollToPosition.Start, true);
            return;
        }

        // Все поля заполнены: открываем шторку экспорта
        FileNameEntry.Text = ExportService.DefaultFileName(_template);
        ExportOverlay.IsVisible = true;
    }

    // =====================================================================
    //  ЭКСПОРТ
    // =====================================================================

    private async void OnFormattingClicked(object? sender, EventArgs e)
    {
        FormattingPanel.IsVisible = !FormattingPanel.IsVisible;
        if (FormattingPanel.IsVisible)
            await FormScroll.ScrollToAsync(FormattingPanel, ScrollToPosition.Start, true);
    }

    private void OnFormattingResetClicked(object? sender, EventArgs e) => ResetFormatting();

    private void ResetFormatting()
    {
        FormatFont.ItemsSource = new[] { "Шрифт шаблона", "Times New Roman", "Arial" };
        FormatSpacing.ItemsSource = new[] { "Интервал шаблона", "Одинарный", "1,15", "Полуторный", "Двойной" };
        FormatAlignment.ItemsSource = new[] { "Выравнивание шаблона", "Влево", "По центру", "Вправо" };
        FormatLeft.Text = FormatRight.Text = FormatTop.Text = FormatBottom.Text = FormatIndent.Text = FormatSize.Text = "";
        FormatFont.SelectedIndex = FormatSpacing.SelectedIndex = FormatAlignment.SelectedIndex = 0;
        UpdateFormatting();
    }

    private void OnFormattingChanged(object? sender, EventArgs e)
    {
        if (FormattingError is not null)
            UpdateFormatting();
    }

    private void UpdateFormatting()
    {
        bool invalid = false;
        double? Read(Entry input, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(input.Text))
                return null;
            if (double.TryParse(input.Text.Trim().Replace(',', '.'),
                    System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out double value)
                && value >= min && value <= max)
                return value;

            invalid = true;
            return null;
        }

        var formatting = new DocumentFormatting(
            Read(FormatLeft, 0, 60), Read(FormatRight, 0, 60), Read(FormatTop, 0, 60), Read(FormatBottom, 0, 60),
            FormatFont.SelectedIndex > 0 ? FormatFont.SelectedItem as string : null,
            Read(FormatSize, 8, 32),
            FormatSpacing.SelectedIndex switch { 1 => 1, 2 => 1.15, 3 => 1.5, 4 => 2, _ => null },
            Read(FormatIndent, 0, 30),
            FormatAlignment.SelectedIndex switch { 1 => TextAlignment.Start, 2 => TextAlignment.Center, 3 => TextAlignment.End, _ => null });

        FormattingError.IsVisible = invalid;
        if (invalid)
            return;

        _formatting = formatting == new DocumentFormatting(null, null, null, null, null, null, null, null, null)
            ? null : formatting;
        QueueRender();
    }

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
            var result = await ExportService.SaveDocxAsync(_template, _values, ExportName(), _formatting);
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
            var result = await ExportService.SaveDocxAsync(_template, _values, ExportName(), _formatting);
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

    // PDF: локальный экспорт через движок текущей платформы.
    private async void OnExportPdfTapped(object? sender, EventArgs e)
    {
        if (_template is null)
            return;

        try
        {
            var result = await ExportService.SavePdfAsync(_template, _values, ExportName(), _formatting);
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
        double leftCm = _formatting?.LeftMm / 10 ?? _activeRule?.Left.Cm ?? 3.0;
        double rightCm = _formatting?.RightMm / 10 ?? _activeRule?.Right.Cm ?? 1.5;
        double topCm = _formatting?.TopMm / 10 ?? _activeRule?.Top.Cm ?? 2.0;
        double bottomCm = _formatting?.BottomMm / 10 ?? _activeRule?.Bottom.Cm ?? 2.0;

        sheet.Padding = new Thickness(
            leftCm * cmScale,
            topCm * cmScale,
            rightCm * cmScale,
            bottomCm * cmScale);

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

            double fontSize = Math.Clamp(_font * (_formatting?.FontSize ?? para.Pt) / 14, 6, 42);
            if (_formatting?.FirstLineMm is > 0 and var indent)
            {
                int spaces = (int)Math.Round(indent * w / 210 / (fontSize / 4));
                if (spaces > 0)
                    spans = [T(new string('\u00a0', spaces)), ..spans];
            }

            var label = Para(_formatting?.Alignment ?? para.Align, spans);
            label.FontSize = fontSize;
            if (_formatting?.LineSpacing is { } lineSpacing)
                label.LineHeight = lineSpacing;
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

    private void OpenChat(FieldUi field)
    {
        HapticHelper.Click();
        if (!ReferenceEquals(_generationField, field))
        {
            _chatRequestCts?.Cancel();
            _generationField = field;
            _lastGeneratedText = null;
            _selectedRevision = null;
            _pendingDescription = null;
            ChatInput.Text = string.Empty;
            ChatMessagesStack.Children.Clear();
        }

        ChatFieldLabel.Text = GetDisplayLabel(field.Field.Label);
        _originalChatHeight = DeviceInfo.Platform == DevicePlatform.Android
            ? Math.Min(520, Math.Max(360, (Height > 0 ? Height : 700) * 0.75))
            : 560;
        ApplyChatCompactLayout();
        ChatOverlay.IsVisible = true;
        ChatSheet.TranslationY = 700;
        _ = ChatSheet.TranslateToAsync(0, 0, 250, Easing.CubicOut);
        ChatInput.Focus();
    }

    private void ApplyChatCompactLayout()
    {
        _isChatMaximized = false;
        ChatMaximizeIcon.Icon = "maximize";
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            ChatSheet.HorizontalOptions = LayoutOptions.Fill;
            ChatSheet.VerticalOptions = LayoutOptions.End;
            ChatSheet.WidthRequest = -1;
            ChatSheet.MaximumWidthRequest = 700;
            ChatSheet.HeightRequest = _originalChatHeight;
            ChatSheet.Margin = new Thickness(0);
            ChatSheet.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(24, 24, 0, 0)
            };
        }
        else
        {
            ChatSheet.HorizontalOptions = LayoutOptions.End;
            ChatSheet.VerticalOptions = LayoutOptions.End;
            ChatSheet.WidthRequest = 490;
            ChatSheet.MaximumWidthRequest = 500;
            ChatSheet.HeightRequest = _originalChatHeight;
            ChatSheet.Margin = new Thickness(0, 0, 24, 24);
            ChatSheet.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(20)
            };
        }
    }

    private void ApplyChatMaximizedLayout()
    {
        _isChatMaximized = true;
        ChatMaximizeIcon.Icon = "minimize";
        ChatSheet.HorizontalOptions = LayoutOptions.Fill;
        ChatSheet.VerticalOptions = LayoutOptions.Fill;
        ChatSheet.WidthRequest = -1;
        ChatSheet.MaximumWidthRequest = double.PositiveInfinity;
        ChatSheet.HeightRequest = -1;
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            ChatSheet.Margin = new Thickness(0);
            ChatSheet.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(0)
            };
        }
        else
        {
            ChatSheet.Margin = new Thickness(16, 12, 16, 16);
            ChatSheet.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = new CornerRadius(16)
            };
        }
    }

    private async void OnToggleChatMaximizeTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();

        if (!_isChatMaximized)
        {
            _isChatMaximized = true;
            ChatMaximizeIcon.Icon = "minimize";
            ChatSheet.MaximumWidthRequest = double.PositiveInfinity;

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                double startH = ChatSheet.Height > 0 ? ChatSheet.Height : _originalChatHeight;
                double endH = ChatOverlay.Height > 100 ? ChatOverlay.Height : (this.Height > 100 ? this.Height : 700);

                var anim = new Animation(v => ChatSheet.HeightRequest = v, startH, endH, Easing.CubicOut);
                anim.Commit(ChatSheet, "ChatMaximize", length: 250, finished: (_, __) =>
                {
                    ApplyChatMaximizedLayout();
                });
            }
            else
            {
                double startW = ChatSheet.Width > 0 ? ChatSheet.Width : 490;
                double startH = ChatSheet.Height > 0 ? ChatSheet.Height : _originalChatHeight;
                double endW = (ChatOverlay.Width > 100 ? ChatOverlay.Width : this.Width) - 32;
                double endH = (ChatOverlay.Height > 100 ? ChatOverlay.Height : this.Height) - 28;

                if (endW < 500) endW = 500;
                if (endH < 560) endH = 560;

                ChatSheet.HorizontalOptions = LayoutOptions.End;
                ChatSheet.VerticalOptions = LayoutOptions.End;

                var anim = new Animation();
                anim.Add(0, 1, new Animation(w => ChatSheet.WidthRequest = w, startW, endW, Easing.CubicOut));
                anim.Add(0, 1, new Animation(h => ChatSheet.HeightRequest = h, startH, endH, Easing.CubicOut));
                anim.Commit(ChatSheet, "ChatMaximize", length: 250, finished: (_, __) =>
                {
                    ApplyChatMaximizedLayout();
                });
            }
        }
        else
        {
            _isChatMaximized = false;
            ChatMaximizeIcon.Icon = "maximize";

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                double startH = ChatSheet.Height > 0 ? ChatSheet.Height : 700;
                double endH = _originalChatHeight;

                var anim = new Animation(v => ChatSheet.HeightRequest = v, startH, endH, Easing.CubicOut);
                anim.Commit(ChatSheet, "ChatMinimize", length: 250, finished: (_, __) =>
                {
                    ApplyChatCompactLayout();
                });
            }
            else
            {
                double startW = ChatSheet.Width > 0 ? ChatSheet.Width : 800;
                double startH = ChatSheet.Height > 0 ? ChatSheet.Height : 700;
                double endW = 490;
                double endH = _originalChatHeight;

                ChatSheet.HorizontalOptions = LayoutOptions.End;
                ChatSheet.VerticalOptions = LayoutOptions.End;
                ChatSheet.Margin = new Thickness(0, 0, 24, 24);

                var anim = new Animation();
                anim.Add(0, 1, new Animation(w => ChatSheet.WidthRequest = w, startW, endW, Easing.CubicOut));
                anim.Add(0, 1, new Animation(h => ChatSheet.HeightRequest = h, startH, endH, Easing.CubicOut));
                anim.Commit(ChatSheet, "ChatMinimize", length: 250, finished: (_, __) =>
                {
                    ApplyChatCompactLayout();
                });
            }
        }
    }

    private CancellationTokenSource? _chatRequestCts;

    private void OnCancelChatRequestTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        _chatRequestCts?.Cancel();
    }

    private async void OnCloseChatTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        _chatRequestCts?.Cancel();

        await ChatSheet.TranslateToAsync(0, 700, 200, Easing.CubicIn);
        ChatOverlay.IsVisible = false;
        ApplyChatCompactLayout();
    }

    private async void OnChatOverlayBackgroundTapped(object? sender, TappedEventArgs e)
    {
        _chatRequestCts?.Cancel();

        await ChatSheet.TranslateToAsync(0, 700, 200, Easing.CubicIn);
        ChatOverlay.IsVisible = false;
        ApplyChatCompactLayout();
    }

    private void OnChatSheetTapped(object? sender, TappedEventArgs e)
    {
        // Предотвращаем закрытие шторки при клике на саму панель
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
        var message = ChatInput.Text?.Trim();
        var field = _generationField;
        if (string.IsNullOrWhiteSpace(message) || field?.Field.TextGeneration is not { } kind ||
            _chatRequestCts is not null || _consentDialogOpen)
            return;

        if (!SettingsService.TextGenerationConsentAccepted)
        {
            _consentDialogOpen = true;
            try
            {
                if (!await Dialogs.ConfirmAsync("Облачная генерация текста",
                    "Сервис получает отправленное сообщение и при правке предыдущий вариант из чата. " +
                    "GosTek маскирует распознанные реквизиты. Не вводите конфиденциальные сведения. " +
                    "Поля документа, профиль и сканы остаются в приложении.",
                    "Разрешить", DialogKind.Info))
                    return;

                SettingsService.TextGenerationConsentAccepted = true;
            }
            finally
            {
                _consentDialogOpen = false;
            }
        }

        if (!ChatOverlay.IsVisible || !ReferenceEquals(_generationField, field))
            return;

        var description = _pendingDescription is null
            ? message : $"{_pendingDescription}\nУточнение: {message}";
        if (description.Length > TextGenerationService.MaxMessageLength)
        {
            AddErrorMessageBubble("Опишите ситуацию короче, чтобы отправить запрос.");
            _pendingDescription = null;
            return;
        }

        using var request = new CancellationTokenSource();
        _chatRequestCts = request;
        ChatInput.Text = string.Empty;
        AddUserMessageBubble(message);
        TypingIndicator.IsVisible = true;
        SendButton.IsEnabled = false;
        try
        {
            var previousText = _selectedRevision;
            if (previousText is null && IsRevisionRequest(message))
                previousText = _lastGeneratedText;

            var result = await TextGenerationService.Instance.GenerateAsync(description, kind,
                previousText, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_generationField, field) || !_fields.Contains(field))
                return;

            if (result.Question is { } question)
            {
                _pendingDescription = description;
                _selectedRevision = previousText;
                AddBotMessageBubble(question);
                return;
            }

            var text = result.Text!;
            _pendingDescription = null;
            _lastGeneratedText = text;
            _selectedRevision = null;
            ChatInput.Placeholder = "Опишите ситуацию или попросите изменить текст";
            AddBotMessageBubble(text);
            AddTextInsertionAction(field, text);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
        }
        catch (AiAssistantException ex)
        {
            AddErrorMessageBubble(ex.Message);
            if (string.IsNullOrWhiteSpace(ChatInput.Text))
                ChatInput.Text = message;
        }
        finally
        {
            _chatRequestCts = null;
            TypingIndicator.IsVisible = false;
            SendButton.IsEnabled = true;
            _ = ChatScrollView.ScrollToAsync(ChatMessagesStack, ScrollToPosition.End, true);
        }
    }

    private static bool IsRevisionRequest(string message) => System.Text.RegularExpressions.Regex.IsMatch(
        message,
        @"^(?:сделай\s+(?:его\s+|текст\s+)?(?:короче|длиннее|официальнее|проще)|сократи|перефразируй|перепиши|убери|измени|shorten|rewrite|rephrase)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private void AddTextInsertionAction(FieldUi field, string text)
    {
        var previousText = field.Input.Text;
        var inserted = string.IsNullOrWhiteSpace(previousText);
        if (inserted)
            field.Input.Text = text;

        var action = new Button
        {
            Text = inserted ? "Отменить вставку" : "Заменить текст",
            HorizontalOptions = LayoutOptions.Start,
        };
        action.Clicked += async (_, _) =>
        {
            if (!ReferenceEquals(_generationField, field) || !_fields.Contains(field))
                return;

            var currentText = field.Input.Text;
            if (inserted)
            {
                if (string.Equals(currentText, text, StringComparison.Ordinal))
                    field.Input.Text = previousText;
                action.IsEnabled = false;
                return;
            }

            action.IsEnabled = false;
            try
            {
                if (!string.IsNullOrWhiteSpace(currentText) && !await Dialogs.ConfirmAsync(
                    "Заменить введённый текст?", "В поле будет вставлен вариант из чата.", "Заменить"))
                    return;
                if (!ReferenceEquals(_generationField, field) || !_fields.Contains(field) ||
                    !string.Equals(field.Input.Text, currentText, StringComparison.Ordinal))
                    return;

                previousText = currentText;
                field.Input.Text = text;
                inserted = true;
                action.Text = "Отменить вставку";
            }
            finally
            {
                action.IsEnabled = true;
            }
        };
        var edit = new Button { Text = "Править этот вариант", HorizontalOptions = LayoutOptions.Start };
        edit.Clicked += (_, _) =>
        {
            if (!ReferenceEquals(_generationField, field) || _chatRequestCts is not null)
                return;

            _selectedRevision = text;
            _pendingDescription = null;
            ChatInput.Placeholder = "Что изменить в этом варианте?";
            ChatInput.Focus();
        };
        ChatMessagesStack.Children.Add(new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Children = { action, edit }
        });
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
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "ActionBg");
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

    /// <summary>Закрыть открытый чат или окно экспорта.</summary>
    public bool TryDismissOverlays()
    {
        if (ChatOverlay.IsVisible)
        {
            _chatRequestCts?.Cancel();
            ChatOverlay.IsVisible = false;
            ApplyChatCompactLayout();
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
