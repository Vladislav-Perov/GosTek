/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис инициализации и копирования эталонных шаблонов ГОСТ при первом запуске приложения
 * СОДЕРЖИМОЕ: TemplateSeeder
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models.TemplateCatalog, GosTek.SettingsService
 * ==============================================================================
 */
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;

namespace GosTek.Services;

/// <summary>
/// Автоматически развертывает базовые и студенческие шаблоны .docx при первом запуске приложения
/// или при добавлении новых типовых документов в каталог пользователя.
/// </summary>
public static class TemplateSeeder
{
    // Базовые служебные шаблоны
    public const string VacationRawAsset = "vacation_template.docx";
    public const string ContractRawAsset = "contract_template.docx";
    public const string ExplanationRawAsset = "explanation_template.docx";

    public const string VacationTargetName = "Заявления_Заявление_на_отпуск.docx";
    public const string ContractTargetName = "Договоры_Договор_купли_продажи.docx";
    public const string ExplanationTargetName = "Служебные_Объяснительная_записка.docx";

    // Студенческие шаблоны по ГОСТ Республики Беларусь (Фаза 13)
    public const string StudyCertificateRawAsset = "study_certificate_template.docx";
    public const string FinancialAidRawAsset = "financial_aid_template.docx";
    public const string DormitoryRawAsset = "dormitory_template.docx";
    public const string AcademicLeaveRawAsset = "academic_leave_template.docx";
    public const string StudentExplanationRawAsset = "student_explanation_template.docx";
    public const string IndividualScheduleRawAsset = "individual_schedule_template.docx";

    public const string StudyCertificateTargetName = "Учёба_Справка с места учёбы.docx";
    public const string FinancialAidTargetName = "Учёба_Заявление на материальную помощь.docx";
    public const string DormitoryTargetName = "Учёба_Заявление на общежитие.docx";
    public const string AcademicLeaveTargetName = "Учёба_Заявление на академический отпуск.docx";
    public const string StudentExplanationTargetName = "Учёба_Объяснительная записка в деканат.docx";
    public const string IndividualScheduleTargetName = "Учёба_Индивидуальный график сдачи.docx";

    public static readonly (string RawAsset, string TargetFileName)[] DefaultTemplates =
    {
        (VacationRawAsset, VacationTargetName),
        (ContractRawAsset, ContractTargetName),
        (ExplanationRawAsset, ExplanationTargetName),
        (StudyCertificateRawAsset, StudyCertificateTargetName),
        (FinancialAidRawAsset, FinancialAidTargetName),
        (DormitoryRawAsset, DormitoryTargetName),
        (AcademicLeaveRawAsset, AcademicLeaveTargetName),
        (StudentExplanationRawAsset, StudentExplanationTargetName),
        (IndividualScheduleRawAsset, IndividualScheduleTargetName),

        // Дополнительные шаблоны (из библиотеки примеров)
        ("tpl_it_dogovor_na_razrabotku_programmnogo_obespecheniya.docx", "IT_Договор_на_разработку_программного_обеспечения.docx"),
        ("tpl_it_tehnicheskoe_zadanie.docx", "IT_Техническое_задание.docx"),
        ("tpl_avto_dogovor_kupli_prodazhi_avtomobilya.docx", "Авто_Договор_купли-продажи_автомобиля.docx"),
        ("tpl_biznes_kommercheskoe_predlozhenie.docx", "Бизнес_Коммерческое_предложение.docx"),
        ("tpl_biznes_protokol_obschego_sobraniya_uchastnikov.docx", "Бизнес_Протокол_общего_собрания_участников.docx"),
        ("tpl_biznes_soglashenie_o_nerazglashenii.docx", "Бизнес_Соглашение_о_неразглашении.docx"),
        ("tpl_grazhdanin_doverennost_na_predstavlenie_interesov.docx", "Гражданин_Доверенность_на_представление_интересов.docx"),
        ("tpl_grazhdanin_zhaloba.docx", "Гражданин_Жалоба.docx"),
        ("tpl_grazhdanin_zayavlenie_o_vydache_spravki.docx", "Гражданин_Заявление_о_выдаче_справки.docx"),
        ("tpl_grazhdanin_obraschenie_v_gosudarstvennyy_organ.docx", "Гражданин_Обращение_в_государственный_орган.docx"),
        ("tpl_grazhdanin_raspiska_o_poluchenii_deneg.docx", "Гражданин_Расписка_о_получении_денег.docx"),
        ("tpl_kadry_avansovyy_otchyot.docx", "Кадры_Авансовый_отчёт.docx"),
        ("tpl_kadry_grafik_otpuskov.docx", "Кадры_График_отпусков.docx"),
        ("tpl_kadry_dolzhnostnaya_instruktsiya.docx", "Кадры_Должностная_инструкция.docx"),
        ("tpl_kadry_trudovoy_dogovor.docx", "Кадры_Трудовой_договор.docx"),
        ("tpl_nedvizhimost_akt_priyoma_peredachi.docx", "Недвижимость_Акт_приёма-передачи.docx"),
        ("tpl_nedvizhimost_dogovor_arendy_zhilogo_pomescheniya.docx", "Недвижимость_Договор_аренды_жилого_помещения.docx"),
        ("tpl_organizatsiya_doverennost.docx", "Организация_Доверенность.docx"),
        ("tpl_organizatsiya_obyasnitelnaya_zapiska.docx", "Организация_Объяснительная_записка.docx"),
        ("tpl_organizatsiya_prikaz_o_predostavlenii_otpuska.docx", "Организация_Приказ_о_предоставлении_отпуска.docx"),
        ("tpl_organizatsiya_prikaz_o_priyome_na_rabotu.docx", "Организация_Приказ_о_приёме_на_работу.docx"),
        ("tpl_organizatsiya_prikaz_ob_uvolnenii.docx", "Организация_Приказ_об_увольнении.docx"),
        ("tpl_organizatsiya_sluzhebnaya_zapiska.docx", "Организация_Служебная_записка.docx"),
        ("tpl_organizatsiya_spravka_s_mesta_raboty.docx", "Организация_Справка_с_места_работы.docx"),
        ("tpl_predprinimatel_akt_vypolnennyh_rabot.docx", "Предприниматель_Акт_выполненных_работ.docx"),
        ("tpl_predprinimatel_dogovor_okazaniya_uslug.docx", "Предприниматель_Договор_оказания_услуг.docx"),
        ("tpl_predprinimatel_zayavlenie_o_prekraschenii_deyatelnosti_ip.docx", "Предприниматель_Заявление_о_прекращении_деятельности_ИП.docx"),
        ("tpl_predprinimatel_zayavlenie_o_priostanovlenii_deyatelnosti.docx", "Предприниматель_Заявление_о_приостановлении_деятельности.docx"),
        ("tpl_predprinimatel_zayavlenie_o_registratsii_ip.docx", "Предприниматель_Заявление_о_регистрации_ИП.docx"),
        ("tpl_predprinimatel_zayavlenie_ob_izmenenii_svedeniy_ob_ip.docx", "Предприниматель_Заявление_об_изменении_сведений_об_ИП.docx"),
        ("tpl_predprinimatel_schyot_na_oplatu.docx", "Предприниматель_Счёт_на_оплату.docx"),
        ("tpl_prepodavatel_zayavlenie_o_napravlenii_na_povyshenie_kvalifikatsii.docx", "Преподаватель_Заявление_о_направлении_на_повышение_квалификации.docx"),
        ("tpl_prepodavatel_kalendarno_tematicheskiy_plan.docx", "Преподаватель_Календарно-тематический_план.docx"),
        ("tpl_prepodavatel_otzyv_na_kursovuyu_rabotu.docx", "Преподаватель_Отзыв_на_курсовую_работу.docx"),
        ("tpl_prepodavatel_otzyv_rukovoditelya_na_diplomnuyu_rabotu.docx", "Преподаватель_Отзыв_руководителя_на_дипломную_работу.docx"),
        ("tpl_prepodavatel_raport_o_perenose_zanyatiya.docx", "Преподаватель_Рапорт_о_переносе_занятия.docx"),
        ("tpl_prepodavatel_harakteristika_na_studenta.docx", "Преподаватель_Характеристика_на_студента.docx"),
        ("tpl_prepodavatel_ekzamenatsionnaya_vedomost.docx", "Преподаватель_Экзаменационная_ведомость.docx"),
        ("tpl_rabotnik_zayavlenie_na_otpusk.docx", "Работник_Заявление_на_отпуск.docx"),
        ("tpl_rabotnik_zayavlenie_na_otpusk_bez_sohraneniya_zarabotnoy_platy.docx", "Работник_Заявление_на_отпуск_без_сохранения_заработной_платы.docx"),
        ("tpl_rabotnik_zayavlenie_o_priyome_na_rabotu.docx", "Работник_Заявление_о_приёме_на_работу.docx"),
        ("tpl_rabotnik_zayavlenie_ob_uvolnenii_po_sobstvennomu_zhelaniyu.docx", "Работник_Заявление_об_увольнении_по_собственному_желанию.docx"),
        ("tpl_semya_dogovor_dareniya.docx", "Семья_Договор_дарения.docx"),
        ("tpl_semya_soglasie_na_vyezd_nesovershennoletnego_za_granitsu.docx", "Семья_Согласие_на_выезд_несовершеннолетнего_за_границу.docx"),
        ("tpl_stroitelstvo_dogovor_podryada_na_remont.docx", "Строительство_Договор_подряда_на_ремонт.docx"),
        ("tpl_stroitelstvo_smeta_na_remont.docx", "Строительство_Смета_на_ремонт.docx"),
        ("tpl_student_zayavlenie_na_materialnuyu_pomosch.docx", "Студент_Заявление_на_материальную_помощь.docx"),
        ("tpl_student_zayavlenie_na_mesto_v_obschezhitii.docx", "Студент_Заявление_на_место_в_общежитии.docx"),
        ("tpl_student_zayavlenie_o_vosstanovlenii.docx", "Студент_Заявление_о_восстановлении.docx"),
        ("tpl_student_zayavlenie_o_vydache_spravki_ob_obuchenii.docx", "Студент_Заявление_о_выдаче_справки_об_обучении.docx"),
        ("tpl_student_zayavlenie_o_perevode_na_druguyu_spetsialnost.docx", "Студент_Заявление_о_переводе_на_другую_специальность.docx"),
        ("tpl_student_zayavlenie_o_peresdache_ekzamena.docx", "Студент_Заявление_о_пересдаче_экзамена.docx"),
        ("tpl_student_zayavlenie_o_predostavlenii_akademicheskogo_otpuska.docx", "Студент_Заявление_о_предоставлении_академического_отпуска.docx"),
        ("tpl_student_zayavlenie_ob_otchislenii_po_sobstvennomu_zhelaniyu.docx", "Студент_Заявление_об_отчислении_по_собственному_желанию.docx"),
        ("tpl_student_obyasnitelnaya_zapiska_o_propuske_zanyatiy.docx", "Студент_Объяснительная_записка_о_пропуске_занятий.docx"),
        ("tpl_student_titulnyy_list_kursovoy_raboty.docx", "Студент_Титульный_лист_курсовой_работы.docx"),
        ("tpl_torgovlya_akt_sverki_vzaimoraschyotov.docx", "Торговля_Акт_сверки_взаиморасчётов.docx"),
        ("tpl_torgovlya_dogovor_postavki.docx", "Торговля_Договор_поставки.docx"),
        ("tpl_torgovlya_schyot_na_oplatu.docx", "Торговля_Счёт_на_оплату.docx"),
        ("tpl_torgovlya_tovarnaya_nakladnaya.docx", "Торговля_Товарная_накладная.docx")
    };

    public const int TemplatesSeedVersion = 1;
    private const string SeedVersionKey = "templates_seed_version";

    /// <summary>
    /// Проверяет наличие шаблонов в рабочей папке и при необходимости копирует или генерирует отсутствующие эталонные файлы.
    /// </summary>
    public static async Task EnsureDefaultTemplatesAsync()
    {
        try {
            string folder = SettingsService.TemplatesFolder;
            int currentVersion = Preferences.Default.Get(SeedVersionKey, 0);

            // Если версия шаблонов актуальна и в папке уже есть шаблоны — пропускаем повторную проверку 60+ файлов
            if (currentVersion >= TemplatesSeedVersion && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.docx").Any())
            {
                return;
            }

            Directory.CreateDirectory(folder);

            bool anyAdded = false;

            foreach (var (rawAsset, targetFileName) in DefaultTemplates) {
                string targetPath = Path.Combine(folder, targetFileName);
                if (File.Exists(targetPath))
                    continue;

                bool copied = false;
                try {
                    // 1. Попытка скопировать из ресурсов приложения (Resources/Raw)
                    using var stream = await FileSystem.OpenAppPackageFileAsync(rawAsset);
                    using var fs = File.Create(targetPath);
                    await stream.CopyToAsync(fs);
                    copied = true;
                } catch {
                    copied = false;
                }

                // 2. Если файл отсутствует в ресурсах пакета, генерируем эталонный документ OpenXML
                if (!copied)
                    CreateTemplateDocument(targetPath, targetFileName);

                anyAdded = true;
            }

            Preferences.Default.Set(SeedVersionKey, TemplatesSeedVersion);

            if (anyAdded)
                TemplateCatalog.Refresh();
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[TemplateSeeder] Ошибка сидирования шаблонов: {ex.Message}");
        }
    }

    /// <summary>
    /// Генерирует структуру .docx документа с токенами ГОСТ.
    /// </summary>
    public static void CreateTemplateDocument(string outputPath, string templateName)
    {
        string dir = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        if (templateName.Contains("отпуск", StringComparison.OrdinalIgnoreCase) && !templateName.Contains("академическ", StringComparison.OrdinalIgnoreCase) ||
            templateName.Contains("vacation", StringComparison.OrdinalIgnoreCase))
            BuildVacationTemplate(body);
        else if (templateName.Contains("Договор", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("contract", StringComparison.OrdinalIgnoreCase))
            BuildSalesContractTemplate(body);
        else if (templateName.Contains("Справка", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("study_certificate", StringComparison.OrdinalIgnoreCase))
            BuildStudyCertificateTemplate(body);
        else if (templateName.Contains("материальн", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("financial_aid", StringComparison.OrdinalIgnoreCase))
            BuildFinancialAidTemplate(body);
        else if (templateName.Contains("общежити", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("dormitory", StringComparison.OrdinalIgnoreCase))
            BuildDormitoryTemplate(body);
        else if (templateName.Contains("академическ", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("academic_leave", StringComparison.OrdinalIgnoreCase))
            BuildAcademicLeaveTemplate(body);
        else if (templateName.Contains("деканат", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("student_explanation", StringComparison.OrdinalIgnoreCase))
            BuildStudentExplanationTemplate(body);
        else if (templateName.Contains("график", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("individual_schedule", StringComparison.OrdinalIgnoreCase))
            BuildIndividualScheduleTemplate(body);
        else
            BuildExplanationTemplate(body);

        mainPart.Document.Save();
    }

    private static void BuildVacationTemplate(Body body)
    {
        AddParagraph(body, "Руководителю [Организация]", JustificationValues.Right);
        AddParagraph(body, "[Должность]", JustificationValues.Right);
        AddParagraph(body, "[ФИО]", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу предоставить мне трудовой отпуск с [Дата начала-00.00.0000] продолжительностью [Количество дней-14] календарных дней.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Дата-00.00.0000]                                                              [ФИО]", JustificationValues.Left);
    }

    private static void BuildSalesContractTemplate(Body body)
    {
        AddParagraph(body, "ДОГОВОР КУПЛИ-ПРОДАЖИ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "г. [Город-Минск]                                                  [Дата-00.00.0000]", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Организация], именуемое в дальнейшем «Продавец», в лице представителя [ФИО], с одной стороны, и гражданин [Покупатель ФИО], с другой стороны, заключили настоящий договор о нижеследующем:", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "1. Предмет договора: Продавец обязуется передать в собственность, а Покупатель принять и оплатить [Наименование товара].", JustificationValues.Left);
        AddParagraph(body, "2. Сумма договора составляет [Сумма-0.00] белорусских рублей.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "РЕКВИЗИТЫ СТОРОН:", JustificationValues.Left, isBold: true);
        AddParagraph(body, "Продавец: [Организация], УНП: [УНП], Юр. адрес: [Юридический адрес], Р/с: [Расчётный счёт], Тел: [Телефон].", JustificationValues.Left);
        AddParagraph(body, "Покупатель: [Покупатель ФИО], Паспорт: [Паспорт].", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Подписи сторон: ______________ / [ФИО]                 ______________ / [Покупатель ФИО]", JustificationValues.Left);
    }

    private static void BuildExplanationTemplate(Body body)
    {
        AddParagraph(body, "Руководителю [Организация]", JustificationValues.Right);
        AddParagraph(body, "от [Должность]", JustificationValues.Right);
        AddParagraph(body, "[ФИО]", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Касательно [Причина или событие] сообщаю следующее: [Текст объяснения обстоятельств].", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Дата-00.00.0000]                                                              [ФИО]", JustificationValues.Left);
    }

    // 1. Справка с места учебы
    private static void BuildStudyCertificateTemplate(Body body)
    {
        AddParagraph(body, "МИНИСТЕРСТВО ОБРАЗОВАНИЯ РЕСПУБЛИКИ БЕЛАРУСЬ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "{{University}}", JustificationValues.Center, isBold: true);
        AddParagraph(body, "г. Минск", JustificationValues.Center);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "СПРАВКА", JustificationValues.Center, isBold: true);
        AddParagraph(body, "{{Date}}  № 01-14/{{Group}}", JustificationValues.Center);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Дана {{FullName}} в том, что он(а) действительно является студентом дневной формы получения образования факультета {{Faculty}} по специальности {{Specialty}}.", JustificationValues.Left);
        AddParagraph(body, "В настоящее время обучается на {{Course}} курсе в учебной группе {{Group}} (форма обучения: {{StudyForm}}).", JustificationValues.Left);
        AddParagraph(body, "Справка выдана для предоставления: {{IssueReason}}.", JustificationValues.Left);
        AddParagraph(body, "Срок действия настоящей справки — до окончания текущего учебного года.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Декан факультета                                     ______________ / [Декан ФИО]", JustificationValues.Left);
        AddParagraph(body, "Секретарь деканата                                   ______________ / [Секретарь ФИО]", JustificationValues.Left);
        AddParagraph(body, "М.П.", JustificationValues.Left);
    }

    // 2. Заявление на материальную помощь
    private static void BuildFinancialAidTemplate(Body body)
    {
        AddParagraph(body, "Декану / Ректору", JustificationValues.Right);
        AddParagraph(body, "{{DeanName}}", JustificationValues.Right);
        AddParagraph(body, "студента факультета {{Faculty}}", JustificationValues.Right);
        AddParagraph(body, "учебной группы {{Group}}", JustificationValues.Right);
        AddParagraph(body, "{{FullName}}", JustificationValues.Right);
        AddParagraph(body, "тел. {{Phone}}", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу оказать мне единовременную материальную помощь в связи со сложившимися материальными обстоятельствами: {{Reason}}.", JustificationValues.Left);
        AddParagraph(body, "К заявлению прилагаю документы, подтверждающие основания для выплаты: [Подтверждающие документы].", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "{{Date}}                                                             ______________ / {{FullName}}", JustificationValues.Left);
    }

    // 3. Заявление на предоставление / продление места в общежитии
    private static void BuildDormitoryTemplate(Body body)
    {
        AddParagraph(body, "Председателю жилищной комиссии", JustificationValues.Right);
        AddParagraph(body, "{{CommissionHead}}", JustificationValues.Right);
        AddParagraph(body, "студента факультета {{Faculty}}", JustificationValues.Right);
        AddParagraph(body, "{{Course}} курса, группы {{Group}}", JustificationValues.Right);
        AddParagraph(body, "{{FullName}}", JustificationValues.Right);
        AddParagraph(body, "проживающего по адресу: {{HomeAddress}}", JustificationValues.Right);
        AddParagraph(body, "тел. {{Phone}}", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу предоставить (продлить) мне койко-место в общежитии на {{AcademicYear}} учебный год.", JustificationValues.Left);
        AddParagraph(body, "С Правилами внутреннего распорядка в общежитии ознакомлен(а) и обязуюсь их строго соблюдать. Своевременную оплату проживания гарантирую.", JustificationValues.Left);
        AddParagraph(body, "Льготная категория (при наличии): {{PrivilegeCategory}}.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "{{Date}}                                                             ______________ / {{FullName}}", JustificationValues.Left);
    }

    // 4. Заявление на академический отпуск
    private static void BuildAcademicLeaveTemplate(Body body)
    {
        AddParagraph(body, "Ректору университета", JustificationValues.Right);
        AddParagraph(body, "{{RectorName}}", JustificationValues.Right);
        AddParagraph(body, "студента факультета {{Faculty}}", JustificationValues.Right);
        AddParagraph(body, "{{Course}} курса, группы {{Group}}", JustificationValues.Right);
        AddParagraph(body, "специальности {{Specialty}}", JustificationValues.Right);
        AddParagraph(body, "{{FullName}}", JustificationValues.Right);
        AddParagraph(body, "тел. {{Phone}}", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу предоставить мне академический отпуск с {{StartDate}} по {{EndDate}} по следующей уважительной причине: {{Reason}}.", JustificationValues.Left);
        AddParagraph(body, "Основание предоставления отпуска: {{AttachedDocs}}.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "{{Date}}                                                             ______________ / {{FullName}}", JustificationValues.Left);
    }

    // 5. Объяснительная записка в деканат
    private static void BuildStudentExplanationTemplate(Body body)
    {
        AddParagraph(body, "Декану факультета {{Faculty}}", JustificationValues.Right);
        AddParagraph(body, "{{DeanName}}", JustificationValues.Right);
        AddParagraph(body, "студента группы {{Group}}", JustificationValues.Right);
        AddParagraph(body, "специальности {{Specialty}}", JustificationValues.Right);
        AddParagraph(body, "{{FullName}}", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Я, {{FullName}}, отсутствовал(а) на занятиях ({{MissedClasses}}) в период {{MissedDates}} по следующей причине: {{Reason}}.", JustificationValues.Left);
        AddParagraph(body, "Оправдательные документы: {{AttachedDocs}}.", JustificationValues.Left);
        AddParagraph(body, "Обязуюсь отработать все пропущенные учебные занятия в полном объеме.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "{{Date}}                                                             ______________ / {{FullName}}", JustificationValues.Left);
    }

    // 6. Индивидуальный график сдачи зачетов и экзаменов
    private static void BuildIndividualScheduleTemplate(Body body)
    {
        AddParagraph(body, "Декану факультета {{Faculty}}", JustificationValues.Right);
        AddParagraph(body, "{{DeanName}}", JustificationValues.Right);
        AddParagraph(body, "студента {{Course}} курса, группы {{Group}}", JustificationValues.Right);
        AddParagraph(body, "специальности {{Specialty}}", JustificationValues.Right);
        AddParagraph(body, "{{FullName}}", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "о предоставлении индивидуального графика сдачи зачетов и экзаменов", JustificationValues.Center);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу разрешить мне индивидуальный график сдачи зачетов и экзаменов за {{Semester}} по уважительной причине: {{Reason}}.", JustificationValues.Left);
        AddParagraph(body, "Предлагаемый график и сроки сдачи дисциплин: {{ScheduleDetails}}.", JustificationValues.Left);
        AddParagraph(body, "Обязуюсь полностью ликвидировать академическую задолженность до {{DeadlineDate}}.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Согласовано:", JustificationValues.Left);
        AddParagraph(body, "Заведующий кафедрой                                  ______________ / [Зав. кафедрой ФИО]", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "{{Date}}                                                             ______________ / {{FullName}}", JustificationValues.Left);
    }

    private static void AddParagraph(Body body, string text, JustificationValues align, bool isBold = false)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new SpacingBetweenLines {
            Before = "0",
            After = "0",
            Line = "240",
            LineRule = LineSpacingRuleValues.Auto,
        });
        pPr.AppendChild(new Justification { Val = align });

        var run = p.AppendChild(new Run());
        var rPr = run.AppendChild(new RunProperties());
        rPr.AppendChild(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman",
            EastAsia = "Times New Roman", ComplexScript = "Times New Roman" });
        if (isBold) {
            rPr.AppendChild(new Bold());
        }
        rPr.AppendChild(new FontSize { Val = "28" }); // 14 pt
        rPr.AppendChild(new FontSizeComplexScript { Val = "28" });
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
    }
}
