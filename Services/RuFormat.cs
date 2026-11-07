using System.Globalization;

namespace GosTek.Services;

/// <summary>Числа и склонения для русских надписей («2,5 см», «1 страница», «5 страниц»).</summary>
public static class RuFormat
{
    /// <summary>Число с запятой, без лишних нулей: 1,5; 14; 2,5.</summary>
    public static string Number(double value, int maxDecimals = 1)
    {
        var format = maxDecimals <= 0 ? "0" : "0." + new string('#', maxDecimals);
        return value.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');
    }

    /// <summary>Склонение по числу: Plural(1, "страница", "страницы", "страниц").</summary>
    public static string Plural(int n, string one, string few, string many)
    {
        int mod100 = Math.Abs(n) % 100;
        int mod10 = mod100 % 10;

        if (mod100 is >= 11 and <= 14)
            return many;

        return mod10 switch {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many,
        };
    }

    /// <summary>«42 страницы».</summary>
    public static string Count(int n, string one, string few, string many)
        => $"{n} {Plural(n, one, few, many)}";

    /// <summary>Размер файла: «48 КБ», «1,2 МБ».</summary>
    public static string FileSize(long bytes)
        => bytes switch {
            < 1024 => $"{bytes} Б",
            < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} КБ",
            _ => $"{Number(bytes / 1024.0 / 1024.0)} МБ",
        };
}
