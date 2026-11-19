"""
Генератор профессиональной презентации GosTek (.pptx)
Дизайн-система: Office Blue / 16:9 Widescreen
"""

import os
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
from pptx.enum.shapes import MSO_SHAPE

def create_presentation():
    prs = Presentation()
    # 16:9 Widescreen: 13.333 x 7.5 inches
    prs.slide_width = Inches(13.333)
    prs.slide_height = Inches(7.5)
    blank_layout = prs.slide_layouts[6]

    # Фирменная палитра Office Blue
    COLOR_BG_DARK = RGBColor(7, 13, 24)       # #070D18
    COLOR_BG_CARD = RGBColor(16, 30, 54)      # #101E36
    COLOR_BORDER  = RGBColor(37, 99, 235)     # #2563EB
    COLOR_CYAN    = RGBColor(56, 189, 248)    # #38BDF8
    COLOR_EMERALD = RGBColor(16, 185, 129)    # #10B981
    COLOR_TEXT    = RGBColor(248, 250, 252)   # #F8FAFC
    COLOR_MUTED   = RGBColor(148, 163, 184)   # #94A3B8
    COLOR_WHITE   = RGBColor(255, 255, 255)
    COLOR_AMBER   = RGBColor(245, 158, 11)

    FONT_FAMILY = "Segoe UI"
    FONT_CODE   = "Consolas"

    def apply_background(slide):
        bg = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, 0, 0, prs.slide_width, prs.slide_height)
        bg.fill.solid()
        bg.fill.fore_color.rgb = COLOR_BG_DARK
        bg.line.color.rgb = COLOR_BG_DARK
        return bg

    def add_header(slide, tag_text, title_text, category_text="GosTek · Проект 2026"):
        # Категория / хлебные крошки
        tag_box = slide.shapes.add_textbox(Inches(0.8), Inches(0.4), Inches(11.7), Inches(0.4))
        tf = tag_box.text_frame
        tf.word_wrap = True
        tf.margin_left = tf.margin_top = tf.margin_right = tf.margin_bottom = 0
        p = tf.paragraphs[0]
        p.text = f"{category_text.upper()}  |  {tag_text.upper()}"
        p.font.name = FONT_FAMILY
        p.font.size = Pt(11)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN

        # Основной заголовок
        title_box = slide.shapes.add_textbox(Inches(0.8), Inches(0.75), Inches(11.7), Inches(0.8))
        tf = title_box.text_frame
        tf.word_wrap = True
        tf.margin_left = tf.margin_top = tf.margin_right = tf.margin_bottom = 0
        p = tf.paragraphs[0]
        p.text = title_text
        p.font.name = FONT_FAMILY
        p.font.size = Pt(26)
        p.font.bold = True
        p.font.color.rgb = COLOR_TEXT

    def add_card(slide, left, top, width, height, bg_color=COLOR_BG_CARD, border_color=None):
        card = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left, top, width, height)
        card.fill.solid()
        card.fill.fore_color.rgb = bg_color
        if border_color:
            card.line.color.rgb = border_color
            card.line.width = Pt(1.5)
        else:
            card.line.color.rgb = RGBColor(30, 48, 80)
            card.line.width = Pt(1)
        return card

    def add_screenshot_placeholder(slide, left, top, width, height, label, filename):
        card = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, left, top, width, height)
        card.fill.solid()
        card.fill.fore_color.rgb = RGBColor(11, 22, 38)
        card.line.color.rgb = COLOR_CYAN
        card.line.width = Pt(1.5)

        txBox = slide.shapes.add_textbox(left + Inches(0.2), top + Inches(0.2), width - Inches(0.4), height - Inches(0.4))
        tf = txBox.text_frame
        tf.word_wrap = True
        p = tf.paragraphs[0]
        p.text = "🖼️ МЕСТО ДЛЯ СКРИНШОТА"
        p.alignment = PP_ALIGN.CENTER
        p.font.name = FONT_FAMILY
        p.font.size = Pt(13)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN

        p2 = tf.add_paragraph()
        p2.text = f"\n{label}\n\nФайл: {filename}"
        p2.alignment = PP_ALIGN.CENTER
        p2.font.name = FONT_FAMILY
        p2.font.size = Pt(11)
        p2.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 0: ТИТУЛЬНЫЙ
    # =========================================================================
    s0 = prs.slides.add_slide(blank_layout)
    apply_background(s0)

    # Декоративная фоновая карточка
    add_card(s0, Inches(0.8), Inches(0.8), Inches(11.733), Inches(5.9), bg_color=RGBColor(12, 22, 40), border_color=COLOR_BORDER)

    # Бейдж
    badge = s0.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, Inches(1.4), Inches(1.3), Inches(4.2), Inches(0.45))
    badge.fill.solid()
    badge.fill.fore_color.rgb = RGBColor(20, 45, 85)
    badge.line.color.rgb = COLOR_CYAN
    badge.text_frame.text = "● БЕЛОРУССКАЯ РАЗРАБОТКА · ГОСТ Р 7.0.97"
    badge.text_frame.paragraphs[0].font.size = Pt(11)
    badge.text_frame.paragraphs[0].font.bold = True
    badge.text_frame.paragraphs[0].font.color.rgb = COLOR_CYAN
    badge.text_frame.paragraphs[0].alignment = PP_ALIGN.CENTER

    # Заголовок
    tbox = s0.shapes.add_textbox(Inches(1.4), Inches(2.0), Inches(10.5), Inches(2.2))
    tf = tbox.text_frame
    tf.word_wrap = True
    p = tf.paragraphs[0]
    p.text = "GosTek"
    p.font.name = FONT_FAMILY
    p.font.size = Pt(54)
    p.font.bold = True
    p.font.color.rgb = COLOR_WHITE

    p2 = tf.add_paragraph()
    p2.text = "Кроссплатформенная подготовка, оцифровка и аудит документов"
    p2.font.name = FONT_FAMILY
    p2.font.size = Pt(24)
    p2.font.bold = True
    p2.font.color.rgb = COLOR_CYAN

    p3 = tf.add_paragraph()
    p3.text = "Автоматизация создания заявлений, договоров и отчётов на Windows и Android"
    p3.font.name = FONT_FAMILY
    p3.font.size = Pt(16)
    p3.font.color.rgb = COLOR_MUTED

    # Мета-информация внизу
    meta_box = s0.shapes.add_textbox(Inches(1.4), Inches(5.3), Inches(10.5), Inches(1.0))
    mtf = meta_box.text_frame
    mp = mtf.paragraphs[0]
    mp.text = "УО «Бобруйский государственный лесомеханический колледж»  |  Группа РС02-24  |  Бобруйск · 2026"
    mp.font.name = FONT_FAMILY
    mp.font.size = Pt(13)
    mp.font.color.rgb = COLOR_TEXT

    # =========================================================================
    # СЛАЙД 1: ИНФОРМАЦИЯ О НАС (КОМАНДА)
    # =========================================================================
    s1 = prs.slides.add_slide(blank_layout)
    apply_background(s1)
    add_header(s1, "Информация о нас", "Авторы проекта и команда разработки")

    members = [
        ("Перов Владислав Артёмович", "Тимлид / Архитектура / OCR", "Распознавание документов, машиночитаемые зоны паспорта и ID-карт (MRZ), движок Tesseract, экспорт в DOCX и PDF.", COLOR_BORDER),
        ("Мурашко Роман Янович", "ИИ-интеграция / Backend", "Генерация формулировок с помощью ИИ (Groq / Workers AI), логика безопасного шлюза Zero-Text Logging, аудит полей.", COLOR_CYAN),
        ("Ходонович Даниил Русланович", "Интерфейс / UX / Навигация", "Кроссплатформенный дизайн Office Blue (.NET 10 MAUI), адаптивная навигация, валидация реквизитов и доступность.", COLOR_EMERALD)
    ]

    left_start = Inches(0.8)
    card_w = Inches(3.7)
    card_h = Inches(4.6)
    gap = Inches(0.3)

    for i, (name, role, desc, color) in enumerate(members):
        cur_left = left_start + i * (card_w + gap)
        add_card(s1, cur_left, Inches(1.8), card_w, card_h, border_color=color)

        # Контент карточки
        cbox = s1.shapes.add_textbox(cur_left + Inches(0.3), Inches(2.1), card_w - Inches(0.6), card_h - Inches(0.6))
        ctf = cbox.text_frame
        ctf.word_wrap = True

        p = ctf.paragraphs[0]
        p.text = f"Участник #{i+1}"
        p.font.size = Pt(11)
        p.font.bold = True
        p.font.color.rgb = color

        p_name = ctf.add_paragraph()
        p_name.text = name
        p_name.font.size = Pt(18)
        p_name.font.bold = True
        p_name.font.color.rgb = COLOR_WHITE
        p_name.space_after = Pt(10)

        p_role = ctf.add_paragraph()
        p_role.text = role
        p_role.font.size = Pt(12)
        p_role.font.bold = True
        p_role.font.color.rgb = COLOR_CYAN
        p_role.space_after = Pt(14)

        p_desc = ctf.add_paragraph()
        p_desc.text = desc
        p_desc.font.size = Pt(12)
        p_desc.font.color.rgb = COLOR_MUTED

    # Футер колледжа
    cinfo = s1.shapes.add_textbox(Inches(0.8), Inches(6.6), Inches(11.7), Inches(0.5))
    ci_p = cinfo.text_frame.paragraphs[0]
    ci_p.text = "🏛️ УО «Бобруйский государственный лесомеханический колледж» · Специальность: Разработка программного обеспечения"
    ci_p.font.size = Pt(12)
    ci_p.font.color.rgb = COLOR_TEXT

    # =========================================================================
    # СЛАЙД 2: ЦЕЛЬ ПРОЕКТА
    # =========================================================================
    s2 = prs.slides.add_slide(blank_layout)
    apply_background(s2)
    add_header(s2, "Цель проекта", "Зачем создан GosTek и какую ценность он несёт")

    # Левая колонка: Главная цель
    add_card(s2, Inches(0.8), Inches(1.8), Inches(5.7), Inches(4.9), border_color=COLOR_BORDER)
    left_box = s2.shapes.add_textbox(Inches(1.1), Inches(2.1), Inches(5.1), Inches(4.3))
    ltf = left_box.text_frame
    ltf.word_wrap = True

    p = ltf.paragraphs[0]
    p.text = "ГЛАВНАЯ МИССИЯ"
    p.font.size = Pt(12)
    p.font.bold = True
    p.font.color.rgb = COLOR_CYAN

    p2 = ltf.add_paragraph()
    p2.text = "Радикально упростить подготовку документов и сократить время их оформления с 15-20 минут до 30 секунд."
    p2.font.size = Pt(20)
    p2.font.bold = True
    p2.font.color.rgb = COLOR_WHITE
    p2.space_after = Pt(16)

    p3 = ltf.add_paragraph()
    p3.text = "Дать любому студенту, преподавателю или сотруднику возможность самостоятельно получить юридически грамотный, выверенный по ГОСТу документ, готовый к мгновенной печати или отправке."
    p3.font.size = Pt(14)
    p3.font.color.rgb = COLOR_MUTED

    # Правая колонка: 4 конкретных результата
    results = [
        ("⚡ Сокращение времени на 90%", "Автозаполнение исключает рутинный ручной ввод одних и тех же реквизитов."),
        ("🛡️ Нулевой риск ошибок", "Алгоритмическая проверка УНП, IBAN, паспортных данных и дат до сборки файла."),
        ("📏 Строгое соблюдение ГОСТ", "Автоматический контроль шрифта 14 пт, полей 30/15/20/20 мм и красной строки 1.25 см."),
        ("🖨️ Готовые форматы .DOCX и .PDF", "Прямая отправка на печать или экспорт в редактируемый Word без съехавшей разметки.")
    ]

    for j, (rtitle, rdesc) in enumerate(results):
        top_pos = Inches(1.8) + j * Inches(1.22)
        add_card(s2, Inches(6.8), top_pos, Inches(5.7), Inches(1.1))
        rbox = s2.shapes.add_textbox(Inches(7.0), top_pos + Inches(0.12), Inches(5.3), Inches(0.85))
        rtf = rbox.text_frame
        rtf.word_wrap = True
        rp1 = rtf.paragraphs[0]
        rp1.text = rtitle
        rp1.font.size = Pt(14)
        rp1.font.bold = True
        rp1.font.color.rgb = COLOR_CYAN
        rp2 = rtf.add_paragraph()
        rp2.text = rdesc
        rp2.font.size = Pt(11)
        rp2.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 3: ЗАДАЧИ ПРОЕКТА
    # =========================================================================
    s3 = prs.slides.add_slide(blank_layout)
    apply_background(s3)
    add_header(s3, "Задачи проекта", "Ключевые инженерные задачи, решённые в GosTek")

    tasks = [
        ("1. Конвертация бланков в формы", "Разработать модуль преобразования встроенных и пользовательских шаблонов Word (.docx) в динамические поля ввода без необходимости программирования."),
        ("2. Интеллектуальный OCR-сканер", "Реализовать оптическое распознавание документов с камеры на Android и из файлов на Windows с извлечением структурированных данных паспортов и ID-карт."),
        ("3. Движок нормаконтроля (Инспектор)", "Создать автоматический инспектор разметки DOCX, сверяющий структуру документа с правилами ГОСТ Р 7.0.97-2016 и исправляющий ошибки в 1 клик."),
        ("4. Аппаратная безопасность данных", "Внедрить защищённое локальное хранилище SecureStorage (Android KeyStore / Windows DPAPI) и реализовать строгий принцип Zero-Text Logging.")
    ]

    for k, (ttitle, tdesc) in enumerate(tasks):
        top_pos = Inches(1.8) + k * Inches(1.25)
        add_card(s3, Inches(0.8), top_pos, Inches(11.7), Inches(1.15), border_color=COLOR_BORDER if k == 0 else None)
        tbox = s3.shapes.add_textbox(Inches(1.1), top_pos + Inches(0.15), Inches(11.1), Inches(0.85))
        ttf = tbox.text_frame
        ttf.word_wrap = True
        p = ttf.paragraphs[0]
        p.text = ttitle
        p.font.size = Pt(16)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN
        p2 = ttf.add_paragraph()
        p2.text = tdesc
        p2.font.size = Pt(12)
        p2.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 4: АКТУАЛЬНОСТЬ ПРОЕКТА
    # =========================================================================
    s4 = prs.slides.add_slide(blank_layout)
    apply_background(s4)
    add_header(s4, "Актуальность", "Где теряется время и возникают ошибки при работе с документами?")

    p_cards = [
        ("Постоянный повторный ввод", "ФИО, паспортные данные, адрес, телефоны, УНП и IBAN дублируются в десятках заявлений, справок и договоров. Рутинный набор отнимает драгоценное время и приводит к опечаткам.", "⏰ До 20 минут на бланк", COLOR_BORDER),
        ("Опасность «старых файлов»", "Когда за основу берётся чужой старый файл Word, легко пропустить скрытое поле: чужую дату, фамилию или неактуальную сумму. Такой документ отклоняется секретариатом.", "⚠️ Человеческий фактор", COLOR_AMBER),
        ("Ручная проверка оформления", "Перед сдачей курсовой, отчёта или заявления приходится вручную сверять поля (30 мм слева?), шрифт (14 пт?), отступ красной строки (1.25 см) и интервал (1.5). Глаз легко пропускает сбой.", "🔍 До 70% замечаний", COLOR_CYAN)
    ]

    for m, (title, desc, badge_txt, col) in enumerate(p_cards):
        left_p = left_start + m * (card_w + gap)
        add_card(s4, left_p, Inches(1.8), card_w, Inches(4.9), border_color=col)
        pbox = s4.shapes.add_textbox(left_p + Inches(0.3), Inches(2.1), card_w - Inches(0.6), Inches(4.3))
        ptf = pbox.text_frame
        ptf.word_wrap = True

        p1 = ptf.paragraphs[0]
        p1.text = badge_txt
        p1.font.size = Pt(11)
        p1.font.bold = True
        p1.font.color.rgb = col

        p2 = ptf.add_paragraph()
        p2.text = title
        p2.font.size = Pt(18)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_WHITE
        p2.space_after = Pt(14)

        p3 = ptf.add_paragraph()
        p3.text = desc
        p3.font.size = Pt(13)
        p3.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 5: ИННОВАЦИОННОСТЬ GOSTEK
    # =========================================================================
    s5 = prs.slides.add_slide(blank_layout)
    apply_background(s5)
    add_header(s5, "Инновационность", "Ключевые технологические отличия и преимущества GosTek")

    innovations = [
        ("Белорусская специфика", "Полный учет национальных стандартов: встроенные бланки колледжа, поддержка 9-значного УНП, контрольная сумма IBAN mod 97, серия и номер паспорта РБ и формат ID-карт."),
        ("Word-файл превращается в форму", "Любая текстовая метка вида {{ФИО}} или [Дата-00.00.0000] автоматически становится полем интерфейса. Достаточно загрузить свой DOCX, и приложение готово к работе."),
        ("Автоматическое исправление в 1 клик", "Инспектор не просто выводит список замечаний, а формирует отдельную чистую копию с автоматически исправленными абзацами, интервалами и полями."),
        ("Бескомпромиссная приватность (Zero-Text)", "Никаких облачных баз данных. Все реквизиты хранятся на устройстве в аппаратно защищенном виде. Облачный ИИ подключается строго по явному согласию.")
    ]

    for n, (ititle, idesc) in enumerate(innovations):
        left_coord = Inches(0.8) if n % 2 == 0 else Inches(6.8)
        top_coord = Inches(1.8) if n < 2 else Inches(4.3)
        add_card(s5, left_coord, top_coord, Inches(5.7), Inches(2.2), border_color=COLOR_BORDER if n == 0 else None)

        ibox = s5.shapes.add_textbox(left_coord + Inches(0.3), top_coord + Inches(0.2), Inches(5.1), Inches(1.8))
        itf = ibox.text_frame
        itf.word_wrap = True

        p1 = itf.paragraphs[0]
        p1.text = ititle
        p1.font.size = Pt(16)
        p1.font.bold = True
        p1.font.color.rgb = COLOR_CYAN

        p2 = itf.add_paragraph()
        p2.text = idesc
        p2.font.size = Pt(12)
        p2.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 6: ПЕРЕЧИСЛЕНИЕ ВСЕХ МОДУЛЕЙ
    # =========================================================================
    s6 = prs.slides.add_slide(blank_layout)
    apply_background(s6)
    add_header(s6, "Модули системы", "Модульная архитектура GosTek: 7 ключевых компонентов")

    # Сетка 7 модулей
    all_modules = [
        ("01", "Шаблоны Word", "Каталог встроенных бланков и импорт пользовательских файлов DOCX."),
        ("02", "Профиль реквизитов", "Безопасное хранение ФИО, паспорта, телефона, УНП и IBAN."),
        ("03", "Сканер документов (OCR)", "Распознавание текста с камеры и сканов, чтение паспорта и ID-карт."),
        ("04", "ИИ-ассистент текста", "Помощь в формулировке деловых объяснительных строго по согласию."),
        ("05", "Инспектор DOCX", "Нормаконтроль Word-файлов по правилам ГОСТ и исправление ошибок."),
        ("06", "Редактор правил", "Настройка требований к полям, шрифтам, отступам и таблицам."),
        ("07", "Экспорт DOCX и PDF", "Генерация редактируемого Word и полиграфического PDF в 1 клик.")
    ]

    # Верхний ряд (4 модуля)
    mod_w = Inches(2.7)
    gap_m = Inches(0.27)
    for q in range(4):
        num, mname, msub = all_modules[q]
        add_card(s6, Inches(0.8) + q * (mod_w + gap_m), Inches(1.8), mod_w, Inches(2.3))
        mb = s6.shapes.add_textbox(Inches(0.95) + q * (mod_w + gap_m), Inches(1.95), mod_w - Inches(0.3), Inches(2.0))
        mtf = mb.text_frame
        mtf.word_wrap = True
        p = mtf.paragraphs[0]
        p.text = num
        p.font.size = Pt(20)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN
        p2 = mtf.add_paragraph()
        p2.text = mname
        p2.font.size = Pt(14)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_WHITE
        p3 = mtf.add_paragraph()
        p3.text = msub
        p3.font.size = Pt(10)
        p3.font.color.rgb = COLOR_MUTED

    # Нижний ряд (3 модуля пошире)
    mod_w2 = Inches(3.7)
    gap_m2 = Inches(0.3)
    for q in range(4, 7):
        num, mname, msub = all_modules[q]
        idx = q - 4
        add_card(s6, Inches(0.8) + idx * (mod_w2 + gap_m2), Inches(4.35), mod_w2, Inches(2.3))
        mb = s6.shapes.add_textbox(Inches(0.95) + idx * (mod_w2 + gap_m2), Inches(4.5), mod_w2 - Inches(0.3), Inches(2.0))
        mtf = mb.text_frame
        mtf.word_wrap = True
        p = mtf.paragraphs[0]
        p.text = num
        p.font.size = Pt(20)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN
        p2 = mtf.add_paragraph()
        p2.text = mname
        p2.font.size = Pt(14)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_WHITE
        p3 = mtf.add_paragraph()
        p3.text = msub
        p3.font.size = Pt(11)
        p3.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙДЫ С ОПИСАНИЕМ КАЖДОГО МОДУЛЯ (7 слайдов)
    # =========================================================================

    def add_module_slide(num_str, title_str, subtitle_str, bullets, screenshot_name, screenshot_label):
        s = prs.slides.add_slide(blank_layout)
        apply_background(s)
        add_header(s, f"Модуль {num_str}", title_str, category_text="GosTek · Архитектура системы")

        # Левая часть: текстовое описание
        add_card(s, Inches(0.8), Inches(1.8), Inches(5.8), Inches(4.9), border_color=COLOR_BORDER)
        tbox = s.shapes.add_textbox(Inches(1.1), Inches(2.1), Inches(5.2), Inches(4.3))
        tf = tbox.text_frame
        tf.word_wrap = True

        p = tf.paragraphs[0]
        p.text = subtitle_str.upper()
        p.font.size = Pt(11)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN
        p.space_after = Pt(10)

        for btitle, bdesc in bullets:
            pb_title = tf.add_paragraph()
            pb_title.text = f"●  {btitle}"
            pb_title.font.size = Pt(13)
            pb_title.font.bold = True
            pb_title.font.color.rgb = COLOR_WHITE

            pb_desc = tf.add_paragraph()
            pb_desc.text = f"    {bdesc}"
            pb_desc.font.size = Pt(11)
            pb_desc.font.color.rgb = COLOR_MUTED
            pb_desc.space_after = Pt(8)

        # Правая часть: место под реальный скриншот
        add_screenshot_placeholder(s, Inches(6.9), Inches(1.8), Inches(5.6), Inches(4.9), screenshot_label, screenshot_name)
        return s

    # 1. Шаблоны Word
    add_module_slide(
        "01", "Модуль «Шаблоны Word»", "Каталог бланков и парсер пользовательских DOCX",
        [
            ("Встроенная библиотека колледжа", "Заявления на академический отпуск, общежитие, материальную помощь, справки об обучении, договоры и объяснительные записки."),
            ("Метки подстановки {{...}}", "Любой собственный DOCX-файл размечается метками [ФИО], [Дата] или {{Причина}} и превращается в интерактивную форму."),
            ("Интеллектуальная группировка", "Поиск по ключевым словам, разделение на академические и деловые бланки, быстрый фильтр «Избранные».")
        ],
        "screen-win-main.png", "Каталог шаблонов и выбор документа"
    )

    # 2. Профиль с реквизитами
    add_module_slide(
        "02", "Модуль «Профиль с реквизитами»", "Безопасная база персональных данных и реквизитов",
        [
            ("Белорусские реквизиты", "ФИО, паспортные данные, контакты, учебная группа, а также реквизиты организации: УНП (9 цифр) и расчётный счёт IBAN."),
            ("Валидация прямо при вводе", "Проверка контрольной суммы IBAN по алгоритму mod 97 и длины УНП исключает ошибки до подстановки в бланк."),
            ("Аппаратное шифрование", "Персональные данные хранятся локально в SecureStorage (Android KeyStore / Windows DPAPI), защищены от утечек.")
        ],
        "screen-android-settings.png", "Настройки профиля и сохранение реквизитов"
    )

    # 3. Сканер и распознавание (OCR)
    add_module_slide(
        "03", "Модуль «Сканер документов (OCR)»", "Оцифровка бумажных носителей и распознавание MRZ",
        [
            ("Оцифровка со смартфона и ПК", "На Android — съемка камерой или выбор фото из галереи; на Windows — загрузка скана с предпросмотром."),
            ("Распознавание паспорта и ID-карт", "Движок Tesseract находит 2 машиночитаемые строки паспорта РБ или 3 строки ID-карты, извлекая номер, дату и пол."),
            ("Прямой перенос в профиль", "Пользователь сверяет распознанный текст и в 1 клик переносит реквизиты в профиль или активный шаблон.")
        ],
        "screen-win-scan.png", "Интерфейс оцифровки скана и распознавания OCR"
    )

    # 4. ИИ-ассистент формулировок
    add_module_slide(
        "04", "Модуль «ИИ-генерация текста»", "Интеллектуальный помощник для делового стиля",
        [
            ("Помощь в формулировке фактов", "Автор сообщает краткие тезисы («опоздал на 15 мин из-за поломки автобуса»), а ИИ формирует корректный текст."),
            ("Диалоговое уточнение", "Прямо в интерфейсе можно попросить сократить, сделать тон более строгим или дополнить текст аргументами."),
            ("Принцип честного согласия", "ИИ работает через Groq / Cloudflare Workers AI. Запросы отправляются строго по кнопке с подтверждением пользователя.")
        ],
        "screen-win-fields.png", "Генерация текста и заполнение полей бланка"
    )

    # 5. Инспектор DOCX
    add_module_slide(
        "05", "Модуль «Инспектор DOCX»", "Автоматический нормаконтроль готовых документов",
        [
            ("Глубокий аудит разметки", "Инспектор анализирует структуру готового файла Word на расхождения с правилами оформления."),
            ("Контроль всех параметров", "Проверяются поля страницы, гарнитура, кегль, полуторный межстрочный интервал (1.5), красная строка (1.25 см) и нумерация."),
            ("Автоматическое исправление", "Создаётся отдельная исправленная копия документа, где все замечания устранены, а исходный файл сохранён.")
        ],
        "screen-win-fields.png", "Отчёт инспектора с выявленными замечаниями"
    )

    # 6. Редактор правил оформления
    add_module_slide(
        "06", "Модуль «Редактор правил»", "Гибкая настройка стандартов под требования организации",
        [
            ("Параметры страницы", "Настройка полей (левое 30 мм, правое 15 мм, верх/низ 20 мм), ориентации и расположения колонтитулов."),
            ("Основной текст", "Указание строгого шрифта (Times New Roman 14), выравнивания по ширине и запрета лишних пробелов."),
            ("Таблицы и иллюстрации", "Отдельные правила для подписей к рисункам и таблицам, позволяющие в одном файле настроить всё оформление отчёта.")
        ],
        "screen-win-main.png", "Редактор параметров правил нормаконтроля"
    )

    # 7. Экспорт в DOCX и PDF
    add_module_slide(
        "07", "Модуль «Экспорт в DOCX и PDF»", "Выгрузка готовых файлов в редактируемом и печатном виде",
        [
            ("Формат Microsoft Word (.docx)", "Создаётся чистый документ OpenXML для дальнейшего редактирования и согласования преподавателем."),
            ("Нативный полиграфический PDF", "Идеален для печати и отправки в электронном виде: стабильная сетка A4, которая не поедет на чужом принтере."),
            ("Предпечатная настройка", "Возможность поменять шрифт, интервал и поля непосредственно в окне экспорта без повреждения шаблона.")
        ],
        "screen-android-editor.png", "Окно выбора формата экспорта и сохранения"
    )

    # =========================================================================
    # СЛАЙД 14: ПЛАНЫ НА БУДУЩЕЕ И ПЕРСПЕКТИВЫ
    # =========================================================================
    s14 = prs.slides.add_slide(blank_layout)
    apply_background(s14)
    add_header(s14, "Планы на будущее", "Перспективы развития и будущие возможности GosTek")

    futures = [
        ("☁️ Облачный хаб шаблонов колледжа", "Централизованный репозиторий бланков: секретарь обновляет форму заявления в админке, и она мгновенно обновляется у всех студентов без перевыпуска приложения."),
        ("📑 Пакетный нормаконтроль для преподавателей", "Массовая проверка всей папки с отчётами группы (30+ файлов Word) за один клик с выводом сводной таблицы нарушений ГОСТ."),
        ("✍️ Интеграция с ЭЦП (ГосСУОК Беларуси)", "Подписание сгенерированных PDF-документов личной электронной цифровой подписью прямо в мобильном приложении или на ПК."),
        ("🔍 Визуальное сравнение версий (Diff)", "Инструмент для автора: наглядное сравнение двух версий документа с подсветкой изменённых полей и параметров разметки."),
        ("🌐 Web-версия (WebAssembly / Blazor)", "Запуск генератора и аудитора документов в любом браузере без необходимости скачивания приложения.")
    ]

    for u, (ftitle, fdesc) in enumerate(futures):
        top_pos = Inches(1.8) + u * Inches(0.98)
        add_card(s14, Inches(0.8), top_pos, Inches(11.7), Inches(0.88), border_color=COLOR_BORDER if u < 2 else None)
        fbox = s14.shapes.add_textbox(Inches(1.1), top_pos + Inches(0.08), Inches(11.1), Inches(0.72))
        ftf = fbox.text_frame
        ftf.word_wrap = True
        p = ftf.paragraphs[0]
        p.text = ftitle
        p.font.size = Pt(14)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN
        p2 = ftf.add_paragraph()
        p2.text = fdesc
        p2.font.size = Pt(11)
        p2.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 15: МОНЕТИЗАЦИЯ И ПИЛОТНЫЕ ВНЕДРЕНИЯ
    # =========================================================================
    s15 = prs.slides.add_slide(blank_layout)
    apply_background(s15)
    add_header(s15, "Монетизация и пилот", "Практическая ценность для колледжей и предприятий")

    monetization = [
        ("Оцифровка бланков организации", "Колледж или предприятие передает свои действующие формы. Команда размечает шаблоны, проверяет автозаполнение и поставляет настроенный комплект.", "💼 Внедрение под ключ", COLOR_BORDER),
        ("Подписка на актуальные бланки", "При изменении нормативных требований или бланков заявлений команда оперативно обновляет комплект форм и гарантирует корректность реквизитов.", "🔄 Сервисное сопровождение", COLOR_CYAN),
        ("Проведение пилота в БГЛК", "Предложить пилотный запуск с тремя ключевыми бланками (отпуск, матпомощь, общежитие). Оценить снижение нагрузки на секретаря и преподавателей.", "🚀 Пилотный проект 2026", COLOR_EMERALD)
    ]

    for v, (mtitle, mdesc, mtag, mcol) in enumerate(monetization):
        left_pos = left_start + v * (card_w + gap)
        add_card(s15, left_pos, Inches(1.8), card_w, Inches(4.9), border_color=mcol)
        mbox = s15.shapes.add_textbox(left_pos + Inches(0.3), Inches(2.1), card_w - Inches(0.6), Inches(4.3))
        mtf = mbox.text_frame
        mtf.word_wrap = True

        p1 = mtf.paragraphs[0]
        p1.text = mtag
        p1.font.size = Pt(11)
        p1.font.bold = True
        p1.font.color.rgb = mcol

        p2 = mtf.add_paragraph()
        p2.text = mtitle
        p2.font.size = Pt(18)
        p2.font.bold = True
        p2.font.color.rgb = COLOR_WHITE
        p2.space_after = Pt(14)

        p3 = mtf.add_paragraph()
        p3.text = mdesc
        p3.font.size = Pt(13)
        p3.font.color.rgb = COLOR_MUTED

    # =========================================================================
    # СЛАЙД 16: ЗАКЛЮЧЕНИЕ
    # =========================================================================
    s16 = prs.slides.add_slide(blank_layout)
    apply_background(s16)

    add_card(s16, Inches(0.8), Inches(0.8), Inches(11.733), Inches(5.9), bg_color=RGBColor(12, 22, 40), border_color=COLOR_BORDER)

    # Заголовок
    zbox = s16.shapes.add_textbox(Inches(1.4), Inches(1.2), Inches(10.5), Inches(1.4))
    ztf = zbox.text_frame
    ztf.word_wrap = True
    p = ztf.paragraphs[0]
    p.text = "Заключение"
    p.font.name = FONT_FAMILY
    p.font.size = Pt(36)
    p.font.bold = True
    p.font.color.rgb = COLOR_WHITE

    p2 = ztf.add_paragraph()
    p2.text = "GosTek — готовое решение для экономии времени и стандартизации документов"
    p2.font.name = FONT_FAMILY
    p2.font.size = Pt(16)
    p2.font.color.rgb = COLOR_CYAN

    # 3 ключевых факта о готовности
    facts = [
        ("✅ Проект полностью реализован", "Работает на Windows 10/11 и Android 8+. 370 автоматических тестов xUnit подтверждают стабильность всех модулей."),
        ("🛡️ Безопасность и автономность", "Offline-First архитектура, Zero-Text Logging, шифрование SecureStorage. Никаких утечек персональных данных."),
        ("📦 Открытый репозиторий и сайт", "Сайт загрузки готов (GosTek-site), открытый исходный код доступен на GitHub.")
    ]

    for f_idx, (ftit, fsub) in enumerate(facts):
        top_f = Inches(2.7) + f_idx * Inches(1.0)
        add_card(s16, Inches(1.4), top_f, Inches(10.5), Inches(0.88), bg_color=COLOR_BG_CARD)
        fbox = s16.shapes.add_textbox(Inches(1.6), top_f + Inches(0.1), Inches(10.1), Inches(0.68))
        ftf = fbox.text_frame
        ftf.word_wrap = True
        fp1 = ftf.paragraphs[0]
        fp1.text = ftit
        fp1.font.size = Pt(14)
        fp1.font.bold = True
        fp1.font.color.rgb = COLOR_WHITE
        fp2 = ftf.add_paragraph()
        fp2.text = fsub
        fp2.font.size = Pt(11)
        fp2.font.color.rgb = COLOR_MUTED

    # Контакты и репозиторий
    cbox = s16.shapes.add_textbox(Inches(1.4), Inches(5.8), Inches(10.5), Inches(0.7))
    ctf = cbox.text_frame
    cp = ctf.paragraphs[0]
    cp.text = "🔗 GitHub: github.com/Vladislav-Perov/GosTek  |  Спасибо за внимание! Готовы ответить на вопросы."
    cp.font.name = FONT_FAMILY
    cp.font.size = Pt(13)
    cp.font.bold = True
    cp.font.color.rgb = COLOR_CYAN

    # Сохраняем презентацию
    out_dir = r"D:\GosTek-site"
    out_path = os.path.join(out_dir, "GosTek-Presentation.pptx")
    prs.save(out_path)
    print(f"Презентация успешно создана: {out_path}")

    # Также скопируем в корень проекта для удобства
    root_copy = r"D:\Progect_Gostek\GosTek-Presentation.pptx"
    prs.save(root_copy)
    print(f"Копия сохранена: {root_copy}")

if __name__ == "__main__":
    create_presentation()
