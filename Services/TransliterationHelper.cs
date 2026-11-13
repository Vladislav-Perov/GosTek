/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Умная транслитерация белорусских имен и фамилий с латиницы (MRZ) на русский язык
 * СОДЕРЖИМОЕ: TransliterationHelper
 * ЗАВИСИМОСТИ: System, System.Collections.Generic
 * ==============================================================================
 */
using System.Text;

namespace GosTek.Services;

/// <summary>
/// Хелпер транслитерации имен и фамилий граждан РБ со словарем популярных имен и правилами для фамилий.
/// </summary>
public static class TransliterationHelper
{
    // Словарь официальной белорусской латиницы в русские имена
    private static readonly Dictionary<string, string> NameDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SIARHEI"] = "СЕРГЕЙ",
        ["SERGEI"] = "СЕРГЕЙ",
        ["SERGEY"] = "СЕРГЕЙ",
        ["ALIAKSANDR"] = "АЛЕКСАНДР",
        ["ALEKSANDR"] = "АЛЕКСАНДР",
        ["ALEXANDER"] = "АЛЕКСАНДР",
        ["DZMITRY"] = "ДМИТРИЙ",
        ["DMITRY"] = "ДМИТРИЙ",
        ["DMITRII"] = "ДМИТРИЙ",
        ["YAUHENI"] = "ЕВГЕНИЙ",
        ["EVGENY"] = "ЕВГЕНИЙ",
        ["EVGENII"] = "ЕВГЕНИЙ",
        ["ANDREI"] = "АНДРЕЙ",
        ["ANDREY"] = "АНДРЕЙ",
        ["ULADZIMIR"] = "ВЛАДИМИР",
        ["VLADIMIR"] = "ВЛАДИМИР",
        ["MIKHAIL"] = "МИХАИЛ",
        ["PAVAL"] = "ПАВЕЛ",
        ["PAVEL"] = "ПАВЕЛ",
        ["TATSIANA"] = "ТАТЬЯНА",
        ["TATIANA"] = "ТАТЬЯНА",
        ["ALENA"] = "ЕЛЕНА",
        ["ELENA"] = "ЕЛЕНА",
        ["VOLHA"] = "ОЛЬГА",
        ["OLGA"] = "ОЛЬГА",
        ["NATALLIA"] = "НАТАЛЬЯ",
        ["NATALIA"] = "НАТАЛЬЯ",
        ["IRYNA"] = "ИРИНА",
        ["IRINA"] = "ИРИНА",
        ["SVIATLANA"] = "СВЕТЛАНА",
        ["SVETLANA"] = "СВЕТЛАНА",
        ["KATSIARYNA"] = "ЕКАТЕРИНА",
        ["EKATERINA"] = "ЕКАТЕРИНА",
        ["MARYNA"] = "МАРИНА",
        ["MARINA"] = "МАРИНА",
        ["ANNA"] = "АННА",
        ["HANNA"] = "АННА",
    };

    // Многобуквенные замены для фамилий
    private static readonly (string Latin, string Cyrillic)[] MultiCharRules =
    {
        ("SH", "Ш"),
        ("CH", "Ч"),
        ("ZH", "Ж"),
        ("KH", "Х"),
        ("TS", "Ц"),
        ("YA", "Я"),
        ("YU", "Ю"),
        ("YE", "Е"),
        ("DZ", "ДЗ"),
    };

    // Одиночные буквы
    private static readonly Dictionary<char, string> SingleCharRules = new()
    {
        ['A'] = "А",
        ['B'] = "Б",
        ['V'] = "В",
        ['G'] = "Г",
        ['D'] = "Д",
        ['E'] = "Е",
        ['Z'] = "З",
        ['I'] = "И",
        ['J'] = "Й",
        ['K'] = "К",
        ['L'] = "Л",
        ['M'] = "М",
        ['N'] = "Н",
        ['O'] = "О",
        ['P'] = "П",
        ['R'] = "Р",
        ['S'] = "С",
        ['T'] = "Т",
        ['U'] = "У",
        ['F'] = "Ф",
        ['H'] = "Х",
        ['C'] = "Ц",
        ['Y'] = "Ы",
        ['W'] = "В",
        ['X'] = "КС",
        ['Q'] = "К",
    };

    /// <summary>
    /// Универсальный метод транслитерации слова (имени или фамилии).
    /// </summary>
    public static string Transliterate(string rawWord, bool isName = false)
    {
        if (string.IsNullOrWhiteSpace(rawWord))
            return string.Empty;

        if (isName || IsKnownName(rawWord))
            return TransliterateName(rawWord);

        return TransliterateSurname(rawWord);
    }

    /// <summary>
    /// Проверяет, содержится ли имя в словаре популярных белорусских имен.
    /// </summary>
    public static bool IsKnownName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return false;

        string name = rawName.Trim().ToUpperInvariant().Replace('1', 'I').Replace('0', 'O');
        return NameDictionary.ContainsKey(name);
    }

    /// <summary>
    /// Транслитерирует имя с латиницы на русский язык (с использованием словаря).
    /// </summary>
    public static string TransliterateName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return string.Empty;

        // ⚠️ КРИТИЧНО: Приведение к верхнему регистру перед поиском в словаре
        string name = rawName.Trim().ToUpperInvariant();

        // Исправление типичной путаницы OCR в именах (например, ANDRE1 -> ANDREI)
        name = name.Replace('1', 'I').Replace('0', 'O');

        if (NameDictionary.TryGetValue(name, out string? cyrillicName))
            return cyrillicName;

        return TransliterateSurname(name);
    }

    /// <summary>
    /// Транслитерирует фамилию с латиницы на русский язык по правилам ICAO / транслитерации РБ.
    /// </summary>
    public static string TransliterateSurname(string rawSurname)
    {
        if (string.IsNullOrWhiteSpace(rawSurname))
            return string.Empty;

        // ⚠️ КРИТИЧНО: Приведение к верхнему регистру
        string surname = rawSurname.Trim().ToUpperInvariant();
        surname = surname.Replace('1', 'I').Replace('0', 'O');

        var sb = new StringBuilder();
        int i = 0;
        while (i < surname.Length)
        {
            bool matchedMulti = false;
            foreach (var (latin, cyr) in MultiCharRules)
            {
                if (i + latin.Length <= surname.Length &&
                    surname.AsSpan(i, latin.Length).Equals(latin.AsSpan(), StringComparison.Ordinal))
                {
                    sb.Append(cyr);
                    i += latin.Length;
                    matchedMulti = true;
                    break;
                }
            }

            if (matchedMulti)
                continue;

            char c = surname[i];
            if (SingleCharRules.TryGetValue(c, out string? singleCyr))
                sb.Append(singleCyr);
            else
                sb.Append(c);

            i++;
        }

        return sb.ToString();
    }
}
