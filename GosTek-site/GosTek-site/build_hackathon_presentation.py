"""
Скрипт создания идеальной презентации GosTek в терминальном стиле «Графит» / Hackathon Dev Console
На базе шаблона: GosTek презентация V4 графит.pptx
"""

import os
import pptx
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
from pptx.enum.shapes import MSO_SHAPE

def build_hackathon_presentation():
    base_file = r"D:\Progect_Gostek\GosTek-Presentation-Graphite-Base.pptx"
    prs = pptx.Presentation(base_file)

    # Цветовая палитра терминала «Графит»
    COLOR_BG       = RGBColor(32, 41, 47)     # #20292F
    COLOR_CARD     = RGBColor(41, 53, 60)     # #29353C
    COLOR_MINT     = RGBColor(77, 232, 160)   # #4DE8A0 (фирменный зеленый терминала)
    COLOR_WHITE    = RGBColor(244, 249, 252)  # #F4F9FC (яркий белый)
    COLOR_TEXT     = RGBColor(221, 233, 239)  # #DDE9EF (светлый текст)
    COLOR_MUTED    = RGBColor(157, 181, 193)  # #9DB5C1 (приглушенный статус)
    COLOR_CYAN     = RGBColor(56, 189, 248)

    FONT_MONO = "JetBrains Mono"

    TOTAL_SLIDES = 17

    def set_slide_background(slide):
        bg = slide.background
        fill = bg.fill
        fill.solid()
        fill.fore_color.rgb = COLOR_BG

    def add_terminal_header(slide, prompt_text, comment_text, title_text):
        # 1. Промпт командной строки
        box0 = slide.shapes.add_textbox(Inches(0.67), Inches(0.36), Inches(11.5), Inches(0.35))
        tf0 = box0.text_frame
        tf0.word_wrap = True
        tf0.margin_left = tf0.margin_top = tf0.margin_right = tf0.margin_bottom = 0
        p0 = tf0.paragraphs[0]
        p0.text = prompt_text
        p0.font.name = FONT_MONO
        p0.font.size = Pt(13)
        p0.font.color.rgb = COLOR_MINT

        # 2. Комментарий
        box1 = slide.shapes.add_textbox(Inches(0.67), Inches(0.82), Inches(11.5), Inches(0.38))
        tf1 = box1.text_frame
        tf1.word_wrap = True
        tf1.margin_left = tf1.margin_top = tf1.margin_right = tf1.margin_bottom = 0
        p1 = tf1.paragraphs[0]
        p1.text = comment_text
        p1.font.name = FONT_MONO
        p1.font.size = Pt(15)
        p1.font.color.rgb = COLOR_MINT

        # 3. Главный тезис
        box2 = slide.shapes.add_textbox(Inches(0.67), Inches(1.25), Inches(11.5), Inches(0.65))
        tf2 = box2.text_frame
        tf2.word_wrap = True
        tf2.margin_left = tf2.margin_top = tf2.margin_right = tf2.margin_bottom = 0
        p2 = tf2.paragraphs[0]
        p2.text = title_text
        p2.font.name = FONT_MONO
        p2.font.size = Pt(28)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_WHITE

    def add_status_bar(slide, slide_num):
        # Разделительная линия
        line = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(0.67), Inches(6.83), Inches(12.0), Inches(0.015))
        line.fill.solid()
        line.fill.fore_color.rgb = COLOR_MINT
        line.line.color.rgb = COLOR_MINT

        # Текст слева: status: offline-ready
        sbox = slide.shapes.add_textbox(Inches(0.67), Inches(7.00), Inches(6.0), Inches(0.3))
        stf = sbox.text_frame
        stf.margin_left = stf.margin_top = stf.margin_right = stf.margin_bottom = 0
        sp = stf.paragraphs[0]
        sp.text = "status: offline-ready"
        sp.font.name = FONT_MONO
        sp.font.size = Pt(12)
        sp.font.color.rgb = COLOR_MUTED

        # Текст справа: GosTek · XX / YY
        cbox = slide.shapes.add_textbox(Inches(6.67), Inches(7.00), Inches(6.0), Inches(0.3))
        ctf = cbox.text_frame
        ctf.margin_left = ctf.margin_top = ctf.margin_right = ctf.margin_bottom = 0
        cp = ctf.paragraphs[0]
        cp.alignment = PP_ALIGN.RIGHT
        cp.text = f"GosTek · {slide_num:02d} / {TOTAL_SLIDES:02d}"
        cp.font.name = FONT_MONO
        cp.font.size = Pt(12)
        cp.font.color.rgb = COLOR_MUTED

    def add_terminal_card(slide, left, top, width, height, tag, title, desc, tag_color=COLOR_MINT):
        # Фоновая подложка
        card = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, left, top, width, height)
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD
        card.line.color.rgb = COLOR_MINT
        card.line.width = Pt(1)

        # Контент
        tbox = slide.shapes.add_textbox(left + Inches(0.2), top + Inches(0.15), width - Inches(0.4), height - Inches(0.3))
        tf = tbox.text_frame
        tf.word_wrap = True
        tf.margin_left = tf.margin_top = tf.margin_right = tf.margin_bottom = 0

        p1 = tf.paragraphs[0]
        p1.text = f"[{tag}]  {title}"
        p1.font.name = FONT_MONO
        p1.font.size = Pt(15)
        p1.font.bold = True
        p1.font.color.rgb = tag_color
        p1.space_after = Pt(8)

        p2 = tf.add_paragraph()
        p2.text = desc
        p2.font.name = FONT_MONO
        p2.font.size = Pt(12)
        p2.font.color.rgb = COLOR_TEXT

    def add_terminal_bullets(slide, start_top, items, left=0.67, width=11.5):
        # items: list of (tag, text)
        cur_top = start_top
        for tag, text in items:
            tbox = slide.shapes.add_textbox(Inches(left), Inches(cur_top), Inches(width), Inches(0.85))
            tf = tbox.text_frame
            tf.word_wrap = True
            tf.margin_left = tf.margin_top = tf.margin_right = tf.margin_bottom = 0

            p = tf.paragraphs[0]
            r1 = p.add_run()
            r1.text = f"{tag} "
            r1.font.name = FONT_MONO
            r1.font.size = Pt(17)
            r1.font.bold = True
            r1.font.color.rgb = COLOR_MINT

            r2 = p.add_run()
            r2.text = text
            r2.font.name = FONT_MONO
            r2.font.size = Pt(16)
            r2.font.color.rgb = COLOR_TEXT

            cur_top += 1.05

    def add_screenshot_frame(slide, left, top, width, height, title, label, filename):
        # Окно с кибер-углами
        frame = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, left, top, width, height)
        frame.fill.solid()
        frame.fill.fore_color.rgb = COLOR_CARD
        frame.line.color.rgb = COLOR_MINT
        frame.line.width = Pt(1.5)

        # Заголовок окна
        tbox = slide.shapes.add_textbox(left + Inches(0.2), top + Inches(0.2), width - Inches(0.4), height - Inches(0.4))
        tf = tbox.text_frame
        tf.word_wrap = True
        p = tf.paragraphs[0]
        p.text = f"// {title}"
        p.font.name = FONT_MONO
        p.font.size = Pt(14)
        p.font.bold = True
        p.font.color.rgb = COLOR_MINT

        p2 = tf.add_paragraph()
        p2.text = f"\n{label}\n\n[скриншот: {filename}]"
        p2.alignment = PP_ALIGN.CENTER
        p2.font.name = FONT_MONO
        p2.font.size = Pt(12)
        p2.font.color.rgb = COLOR_MUTED

    # Создаём совершенно новую презентацию, копируя тему и нужные слайды
    # Чтобы соблюсти точную структуру пользователя:
    # 1. Информация про нас
    # 2. Задачи проекта
    # 3. Цель проекта
    # 4. Актуальность и Инновационность
    # 5. Перечисление всех модулей проекта
    # 6. Описание каждого модуля на отдельном слайде (7 модулей)
    # 7. Планы на будущее / Будущие возможности
    # 8. Заключение

    new_prs = pptx.Presentation()
    new_prs.slide_width = Inches(13.333)
    new_prs.slide_height = Inches(7.5)
    blank_layout = new_prs.slide_layouts[6]

    # Извлекаем картинки из оригинальной презентации, чтобы вставить их в новые слайды!
    pic_team = []     # 3 аватара со Слайда 2
    pic_scan = None   # Скан со Слайда 7
    pic_profile = None# Профиль со Слайда 8
    pic_template = None # Шаблоны со Слайда 9
    pic_qr = None     # QR со Слайда 14

    # Извлекаем картинки
    for shape in prs.slides[1].shapes:
        if shape.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.PICTURE:
            pic_team.append(shape.image.blob)

    for shape in prs.slides[6].shapes:
        if shape.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.PICTURE:
            pic_scan = shape.image.blob

    for shape in prs.slides[7].shapes:
        if shape.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.PICTURE:
            pic_profile = shape.image.blob

    for shape in prs.slides[8].shapes:
        if shape.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.PICTURE:
            pic_template = shape.image.blob

    for shape in prs.slides[13].shapes:
        if shape.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.PICTURE:
            pic_qr = shape.image.blob

    print(f"Извлечено: team={len(pic_team)}, scan={bool(pic_scan)}, profile={bool(pic_profile)}, template={bool(pic_template)}, qr={bool(pic_qr)}")

    # Сохраним временные картинки на диск D:
    tmp_dir = r"D:\GosTek-site\assets"
    def save_blob(blob, fname):
        p = os.path.join(tmp_dir, fname)
        with open(p, "wb") as f:
            f.write(blob)
        return p

    img_team_paths = [save_blob(b, f"team_{i}.png") for i, b in enumerate(pic_team)] if pic_team else []
    img_scan_path = save_blob(pic_scan, "slide_scan.png") if pic_scan else None
    img_profile_path = save_blob(pic_profile, "slide_profile.png") if pic_profile else None
    img_template_path = save_blob(pic_template, "slide_template.png") if pic_template else None
    img_qr_path = save_blob(pic_qr, "slide_qr.png") if pic_qr else None

    # =========================================================================
    # СЛАЙД 1: ТИТУЛЬНЫЙ
    # =========================================================================
    s1 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s1)

    # Prompt
    tbox = s1.shapes.add_textbox(Inches(0.67), Inches(0.8), Inches(11.5), Inches(0.4))
    tbox.text_frame.word_wrap = True
    p = tbox.text_frame.paragraphs[0]
    p.text = "~/gostek $ ./present --hackathon"
    p.font.name = FONT_MONO
    p.font.size = Pt(16)
    p.font.color.rgb = COLOR_MINT

    # BY · 2026
    rbox = s1.shapes.add_textbox(Inches(8.0), Inches(0.8), Inches(4.6), Inches(0.4))
    rp = rbox.text_frame.paragraphs[0]
    rp.alignment = PP_ALIGN.RIGHT
    rp.text = "BY · 2026"
    rp.font.name = FONT_MONO
    rp.font.size = Pt(16)
    rp.font.color.rgb = COLOR_MUTED

    # Title
    tbox2 = s1.shapes.add_textbox(Inches(0.67), Inches(1.8), Inches(11.5), Inches(1.6))
    p2 = tbox2.text_frame.paragraphs[0]
    p2.text = "GosTek      _>"
    p2.font.name = FONT_MONO
    p2.font.size = Pt(56)
    p2.font.bold = True
    p2.font.color.rgb = COLOR_WHITE

    # Subtitles
    tbox3 = s1.shapes.add_textbox(Inches(0.67), Inches(3.6), Inches(11.5), Inches(1.8))
    tf3 = tbox3.text_frame
    p3 = tf3.paragraphs[0]
    p3.text = "> Заполнение и аудит документов по белорусским образцам"
    p3.font.name = FONT_MONO
    p3.font.size = Pt(22)
    p3.font.color.rgb = COLOR_TEXT
    p3.space_after = Pt(12)

    p4 = tf3.add_paragraph()
    p4.text = "> Данные не покидают устройство · Локальное шифрование"
    p4.font.name = FONT_MONO
    p4.font.size = Pt(18)
    p4.font.color.rgb = COLOR_MUTED
    p4.space_after = Pt(24)

    # Badges
    p5 = tf3.add_paragraph()
    p5.text = "OCR    OFFLINE    WINDOWS    ANDROID"
    p5.font.name = FONT_MONO
    p5.font.size = Pt(14)
    p5.font.bold = True
    p5.font.color.rgb = COLOR_MINT

    # Nomination
    nbox = s1.shapes.add_textbox(Inches(0.67), Inches(5.8), Inches(11.5), Inches(0.5))
    np = nbox.text_frame.paragraphs[0]
    np.text = 'nomination = "Социальные проекты и образование"'
    np.font.name = FONT_MONO
    np.font.size = Pt(13)
    np.font.color.rgb = COLOR_MINT

    add_status_bar(s1, 1)

    # =========================================================================
    # СЛАЙД 2: 1. ИНФОРМАЦИЯ ПРО НАС (КОМАНДА)
    # =========================================================================
    s2 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s2)
    add_terminal_header(s2, "~/gostek/authors $ cat team.txt", "// Авторы проекта", "Команда разработки KakhatonEnergy")

    members = [
        ("Перов Владислав Артемович", "Распознавание документов (OCR)", "Разработка OCR-модуля, извлечение реквизитов из машиночитаемых строк паспортов и ID-карт (MRZ)."),
        ("Мурашко Роман Янович", "Генерация с помощью ИИ", "Интеграция ИИ-помощника, профиль, безопасный шлюз Zero-Text Logging и экспорт документов."),
        ("Ходонович Даниил Русланович", "Интерфейс и навигация", "Проектирование пользовательского опыта, адаптивная верстка, шаблоны и валидация реквизитов.")
    ]

    col_w = Inches(3.7)
    col_gap = Inches(0.4)
    for i, (mname, mrole, mdesc) in enumerate(members):
        c_left = Inches(0.67) + i * (col_w + col_gap)
        card = s2.shapes.add_shape(MSO_SHAPE.RECTANGLE, c_left, Inches(1.9), col_w, Inches(3.8))
        card.fill.solid()
        card.fill.fore_color.rgb = COLOR_CARD
        card.line.color.rgb = COLOR_MINT
        card.line.width = Pt(1)

        # Вставляем аватар
        if i < len(img_team_paths) and os.path.exists(img_team_paths[i]):
            s2.shapes.add_picture(img_team_paths[i], c_left + Inches(1.25), Inches(2.1), Inches(1.2), Inches(1.2))

        # Текст автора
        abox = s2.shapes.add_textbox(c_left + Inches(0.2), Inches(3.45), col_w - Inches(0.4), Inches(2.1))
        atf = abox.text_frame
        atf.word_wrap = True
        atf.margin_left = atf.margin_top = atf.margin_right = atf.margin_bottom = 0

        p1 = atf.paragraphs[0]
        p1.text = mname
        p1.font.name = FONT_MONO
        p1.font.size = Pt(14)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_WHITE
        p1.alignment = PP_ALIGN.CENTER
        p1.space_after = Pt(6)

        p2 = atf.add_paragraph()
        p2.text = f"// {mrole}"
        p2.font.name = FONT_MONO
        p2.font.size = Pt(11)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_MINT
        p2.alignment = PP_ALIGN.CENTER
        p2.space_after = Pt(8)

        p3 = atf.add_paragraph()
        p3.text = mdesc
        p3.font.name = FONT_MONO
        p3.font.size = Pt(10)
        p3.font.color.rgb = COLOR_MUTED
        p3.alignment = PP_ALIGN.CENTER

    # Информация о колледже
    cinfo = s2.shapes.add_textbox(Inches(0.67), Inches(5.9), Inches(12.0), Inches(0.7))
    ci_tf = cinfo.text_frame
    ci_p1 = ci_tf.paragraphs[0]
    ci_p1.text = '[01] колледж = "Бобруйский государственный лесомеханический колледж"  |  группа = "РС02-24"'
    ci_p1.font.name = FONT_MONO
    ci_p1.font.size = Pt(12)
    ci_p1.font.color.rgb = COLOR_TEXT

    ci_p2 = ci_tf.add_paragraph()
    ci_p2.text = '[02] локация = "Республика Беларусь, Могилевская область, г. Бобруйск"'
    ci_p2.font.name = FONT_MONO
    ci_p2.font.size = Pt(12)
    ci_p2.font.color.rgb = COLOR_MUTED

    add_status_bar(s2, 2)

    # =========================================================================
    # СЛАЙД 3: 2. ЗАДАЧИ ПРОЕКТА
    # =========================================================================
    s3 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s3)
    add_terminal_header(s3, "~/gostek/tasks $ cat tasks.md", "// Задачи проекта", "Ключевые инженерные задачи разработки")

    tasks_data = [
        ("[01]", "Сократить ручной ввод при заполнении бланков", "Преобразовывать встроенные и пользовательские шаблоны Word (.docx) в формы для заполнения с автоподстановкой реквизитов."),
        ("[02]", "Переносить данные из бумажных документов", "Распознавать текст с фотографии на Android и из файлов на Windows, извлекая реквизиты из машиночитаемых строк паспорта или ID-карты."),
        ("[03]", "Проверять оформление готовых документов (Инспектор)", "Находить в готовом DOCX-файле абзацы с неверным интервалом, полями или шрифтом и автоматически исправлять их по ГОСТ, сохраняя исходный файл."),
        ("[04]", "Обеспечить аппаратную безопасность персональных данных", "Реализовать хранение реквизитов в зашифрованном SecureStorage (KeyStore / DPAPI) по принципу Zero-Text Logging без отправки в сеть.")
    ]

    for idx, (num, tname, tdesc) in enumerate(tasks_data):
        c_top = Inches(1.9) + idx * Inches(1.15)
        add_terminal_card(s3, Inches(0.67), c_top, Inches(12.0), Inches(1.05), num, tname, tdesc)

    add_status_bar(s3, 3)

    # =========================================================================
    # СЛАЙД 4: 3. ЦЕЛЬ ПРОЕКТА
    # =========================================================================
    s4 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s4)
    add_terminal_header(s4, "~/gostek/goal $ cat goal.md", "// Цель проекта", "Упростить подготовку документов до 30 секунд")

    # Левая колонка: Главная цель
    add_terminal_card(s4, Inches(0.67), Inches(1.9), Inches(5.8), Inches(4.7), "ЦЕЛЬ", "Главная миссия проекта",
                      "Упростить подготовку документов и сократить время на их оформление с 15-20 минут до 30 секунд, "
                      "чтобы пользователи могли самостоятельно получить юридически выверенный документ по ГОСТу, "
                      "готовый к мгновенной печати или отправке.\n\n"
                      "Приложение объединяет в одном окне распознавание сканов, базу реквизитов, умные шаблоны и нормаконтроль.")

    # Правая колонка: 4 практических эффекта
    effects = [
        ("01", "Экономия времени", "Сократить время на заполнение бланков в 5-10 раз за счет автоподстановки."),
        ("02", "Защита от опечаток", "Снизить риск ошибок при переносе реквизитов благодаря валидации УНП и IBAN."),
        ("03", "Стандарты ГОСТ", "Строго соблюдать требования к полям, шрифтам (14 пт) и полуторному интервалу."),
        ("04", "Готовые файлы", "Мгновенно получать готовый файл DOCX для редактирования или PDF для печати.")
    ]

    for j, (enum_str, etitle, edesc) in enumerate(effects):
        top_e = Inches(1.9) + j * Inches(1.18)
        add_terminal_card(s4, Inches(6.8), top_e, Inches(5.87), Inches(1.05), enum_str, etitle, edesc)

    add_status_bar(s4, 4)

    # =========================================================================
    # СЛАЙД 5: 4. АКТУАЛЬНОСТЬ И ИННОВАЦИОННОСТЬ
    # =========================================================================
    s5 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s5)
    add_terminal_header(s5, "~/gostek/relevance $ diff novelty.md", "// Актуальность и Инновационность", "Почему это важно и чем отличается GosTek")

    # Левая сторона: Актуальность (3 боли)
    act_card = s5.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(0.67), Inches(1.9), Inches(5.8), Inches(4.7))
    act_card.fill.solid()
    act_card.fill.fore_color.rgb = COLOR_CARD
    act_card.line.color.rgb = COLOR_MINT
    act_card.line.width = Pt(1)

    atf = act_card.text_frame
    atf.word_wrap = True
    ap1 = atf.paragraphs[0]
    ap1.text = "// АКТУАЛЬНОСТЬ (ПРОБЛЕМЫ РУЧНОГО ТРУДА)"
    ap1.font.name = FONT_MONO
    ap1.font.size = Pt(13)
    ap1.font.bold = True
    ap1.font.color.rgb = COLOR_MINT
    ap1.space_after = Pt(12)

    ap_items = [
        ("[-] Постоянный повторный ввод", "ФИО, паспорт, адрес и телефон повторяются в десятках заявлений. Легко ошибиться или оставить данные из чужого старого файла."),
        ("[-] Ручной нормаконтроль", "Сверка полей (30/15/20/20 мм), кегля 14 пт и отступа 1.25 см вручную отнимает до 70% времени подготовки отчетов."),
        ("[-] 99.6% онлайн-документов", "По данным Белстат, подача документов онлайн стала нормой, но подготовка файлов всё ещё ведётся вручную.")
    ]
    for h, desc in ap_items:
        p_h = atf.add_paragraph()
        p_h.text = h
        p_h.font.name = FONT_MONO
        p_h.font.size = Pt(12)
        p_h.font.bold = True
        p_h.font.color.rgb = RGBColor(248, 113, 113) # Red/Salmon

        p_d = atf.add_paragraph()
        p_d.text = f"    {desc}"
        p_d.font.name = FONT_MONO
        p_d.font.size = Pt(10)
        p_d.font.color.rgb = COLOR_TEXT
        p_d.space_after = Pt(8)

    # Правая сторона: Инновационность (3 преимущества)
    nov_card = s5.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(6.8), Inches(1.9), Inches(5.87), Inches(4.7))
    nov_card.fill.solid()
    nov_card.fill.fore_color.rgb = COLOR_CARD
    nov_card.line.color.rgb = COLOR_MINT
    nov_card.line.width = Pt(1)

    ntf = nov_card.text_frame
    ntf.word_wrap = True
    np1 = ntf.paragraphs[0]
    np1.text = "// ИННОВАЦИОННОСТЬ GOSTEK"
    np1.font.name = FONT_MONO
    np1.font.size = Pt(13)
    np1.font.bold = True
    np1.font.color.rgb = COLOR_MINT
    np1.space_after = Pt(12)

    np_items = [
        ("[+] Белорусские образцы", "Поддержка специфики национальных реквизитов: 9-значный УНП, валидация IBAN mod 97, паспорта РБ и ID-карты."),
        ("[+] Из Word-файла в форму", "Метка вроде {{ФИО}} автоматически создаёт поле ввода. Достаточно заполнить его один раз, чтобы имя подставилось везде."),
        ("[+] От замечания к исправлению", "Инспектор не просто находит отклонения от ГОСТ, а создаёт отдельную исправленную копию файла в 1 клик.")
    ]
    for h, desc in np_items:
        p_h = ntf.add_paragraph()
        p_h.text = h
        p_h.font.name = FONT_MONO
        p_h.font.size = Pt(12)
        p_h.font.bold = True
        p_h.font.color.rgb = COLOR_MINT

        p_d = ntf.add_paragraph()
        p_d.text = f"    {desc}"
        p_d.font.name = FONT_MONO
        p_d.font.size = Pt(10)
        p_d.font.color.rgb = COLOR_TEXT
        p_d.space_after = Pt(8)

    add_status_bar(s5, 5)

    # =========================================================================
    # СЛАЙД 6: 5. ПЕРЕЧИСЛЕНИЕ ВСЕХ МОДУЛЕЙ ПРОЕКТА
    # =========================================================================
    s6 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s6)
    add_terminal_header(s6, "~/gostek/content $ ls -la modules/", "// Архитектура проекта", "Модульная структура GosTek: 7 ключевых компонентов")

    modules_list = [
        ("01", "Сканер (OCR)", "Камера и распознавание MRZ паспорта/ID-карт"),
        ("02", "Профиль", "Безопасное хранение реквизитов УНП и IBAN"),
        ("03", "Шаблоны Word", "Каталог бланков и парсер меток подстановки"),
        ("04", "ИИ-генерация", "Помощь в формулировках строго по согласию"),
        ("05", "Инспектор DOCX", "Автоматический нормаконтроль по ГОСТ Р 7.0.97"),
        ("06", "Редактор правил", "Настройка требований к полям, шрифтам и таблицам"),
        ("07", "Экспорт DOCX/PDF", "Выгрузка в Word и нативный полиграфический PDF")
    ]

    m_w = Inches(5.8)
    for m_idx, (num, m_name, m_desc) in enumerate(modules_list):
        col_x = Inches(0.67) if m_idx < 4 else Inches(6.8)
        row_y = Inches(1.9) + (m_idx if m_idx < 4 else m_idx - 4) * Inches(1.15)
        add_terminal_card(s6, col_x, row_y, m_w, Inches(1.05), num, m_name, m_desc)

    # Дополнительная плашка пайплайна
    pipe_box = s6.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(6.8), Inches(5.35), m_w, Inches(1.25))
    pipe_box.fill.solid()
    pipe_box.fill.fore_color.rgb = COLOR_CARD
    pipe_box.line.color.rgb = COLOR_MINT
    ptf = pipe_box.text_frame
    ptf.word_wrap = True
    pp1 = ptf.paragraphs[0]
    pp1.text = '// ПАЙПЛАЙН РАБОТЫ'
    pp1.font.name = FONT_MONO
    pp1.font.size = Pt(12)
    pp1.font.bold = True
    pp1.font.color.rgb = COLOR_MINT
    pp2 = ptf.add_paragraph()
    pp2.text = 'pipeline = "скан → профиль → шаблон → инспектор → DOCX / PDF"\nВсе модули (кроме ИИ) работают 100% автономно на устройстве.'
    pp2.font.name = FONT_MONO
    pp2.font.size = Pt(10)
    pp2.font.color.rgb = COLOR_TEXT

    add_status_bar(s6, 6)

    # =========================================================================
    # СЛАЙДЫ 7–13: 6. ОПИСАНИЕ КАЖДОГО МОДУЛЯ НА ОТДЕЛЬНОМ СЛАЙДЕ
    # =========================================================================

    # СЛАЙД 7: Модуль 01 · Сканер (с реальным скриншотом!)
    s7 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s7)
    add_terminal_header(s7, "~/gostek/modules/scan $ ./run", "// Представление блоков проекта · 01 сканер", "Наведи камеру на документ")

    scan_bullets = [
        ("[x]", "Живая камера в рамке, захват снимка одним нажатием"),
        ("[x]", "Фото можно взять напрямую из галереи или выбрать файл"),
        ("[x]", "Локальное распознавание OCR: Tesseract и Windows OCR"),
        ("[x]", "Автоматический парсинг 2-3 машиночитаемых строк MRZ паспорта")
    ]
    add_terminal_bullets(s7, 2.1, scan_bullets, left=0.67, width=8.2)

    # Реальный скриншот смартфона
    if img_scan_path and os.path.exists(img_scan_path):
        s7.shapes.add_picture(img_scan_path, Inches(9.38), Inches(0.97), Inches(2.70), Inches(5.22))
        # Рамка телефона вокруг
        phone_box = s7.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(9.34), Inches(0.95), Inches(2.76), Inches(5.26))
        phone_box.fill.background()
        phone_box.line.color.rgb = COLOR_MINT
        phone_box.line.width = Pt(1.5)

    add_status_bar(s7, 7)

    # СЛАЙД 8: Модуль 02 · Профиль (с реальным скриншотом!)
    s8 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s8)
    add_terminal_header(s8, "~/gostek/modules/profile $ ./run", "// Представление блоков проекта · 02 профиль", "Заполнил один раз — используешь везде")

    profile_bullets = [
        ("[x]", "Кнопка «Заполнить по фото паспорта» переносит данные из OCR"),
        ("[x]", "Реквизиты организации: УНП (9 цифр) и расчётный счёт IBAN"),
        ("[x]", "Алгоритмическая валидация контрольной суммы IBAN mod 97"),
        ("[x]", "Аппаратное шифрование SecureStorage: данные не покидают устройство")
    ]
    add_terminal_bullets(s8, 2.1, profile_bullets, left=4.0, width=8.7)

    # Реальный скриншот профиля слева
    if img_profile_path and os.path.exists(img_profile_path):
        s8.shapes.add_picture(img_profile_path, Inches(0.9), Inches(1.1), Inches(2.5), Inches(5.2))
        p_frame = s8.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(0.86), Inches(1.08), Inches(2.58), Inches(5.24))
        p_frame.fill.background()
        p_frame.line.color.rgb = COLOR_MINT
        p_frame.line.width = Pt(1.5)

    add_status_bar(s8, 8)

    # СЛАЙД 9: Модуль 03 · Шаблоны Word (с реальным десктопным скриншотом!)
    s9 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s9)
    add_terminal_header(s9, "~/gostek/modules/templates $ ./run", "// Представление блоков проекта · 03 шаблоны", "Документ собирается из профиля")

    tpl_bullets = [
        ("[x]", "Библиотека бланков колледжа: общежитие, отпуск, матпомощь, справки"),
        ("[x]", "Метки вида {{ФИО}} и [Дата-00.00.0000] превращаются в форму ввода"),
        ("[x]", "Интерактивный предпросмотр документа прямо во время заполнения"),
        ("[x]", "Импорт собственных шаблонов Word через окно настроек")
    ]
    add_terminal_bullets(s9, 2.1, tpl_bullets, left=0.67, width=7.1)

    # Реальный десктопный скриншот справа
    if img_template_path and os.path.exists(img_template_path):
        s9.shapes.add_picture(img_template_path, Inches(8.09), Inches(1.47), Inches(4.57), Inches(4.10))
        w_frame = s9.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(8.07), Inches(1.45), Inches(4.61), Inches(4.14))
        w_frame.fill.background()
        w_frame.line.color.rgb = COLOR_MINT
        w_frame.line.width = Pt(1.5)

    add_status_bar(s9, 9)

    # СЛАЙД 10: Модуль 04 · ИИ-генерация текста
    s10 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s10)
    add_terminal_header(s10, "~/gostek/modules/ai $ ./assist", "// Представление блоков проекта · 04 ИИ-генерация", "Деловой стиль за секунды")

    ai_bullets = [
        ("[x]", "Автор сообщает факты: «Опоздал на 15 минут из-за поломки автобуса»"),
        ("[x]", "ИИ формулирует юридически корректный текст для заявления или объяснительной"),
        ("[x]", "Диалоговое уточнение: сократить, дополнить аргументами или сделать тон строже"),
        ("[x]", "Строгое согласие: запрос уходит в Groq / Workers AI только по нажатию кнопки"),
        ("[x]", "Файлы документов, профиль и фотографии паспорта в облако НЕ отправляются")
    ]
    add_terminal_bullets(s10, 2.0, ai_bullets, left=0.67, width=12.0)
    add_status_bar(s10, 10)

    # СЛАЙД 11: Модуль 05 · Инспектор DOCX (Нормаконтроль)
    s11 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s11)
    add_terminal_header(s11, "~/gostek/modules/audit $ ./check", "// Представление блоков проекта · 05 инспектор DOCX", "Автоматический нормаконтроль по ГОСТ")

    audit_bullets = [
        ("[x]", "Глубокий анализ готового Word-файла на расхождения со стандартами"),
        ("[x]", "Контроль полей страницы (30/15/20/20 мм), гарнитуры, кегля и нумерации"),
        ("[x]", "Наглядное сравнение: в исходном файле 1,0 → в исправленной копии 1,5"),
        ("[x]", "Автоматическое исправление замечаний в 1 клик с сохранением оригинала")
    ]
    add_terminal_bullets(s11, 2.1, audit_bullets, left=0.67, width=7.2)

    # Плашка сравнения справа
    comp_box = s11.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(8.2), Inches(2.1), Inches(4.45), Inches(4.3))
    comp_box.fill.solid()
    comp_box.fill.fore_color.rgb = COLOR_CARD
    comp_box.line.color.rgb = COLOR_MINT
    ctf = comp_box.text_frame
    ctf.word_wrap = True
    cp1 = ctf.paragraphs[0]
    cp1.text = "// РЕЗУЛЬТАТ АУДИТА ГОСТ"
    cp1.font.name = FONT_MONO
    cp1.font.size = Pt(13)
    cp1.font.bold = True
    cp1.font.color.rgb = COLOR_MINT
    cp1.space_after = Pt(14)

    cp2 = ctf.add_paragraph()
    cp2.text = "В исходном файле:\nМежстрочный интервал: 1,0 (сбой)\nКрасная строка: 0 см (нет отступа)\nПоле слева: 20 мм (не по стандарту)"
    cp2.font.name = FONT_MONO
    cp2.font.size = Pt(11)
    cp2.font.color.rgb = RGBColor(248, 113, 113)
    cp2.space_after = Pt(14)

    cp3 = ctf.add_paragraph()
    cp3.text = "В исправленной копии:\nМежстрочный интервал: 1,5 ✓\nКрасная строка: 1.25 см ✓\nПоле слева: 30 мм (для подшивки) ✓"
    cp3.font.name = FONT_MONO
    cp3.font.size = Pt(11)
    cp3.font.color.rgb = COLOR_MINT

    add_status_bar(s11, 11)

    # СЛАЙД 12: Модуль 06 · Редактор правил оформления
    s12 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s12)
    add_terminal_header(s12, "~/gostek/modules/rules $ ./config", "// Представление блоков проекта · 06 редактор правил", "Настройка под требования организации")

    rules_cards = [
        ("01", "Параметры страницы", "Поля листа (левое 30 мм, правое 15 мм, верх/низ 20 мм), книжная или альбомная ориентация, формат колонтитулов и нумерации."),
        ("02", "Основной текст", "Указание обязательного шрифта (Times New Roman 14 пт), выравнивание по ширине страницы, запрет двойных пробелов и висячих строк."),
        ("03", "Таблицы и иллюстрации", "Отдельные правила для подписей к рисункам («Рис. 1 — ...») и шапок таблиц, позволяющие настроить полный комплект правил для курсовых.")
    ]
    for r_idx, (r_num, r_title, r_desc) in enumerate(rules_cards):
        r_top = Inches(2.1) + r_idx * Inches(1.45)
        add_terminal_card(s12, Inches(0.67), r_top, Inches(12.0), Inches(1.25), r_num, r_title, r_desc)

    add_status_bar(s12, 12)

    # СЛАЙД 13: Модуль 07 · Экспорт в DOCX и PDF
    s13 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s13)
    add_terminal_header(s13, "~/gostek/modules/export $ ./build", "// Представление блоков проекта · 07 экспорт", "Файлы для редактирования и печати")

    add_terminal_card(s13, Inches(0.67), Inches(2.1), Inches(5.8), Inches(4.3), "DOCX", "Формат Microsoft Word",
                      "● Для дальнейшей правки и согласования.\n"
                      "● Чистая OpenXML структура без мусорных тегов.\n"
                      "● Можно открыть в любом текстовом процессоре или передать в инспектор для проверки оформления.\n\n"
                      "file = \"Заявление_на_отпуск.docx\"")

    add_terminal_card(s13, Inches(6.8), Inches(2.1), Inches(5.87), Inches(4.3), "PDF", "Полиграфический PDF",
                      "● Для мгновенной печати или отправки по почте.\n"
                      "● Нативный генератор AndroidPdfWriter собирает страницу A4 прямо на устройстве.\n"
                      "● Разметка и шрифты гарантированно не съедут на принтере получателя.\n\n"
                      "file = \"Заявление_на_отпуск.pdf\"")

    add_status_bar(s13, 13)

    # =========================================================================
    # СЛАЙД 14: Модуль 08 · Приватность и Zero-Logging (бывший Слайд 10)
    # =========================================================================
    s14 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s14)
    add_terminal_header(s14, "~/gostek/blocks/privacy $ netstat", "// Представление блоков проекта · 08 приватность", "Данные остаются на устройстве")

    priv_cards = [
        ("01", "Локально", "Сканы, профиль и документы никогда не покидают устройство пользователя."),
        ("02", "Без регистрации", "Не нужен аккаунт, номер телефона или привязка электронной почты."),
        ("03", "Полный офлайн", "Сканер, база шаблонов и экспорт работают на 100% без доступа к интернету.")
    ]
    for p_idx, (p_num, p_title, p_desc) in enumerate(priv_cards):
        p_top = Inches(2.1) + p_idx * Inches(1.15)
        add_terminal_card(s14, Inches(0.67), p_top, Inches(6.5), Inches(1.0), p_num, p_title, p_desc)

    # Блок про ИИ-помощника справа
    ai_safe_box = s14.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(7.5), Inches(2.1), Inches(5.17), Inches(3.3))
    ai_safe_box.fill.solid()
    ai_safe_box.fill.fore_color.rgb = COLOR_CARD
    ai_safe_box.line.color.rgb = COLOR_MINT
    atf2 = ai_safe_box.text_frame
    atf2.word_wrap = True
    ap1 = atf2.paragraphs[0]
    ap1.text = "// РАБОТА С ИИ-ПОМОЩНИКОМ"
    ap1.font.name = FONT_MONO
    ap1.font.size = Pt(13)
    ap1.font.bold = True
    ap1.font.color.rgb = COLOR_MINT
    ap1.space_after = Pt(12)

    ap2 = atf2.add_paragraph()
    ap2.text = "● Работает только по прямому запросу автора.\n" \
               "● Передаются лишь краткие факты обращения.\n" \
               "● Паспорт, фото, профиль и файлы НЕ отправляются.\n" \
               "● Пользователь сам утверждает сгенерированный текст."
    ap2.font.name = FONT_MONO
    ap2.font.size = Pt(11)
    ap2.font.color.rgb = COLOR_TEXT

    add_status_bar(s14, 14)

    # =========================================================================
    # СЛАЙД 15: 7. ПЛАНЫ НА БУДУЩЕЕ / БУДУЩИЕ ВОЗМОЖНОСТИ
    # =========================================================================
    s15 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s15)
    add_terminal_header(s15, "~/gostek/roadmap $ cat future_plans.md", "// Перспективы развития проекта", "Планы на будущее и масштабирование GosTek")

    futures = [
        ("01", "Умные правила из методички вуза", "Загрузка методических указаний колледжа в PDF — приложение само извлекает параметры оформления и создаёт профиль для инспектора."),
        ("02", "Пакетная проверка для преподавателей", "Применение одного набора правил ко всей папке работ учебной группы (30+ файлов) с выводом единой таблицы нарушений ГОСТ."),
        ("03", "Сканирование бумажных бланков", "Съемка любого незнакомого бумажного документа камерой — GosTek распознает структуру и превращает его в интерактивный шаблон."),
        ("04", "Голосовой ввод и генерация через ИИ", "Изложение фактов голосом на естественном языке с автоматическим формированием всего документа по ГОСТ."),
        ("05", "Зашифрованная синхронизация профиля", "Безопасная сквозная передача карточки реквизитов между смартфоном и компьютером (E2EE).")
    ]

    for f_idx, (f_num, f_title, f_desc) in enumerate(futures):
        f_top = Inches(1.9) + f_idx * Inches(0.95)
        add_terminal_card(s15, Inches(0.67), f_top, Inches(12.0), Inches(0.85), f_num, f_title, f_desc)

    add_status_bar(s15, 15)

    # =========================================================================
    # СЛАЙД 16: 8. ЗАКЛЮЧЕНИЕ И МОНЕТИЗАЦИЯ
    # =========================================================================
    s16 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s16)
    add_terminal_header(s16, "~/gostek/conclusion $ cat summary.md", "// Заключение и монетизация", "Практическая ценность и внедрение GosTek")

    add_terminal_card(s16, Inches(0.67), Inches(1.9), Inches(5.8), Inches(4.7), "SUMMARY", "Итоги реализации проекта",
                      "● GosTek заполняет документы быстрее и не отправляет паспорт на чужой сервер.\n\n"
                      "● Подходит гражданам, учащимся, ИП и организациям, регулярно оформляющим типовые бланки.\n\n"
                      "● Собраны и протестированы версии для Windows (x64) и Android (APK).\n\n"
                      "● 370 автоматических тестов xUnit подтверждают 100% стабильность кода.")

    add_terminal_card(s16, Inches(6.8), Inches(1.9), Inches(5.87), Inches(4.7), "BIZ_MODEL", "Возможности монетизации",
                      "● Базовая версия бесплатна для граждан и учащихся.\n\n"
                      "● Платная подписка на расширенную ИИ-генерацию текста.\n\n"
                      "● Скидки для студентов, преподавателей и бухгалтеров.\n\n"
                      "● Корпоративная оцифровка и регулярное сопровождение комплектов бланков для колледжей и предприятий.")

    add_status_bar(s16, 16)

    # =========================================================================
    # СЛАЙД 17: ФИНАЛЬНЫЙ СЛАЙД (EXIT 0 + QR)
    # =========================================================================
    s17 = new_prs.slides.add_slide(blank_layout)
    set_slide_background(s17)

    # Prompt
    tbox = s17.shapes.add_textbox(Inches(0.67), Inches(0.8), Inches(11.5), Inches(0.4))
    p = tbox.text_frame.paragraphs[0]
    p.text = "~/gostek $ exit 0"
    p.font.name = FONT_MONO
    p.font.size = Pt(16)
    p.font.color.rgb = COLOR_MINT

    # Title
    tbox2 = s17.shapes.add_textbox(Inches(0.67), Inches(1.8), Inches(8.5), Inches(2.2))
    tf2 = tbox2.text_frame
    p2 = tf2.paragraphs[0]
    p2.text = "Спасибо за внимание!      _>"
    p2.font.name = FONT_MONO
    p2.font.size = Pt(46)
    p2.font.bold = True
    p2.font.color.rgb = COLOR_WHITE
    p2.space_after = Pt(14)

    p3 = tf2.add_paragraph()
    p3.text = "> Вопросы? Расскажем и покажем GosTek на устройстве."
    p3.font.name = FONT_MONO
    p3.font.size = Pt(18)
    p3.font.color.rgb = COLOR_TEXT
    p3.space_after = Pt(24)

    p4 = tf2.add_paragraph()
    p4.text = "OCR    OFFLINE    WINDOWS    ANDROID"
    p4.font.name = FONT_MONO
    p4.font.size = Pt(14)
    p4.font.bold = True
    p4.font.color.rgb = COLOR_MINT

    # Вставляем ASCII QR код справа
    if img_qr_path and os.path.exists(img_qr_path):
        s17.shapes.add_picture(img_qr_path, Inches(9.3), Inches(2.2), Inches(3.4), Inches(4.0))

    # Ссылка на репозиторий
    repo_box = s17.shapes.add_textbox(Inches(0.67), Inches(5.8), Inches(8.5), Inches(0.5))
    rp = repo_box.text_frame.paragraphs[0]
    rp.text = 'repo = "https://github.com/Vladislav-Perov/GosTek"'
    rp.font.name = FONT_MONO
    rp.font.size = Pt(13)
    rp.font.color.rgb = COLOR_MINT

    add_status_bar(s17, 17)

    # Сохраняем на диск D:
    out_file = r"D:\Progect_Gostek\GosTek — презентация (терминал графит).pptx"
    new_prs.save(out_file)
    print(f"Идеальная презентация успешно создана: {out_file}")

    # Также сохраняем копию в сайт
    site_file = r"D:\GosTek-site\GosTek-Presentation-Hackathon.pptx"
    new_prs.save(site_file)
    print(f"Копия сохранена: {site_file}")

if __name__ == "__main__":
    build_hackathon_presentation()
