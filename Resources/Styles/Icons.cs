/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Класс-хелпер для работы с векторными иконками и SVG Path Data
 * СОДЕРЖИМОЕ: Векторные пути (M...), хелпер разрешения иконок ResolvePath, создание элементов Path
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls.Shapes, Microsoft.Maui.Graphics
 * ==============================================================================
 */
using Microsoft.Maui.Controls.Shapes;
using MauiPath = Microsoft.Maui.Controls.Shapes.Path;

namespace GosTek.Resources.Styles;

public static class Icons
{
    public const string Sliders = "M4 21v-7m0-4V3m8 21v-9m0-4V3m8 21v-5m0-4V3M1 14h6m2-6h6m2 8h6";
    public const string Shield = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";
    public const string Camera = "M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z M12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    public const string Image = "M19 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2z M8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3z M21 15l-5-5L5 21";
    public const string Folder = "M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z";
    public const string Save = "M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z M17 21v-8H7v8 M7 3v5h8";
    public const string Plus = "M12 5v14M5 12h14";
    public const string Copy = "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2 M15 2H9a1 1 0 0 0-1 1v2a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1V3a1 1 0 0 0-1-1z";
    public const string Calendar = "M19 4H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2z M16 2v4 M8 2v4 M3 10h18";
    public const string Star = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z";
    public const string StarFilled = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z";
    public const string Trash = "M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6";
    public const string Eye = "M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z";
    public const string Sparkles = "M12 3v3m0 12v3m9-9h-3M6 12H3m15.364-6.364l-2.121 2.121M7.757 16.243l-2.121 2.121m12.728 0l-2.121-2.121M7.757 7.757L5.636 5.636";
    public const string User = "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z";
    public const string Bulb = "M9 18h6m-4 4h2m4-12a6 6 0 1 0-10 0c0 2.5 2 4 2 6h4c0-2 2-3.5 2-6z";
    public const string Info = "M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20z M12 16v-4 M12 8h.01";
    public const string FileText = "M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z M14 2v6h6 M16 13H8 M16 17H8 M10 9H8";
    public const string Check = "M20 6L9 17l-5-5";
    public const string AlertTriangle = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z M12 9v4 M12 17h.01";
    public const string Sun = "M12 1v2m0 18v2M4.22 4.22l1.42 1.42m12.72 12.72l1.42 1.42M1 12h2m18 0h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10z";
    public const string Moon = "M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z";
    public const string Pen = "M12 20h9 M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z";
    public const string Ruler = "M21.3 8.7l-5.6-5.6a1 1 0 0 0-1.4 0L2.7 14.7a1 1 0 0 0 0 1.4l5.6 5.6a1 1 0 0 0 1.4 0L21.3 10.1a1 1 0 0 0 0-1.4z M7.5 10.5l2 2 M10.5 7.5l2 2 M13.5 4.5l2 2";
    public const string Share = "M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8 M16 6l-4-4-4 4 M12 2v13";
    public const string Search = "M21 21l-4.35-4.35M17 10a7 7 0 1 1-14 0 7 7 0 0 1 14 0z";
    public const string ChevronDown = "M6 9l6 6 6-6";
    public const string ChevronUp = "M18 15l-6-6-6 6";
    public const string ChevronRight = "M9 18l6-6-6-6";
    public const string ChevronLeft = "M15 18l-6-6 6-6";

    /// <summary>
    /// Возвращает PathData по строковому имени иконки или эмодзи.
    /// </summary>
    public static string ResolvePath(string iconNameOrEmoji)
    {
        if (string.IsNullOrWhiteSpace(iconNameOrEmoji))
            return FileText;

        if (iconNameOrEmoji.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            return iconNameOrEmoji;

        return iconNameOrEmoji.ToLowerInvariant() switch
        {
            "sliders" or "settings" or "⚙️" or "🛠️" => Sliders,
            "shield" or "security" or "check" or "🛡️" => Shield,
            "camera" or "scan" or "📷" => Camera,
            "image" or "gallery" or "photo" or "🖼️" => Image,
            "folder" or "templates" or "📁" or "📑" => Folder,
            "save" or "disk" or "💾" => Save,
            "plus" or "add" or "➕" => Plus,
            "copy" or "📋" => Copy,
            "calendar" or "date" or "📅" => Calendar,
            "star" or "favorite" or "⭐" or "☆" => Star,
            "starfilled" or "star_filled" => StarFilled,
            "trash" or "delete" or "remove" or "🗑️" => Trash,
            "eye" or "preview" or "👁️" => Eye,
            "sparkles" or "ai" or "magic" or "✨" => Sparkles,
            "user" or "profile" or "person" or "👤" => User,
            "bulb" or "idea" or "hint" or "💡" => Bulb,
            "info" or "notice" or "ℹ️" => Info,
            "file" or "doc" or "docx" or "filetext" or "📄" => FileText,
            "check_ok" or "success" or "✓" or "✅" => Check,
            "warning" or "alert" or "⚠️" => AlertTriangle,
            "sun" or "☀️" => Sun,
            "moon" or "🌙" => Moon,
            "pen" or "edit" or "✏️" => Pen,
            "ruler" or "📐" => Ruler,
            "share" or "↗" or "📤" => Share,
            "search" or "🔍" => Search,
            "chevrondown" or "chevron_down" => ChevronDown,
            "chevronup" or "chevron_up" => ChevronUp,
            "chevronright" or "chevron_right" => ChevronRight,
            "chevronleft" or "chevron_left" => ChevronLeft,
            _ => FileText,
        };
    }

    /// <summary>
    /// Создает векторный элемент Path с заданными параметрами.
    /// </summary>
    public static MauiPath CreatePath(
        string iconOrData,
        double size = 18,
        string? strokeColorResource = "TextPrimary",
        string? fillColorResource = null,
        double strokeThickness = 2.0)
    {
        string pathData = ResolvePath(iconOrData);
        var path = new MauiPath
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(pathData)!,
            WidthRequest = size,
            HeightRequest = size,
            Aspect = Stretch.Uniform,
            StrokeThickness = strokeThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        if (!string.IsNullOrEmpty(strokeColorResource))
            path.SetDynamicResource(MauiPath.StrokeProperty, strokeColorResource);

        if (!string.IsNullOrEmpty(fillColorResource))
            path.SetDynamicResource(MauiPath.FillProperty, fillColorResource);

        return path;
    }
}
