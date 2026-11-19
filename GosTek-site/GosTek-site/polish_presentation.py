"""
Скрипт доработки и полировки официальной презентации GosTek
На базе файла: GosTek — синяя палитра (2).pptx
"""

import os
import pptx
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN
from pptx.enum.shapes import MSO_SHAPE

def polish_presentation():
    base_file = r"D:\Progect_Gostek\GosTek-Presentation-Base.pptx"
    prs = pptx.Presentation(base_file)

    # Цвета палитры презентации
    COLOR_BG_CARD = RGBColor(7, 30, 60)      # #071E3C
    COLOR_CYAN    = RGBColor(15, 158, 213)   # #0F9ED5
    COLOR_WHITE   = RGBColor(255, 255, 255)
    COLOR_MUTED   = RGBColor(187, 201, 218)  # #BBC9DA
    COLOR_ACCENT  = RGBColor(29, 95, 156)    # #1D5F9C
    COLOR_BORDER  = RGBColor(37, 99, 235)

    FONT_FAMILY = "Segoe UI"

    # =========================================================================
    # 1. СЛАЙД 3: ИСПРАВЛЕНИЕ ОШИБКИ КОПИПАСТА В ЗАДАЧАХ
    # =========================================================================
    slide3 = prs.slides[2]
    # Находим текстовые блоки
    # TextBox 17: "Проверять оформление готовых документов"
    # TextBox 18 ошибочно содержал текст про распознавание фото!
    for s in slide3.shapes:
        if s.has_text_frame:
            t = s.text_frame.text.strip()
            # Заменяем ошибочный текст в блоке описания 3-й задачи
            if s.name == "TextBox 18" or (t.startswith("Распознавать текст с фотографии") and s.top.inches > 5.5):
                s.text_frame.clear()
                p = s.text_frame.paragraphs[0]
                p.text = "Находить в готовом DOCX-файле абзацы с неверным интервалом, полями или шрифтом и автоматически исправлять их по правилам ГОСТ, сохраняя исходный файл."
                p.font.name = FONT_FAMILY
                p.font.size = Pt(13)
                p.font.color.rgb = COLOR_MUTED
                print("Слайд 3: Ошибка копипаста в задаче #3 успешно исправлена!")

    # =========================================================================
    # 2. СЛАЙД 8: МОДУЛЬ «ШАБЛОНЫ» — ПЛЕЙСХОЛДЕР СКРИНШОТА
    # =========================================================================
    slide8 = prs.slides[7]
    for s in slide8.shapes:
        if s.name == "Прямоугольник 7" or (s.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.AUTO_SHAPE and s.left.inches > 8.0):
            s.fill.solid()
            s.fill.fore_color.rgb = COLOR_BG_CARD
            s.line.color.rgb = COLOR_CYAN
            s.line.width = Pt(1.5)
            s.text_frame.word_wrap = True
            p = s.text_frame.paragraphs[0]
            p.text = "🖼️ КАТАЛОГ ШАБЛОНОВ"
            p.alignment = PP_ALIGN.CENTER
            p.font.name = FONT_FAMILY
            p.font.size = Pt(14)
            p.font.bold = True
            p.font.color.rgb = COLOR_CYAN

            p2 = s.text_frame.add_paragraph()
            p2.text = "\nАвтоматическая генерация формы\nиз разметки Word {{...}}\n\n[Место для скриншота: screen-win-main.png]"
            p2.alignment = PP_ALIGN.CENTER
            p2.font.name = FONT_FAMILY
            p2.font.size = Pt(11)
            p2.font.color.rgb = COLOR_MUTED
            print("Слайд 8: Плейсхолдер скриншота оформлен!")

    # =========================================================================
    # 3. СЛАЙД 9: МОДУЛЬ «ПРОФИЛЬ» — ПЛЕЙСХОЛДЕР СКРИНШОТА МОБИЛКИ
    # =========================================================================
    slide9 = prs.slides[8]
    for s in slide9.shapes:
        if s.name == "Прямоугольник 7" or (s.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.AUTO_SHAPE and s.left.inches > 9.0):
            s.fill.solid()
            s.fill.fore_color.rgb = COLOR_BG_CARD
            s.line.color.rgb = COLOR_CYAN
            s.line.width = Pt(1.5)
            s.text_frame.word_wrap = True
            p = s.text_frame.paragraphs[0]
            p.text = "📱 ПРОФИЛЬ"
            p.alignment = PP_ALIGN.CENTER
            p.font.name = FONT_FAMILY
            p.font.size = Pt(13)
            p.font.bold = True
            p.font.color.rgb = COLOR_CYAN

            p2 = s.text_frame.add_paragraph()
            p2.text = "\nРеквизиты\nУНП и IBAN\n\nШифрование\nSecureStorage\n\n[Скриншот:\nscreen-android-settings.png]"
            p2.alignment = PP_ALIGN.CENTER
            p2.font.name = FONT_FAMILY
            p2.font.size = Pt(10)
            p2.font.color.rgb = COLOR_MUTED
            print("Слайд 9: Мобильный плейсхолдер профиля оформлен!")

    # =========================================================================
    # 4. СЛАЙД 10: МОДУЛЬ «СКАНЕР» — ПЛЕЙСХОЛДЕР СКРИНШОТА СКАНЕРА
    # =========================================================================
    slide10 = prs.slides[9]
    for s in slide10.shapes:
        if s.name == "Прямоугольник 7" or (s.shape_type == pptx.enum.shapes.MSO_SHAPE_TYPE.AUTO_SHAPE and s.left.inches > 9.0):
            s.fill.solid()
            s.fill.fore_color.rgb = COLOR_BG_CARD
            s.line.color.rgb = COLOR_CYAN
            s.line.width = Pt(1.5)
            s.text_frame.word_wrap = True
            p = s.text_frame.paragraphs[0]
            p.text = "📷 СКАНЕР OCR"
            p.alignment = PP_ALIGN.CENTER
            p.font.name = FONT_FAMILY
            p.font.size = Pt(13)
            p.font.bold = True
            p.font.color.rgb = COLOR_CYAN

            p2 = s.text_frame.add_paragraph()
            p2.text = "\nКамера / Скан\n\nЧтение 2-3 строк\nMRZ паспорта\nи ID-карты РБ\n\n[Скриншот:\nscreen-win-scan.png]"
            p2.alignment = PP_ALIGN.CENTER
            p2.font.name = FONT_FAMILY
            p2.font.size = Pt(10)
            p2.font.color.rgb = COLOR_MUTED
            print("Слайд 10: Плейсхолдер сканера оформлен!")

    # =========================================================================
    # 5. СЛАЙД 15: БУДУЩИЕ ВОЗМОЖНОСТИ (РАСШИРЕНИЕ ТЕЗИСАМИ ИЗ РЕЧИ)
    # =========================================================================
    slide15 = prs.slides[14]
    # Добавим подпись к блоку "Предложения для пилота" о расширенных возможностях
    for s in slide15.shapes:
        if s.name == "TextBox 12" and s.has_text_frame:
            s.text_frame.text = "Перспективы: Умные правила из методички вуза · Сканирование бланков · Голосовой ввод ИИ · Синхронизация"
            s.text_frame.paragraphs[0].font.size = Pt(11)
            s.text_frame.paragraphs[0].font.color.rgb = COLOR_CYAN
            print("Слайд 15: Добавлены расширенные тезисы будущих возможностей!")

    # =========================================================================
    # 6. СЛАЙД 16: БУДУЩАЯ МОНЕТИЗАЦИЯ (РАСШИРЕНИЕ)
    # =========================================================================
    slide16 = prs.slides[15]
    for s in slide16.shapes:
        if s.name == "TextBox 12" and s.has_text_frame:
            s.text_frame.text = "Модели дохода: Платная подписка на генерацию с ИИ · Скидки для студентов · Сопровождение бланков организаций"
            s.text_frame.paragraphs[0].font.size = Pt(11)
            s.text_frame.paragraphs[0].font.color.rgb = COLOR_CYAN
            print("Слайд 16: Добавлены тезисы монетизации!")

    # =========================================================================
    # 7. СЛАЙД 17: ЗАКЛЮЧЕНИЕ — ДОБАВЛЕНИЕ QR-КОДА И ФИНАЛЬНЫХ СЛОВ
    # =========================================================================
    slide17 = prs.slides[16]

    # Добавляем изображение QR-кода справа
    qr_img_path = r"D:\GosTek-site\assets\qr_github.png"
    if os.path.exists(qr_img_path):
        qr_left = Inches(9.2)
        qr_top = Inches(2.5)
        qr_width = Inches(3.2)
        qr_height = Inches(3.2)

        # Подложка под QR-код
        qr_bg = slide17.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, qr_left, qr_top, qr_width, qr_height)
        qr_bg.fill.solid()
        qr_bg.fill.fore_color.rgb = COLOR_BG_CARD
        qr_bg.line.color.rgb = COLOR_CYAN
        qr_bg.line.width = Pt(1.5)

        # Картинка QR-кода
        slide17.shapes.add_picture(qr_img_path, qr_left + Inches(0.4), qr_top + Inches(0.2), Inches(2.4), Inches(2.4))

        # Подпись под QR-кодом
        qr_cap = slide17.shapes.add_textbox(qr_left, qr_top + Inches(2.65), qr_width, Inches(0.5))
        p = qr_cap.text_frame.paragraphs[0]
        p.text = "Скачать проект / GitHub"
        p.alignment = PP_ALIGN.CENTER
        p.font.name = FONT_FAMILY
        p.font.size = Pt(10)
        p.font.bold = True
        p.font.color.rgb = COLOR_CYAN

        # Завершающая строка внизу слайда
        fin_box = slide17.shapes.add_textbox(Inches(0.83), Inches(6.4), Inches(11.67), Inches(0.6))
        p_fin = fin_box.text_frame.paragraphs[0]
        p_fin.text = "Спасибо за внимание! Готовы ответить на ваши вопросы."
        p_fin.font.name = FONT_FAMILY
        p_fin.font.size = Pt(16)
        p_fin.font.bold = True
        p_fin.font.color.rgb = COLOR_WHITE
        print("Слайд 17: QR-код и заключительное обращение добавлены!")

    # Сохраняем готовую доработанную презентацию на диск D: (где есть свободное место)
    out_paths = [
        r"D:\Progect_Gostek\GosTek — презентация (доработанная).pptx",
        r"D:\GosTek-site\GosTek-Presentation.pptx"
    ]

    for p in out_paths:
        prs.save(p)
        print(f"Сохранено: {p}")

if __name__ == "__main__":
    polish_presentation()
