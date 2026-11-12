/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис управления пользовательскими правилами оформления ГОСТ и автоопределения по категориям
 * СОДЕРЖИМОЕ: ICustomRulesService, CustomRulesService
 * ЗАВИСИМОСТИ: GosTek.Models.RuleSet, GosTek.Models.RuleSets, GosTek.Models.CustomRuleDto
 * ==============================================================================
 */
using System.Diagnostics;
using System.Text.Json;
using GosTek.Models;

namespace GosTek.Services;

/// <summary>
/// Интерфейс сервиса управления правилами оформления ГОСТ.
/// </summary>
public interface ICustomRulesService
{
    IReadOnlyList<RuleSet> GetAllRules();
    IReadOnlyList<RuleSet> GetCustomRules();
    Task<IReadOnlyList<RuleSet>> LoadRulesAsync();
    Task<bool> SaveRuleAsync(RuleSet rule);
    Task<bool> DeleteRuleAsync(string id);
    string ExtractCategoryFromFileName(string? fileName);
    RuleSet MatchRuleSetForTemplate(string? fileName);
}

/// <summary>
/// Сервис сохранения, загрузки и автоподбора пользовательских правил оформления ГОСТ.
/// Хранение осуществляется в локальном JSON-файле без использования сторонних СУБД.
/// </summary>
public class CustomRulesService : ICustomRulesService
{
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _storageFilePath;
    private readonly List<RuleSet> _customRules = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _loaded;

    public static ICustomRulesService Instance { get; set; } = new CustomRulesService();

    public CustomRulesService(string? storageFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(storageFilePath)) {
            _storageFilePath = storageFilePath;
        } else {
            try {
                _storageFilePath = Path.Combine(FileSystem.AppDataDirectory, "custom_rules.json");
            } catch {
                _storageFilePath = Path.Combine(Path.GetTempPath(), "gostek_custom_rules.json");
            }
        }
    }

    public IReadOnlyList<RuleSet> GetAllRules()
    {
        var list = new List<RuleSet>();
        foreach (var preset in RuleSets.Presets)
            list.Add(preset.Rules);

        lock (_customRules) {
            list.AddRange(_customRules);
        }
        return list;
    }

    public IReadOnlyList<RuleSet> GetCustomRules()
    {
        lock (_customRules) {
            return _customRules.ToList();
        }
    }

    public async Task<IReadOnlyList<RuleSet>> LoadRulesAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try {
            if (!File.Exists(_storageFilePath)) {
                _loaded = true;
                return Array.Empty<RuleSet>();
            }

            string json;
            try {
                json = await File.ReadAllTextAsync(_storageFilePath).ConfigureAwait(false);
            } catch (Exception ex) {
                Debug.WriteLine($"[CustomRulesService] Ошибка чтения файла: {ex.Message}");
                return Array.Empty<RuleSet>();
            }

            if (string.IsNullOrWhiteSpace(json)) {
                _loaded = true;
                return Array.Empty<RuleSet>();
            }

            try {
                var dtos = JsonSerializer.Deserialize<List<CustomRuleDto>>(json, JsonOptions);
                lock (_customRules) {
                    _customRules.Clear();
                    if (dtos != null) {
                        foreach (var dto in dtos) {
                            if (!string.IsNullOrWhiteSpace(dto.Title))
                                _customRules.Add(RuleSet.FromDto(dto));
                        }
                    }
                }
                _loaded = true;
            } catch (JsonException ex) {
                Debug.WriteLine($"[CustomRulesService] Повреждён JSON-файл: {ex.Message}");
                try {
                    var backupPath = $"{_storageFilePath}.corrupted.{DateTime.UtcNow:yyyyMMddHHmmss}";
                    File.Copy(_storageFilePath, backupPath, overwrite: true);
                } catch {
                    // Игнорируем ошибку резервного копирования
                }
            }

            return GetCustomRules();
        } finally {
            _lock.Release();
        }
    }

    public async Task<bool> SaveRuleAsync(RuleSet rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Title))
            throw new ArgumentException("Название правила не может быть пустым.", nameof(rule));

        await _lock.WaitAsync();
        try {
            if (!_loaded && File.Exists(_storageFilePath)) {
                try {
                    var json = await File.ReadAllTextAsync(_storageFilePath);
                    var dtos = JsonSerializer.Deserialize<List<CustomRuleDto>>(json, JsonOptions);
                    lock (_customRules) {
                        _customRules.Clear();
                        if (dtos != null) {
                            foreach (var d in dtos) {
                                if (!string.IsNullOrWhiteSpace(d.Title))
                                    _customRules.Add(RuleSet.FromDto(d));
                            }
                        }
                    }
                } catch {
                    // Игнорируем
                }
                _loaded = true;
            }

            var id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N")[..8] : rule.Id;
            var customRule = rule with { Id = id, IsCustom = true };

            lock (_customRules) {
                int existingIdx = _customRules.FindIndex(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                    _customRules[existingIdx] = customRule;
                else
                    _customRules.Add(customRule);
            }

            var dir = Path.GetDirectoryName(_storageFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            List<CustomRuleDto> dtosToSave;
            lock (_customRules) {
                dtosToSave = _customRules.Select(r => r.ToDto()).ToList();
            }

            var serialized = JsonSerializer.Serialize(dtosToSave, JsonOptions);
            await File.WriteAllTextAsync(_storageFilePath, serialized);
            return true;
        } catch (Exception ex) {
            Debug.WriteLine($"[CustomRulesService] Ошибка сохранения правила: {ex.Message}");
            return false;
        } finally {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteRuleAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        // Защита встроенных системных правил от удаления
        if (id.Equals("universal", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("study_bgtu", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("applications", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("contracts", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        await _lock.WaitAsync();
        try {
            bool removed;
            lock (_customRules) {
                int idx = _customRules.FindIndex(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                    return false;
                _customRules.RemoveAt(idx);
                removed = true;
            }

            if (removed) {
                List<CustomRuleDto> dtosToSave;
                lock (_customRules) {
                    dtosToSave = _customRules.Select(r => r.ToDto()).ToList();
                }

                var serialized = JsonSerializer.Serialize(dtosToSave, JsonOptions);
                await File.WriteAllTextAsync(_storageFilePath, serialized);
            }
            return removed;
        } catch (Exception ex) {
            Debug.WriteLine($"[CustomRulesService] Ошибка удаления правила: {ex.Message}");
            return false;
        } finally {
            _lock.Release();
        }
    }

    public string ExtractCategoryFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "";

        var stem = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (string.IsNullOrEmpty(stem))
            return "";

        int sepIdx = stem.IndexOf('_');
        if (sepIdx > 0)
            return stem[..sepIdx].Trim();

        int dashIdx = stem.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIdx > 0)
            return stem[..dashIdx].Trim();

        int spaceIdx = stem.IndexOf(' ');
        if (spaceIdx > 0)
            return stem[..spaceIdx].Trim();

        return stem;
    }

    public RuleSet MatchRuleSetForTemplate(string? fileName)
    {
        var category = ExtractCategoryFromFileName(fileName);
        if (string.IsNullOrWhiteSpace(category))
            return RuleSets.Universal;

        // 1. Поиск среди пользовательских правил
        lock (_customRules) {
            var customMatch = _customRules.FirstOrDefault(r =>
                (!string.IsNullOrWhiteSpace(r.Category) && r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) ||
                r.Title.Contains(category, StringComparison.OrdinalIgnoreCase));
            if (customMatch != null)
                return customMatch;
        }

        // 2. Поиск среди встроенных пресетов
        foreach (var preset in RuleSets.Presets) {
            if (!string.IsNullOrWhiteSpace(preset.Rules.Category) &&
                preset.Rules.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                return preset.Rules;

            if (preset.Title.Contains(category, StringComparison.OrdinalIgnoreCase))
                return preset.Rules;
        }

        // 3. Базовый ГОСТ по умолчанию
        return RuleSets.Universal;
    }
}
