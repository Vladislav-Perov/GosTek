/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модели параметров ГОСТ-оформления документа для настраиваемого экспорта в DOCX
 * СОДЕРЖИМОЕ: GostFormattingOptions
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml
 * ==============================================================================
 */
namespace GosTek.Models;

public sealed class GostFormattingOptions
{
    public string FontFamily { get; set; } = "Times New Roman";
    
    // В пунктах (pt), по ГОСТ: 14pt (допускается 12pt)
    public double FontSizePt { get; set; } = 14.0;
    
    // Коэффициент межстрочного интервала: 1.0, 1.15, 1.5 (по ГОСТ 1.5)
    public double LineSpacing { get; set; } = 1.5;
    
    // Абзацный отступ (красная строка) в сантиметрах: 1.25 см по ГОСТ
    public double FirstLineIndentCm { get; set; } = 1.25;
    
    // Выравнивание основного текста: "Justify" (по ширине, ГОСТ) или "Left" (по левому краю)
    public string Alignment { get; set; } = "Justify";
    
    // Поля страницы в миллиметрах (ГОСТ: Левое 30 мм, Правое 15 мм, Верхнее 20 мм, Нижнее 20 мм)
    public double MarginLeftMm { get; set; } = 30.0;
    public double MarginRightMm { get; set; } = 15.0;
    public double MarginTopMm { get; set; } = 20.0;
    public double MarginBottomMm { get; set; } = 20.0;
    
    // Дополнительные флаги обработки
    public bool AutoFormatLists { get; set; } = true;
    public bool AutoDetectSignatures { get; set; } = true;
    public bool AutoDetectTitle { get; set; } = true;

    /// <summary>
    /// Создает эталонный экземпляр со стандартными параметрами ГОСТ 7.0.97-2016 / СТБ 6.38.
    /// </summary>
    public static GostFormattingOptions CreateDefault() => new();
}
