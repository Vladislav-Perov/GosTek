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
    string? LastSavedRuleId { get; }
    RuleSet MasterRule { get; }
    IReadOnlyList<RuleSet> GetAllRules();
    IReadOnlyList<RuleSet> GetCustomRules();
    Task<IReadOnlyList<RuleSet>> LoadRulesAsync();
    Task<bool> SaveRuleAsync(RuleSet rule);
    Task<bool> DeleteRuleAsync(string id);
    string ExtractCategoryFromFileName(string? fileName);
    RuleSet MatchRuleSetForTemplate(string? fileName);
    RuleSet CreateNewRule();
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
    private RuleSet _masterRule = RuleSets.MasterRule;
    private readonly List<RuleSet> _customRules = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _loaded;

    public string? LastSavedRuleId { get; private set; }

    public RuleSet MasterRule
    {
        get
        {
            lock (_customRules)
            {
                return _masterRule;
            }
        }
    }

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

    public static string NormalizeRuleId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
            id.Equals("universal", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("study_bgtu", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("applications", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("contracts", StringComparison.OrdinalIgnoreCase))
        {
            return RuleSets.MasterRuleId;
        }
        return id;
    }

    public IReadOnlyList<RuleSet> GetAllRules()
    {
        lock (_customRules) {
            var list = new List<RuleSet> { _masterRule };
            list.AddRange(_customRules);
            return list;
        }
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
        try
        {
            await TryLoadRulesAsync().ConfigureAwait(false);
            return GetCustomRules();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<bool> TryLoadRulesAsync()
    {
        try
        {
            var json = File.Exists(_storageFilePath)
                ? await File.ReadAllTextAsync(_storageFilePath).ConfigureAwait(false)
                : null;
            var dtos = string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<List<CustomRuleDto>>(json, JsonOptions);

            var master = RuleSets.MasterRule;
            var custom = new List<RuleSet>();
            foreach (var dto in dtos ?? [])
            {
                if (string.IsNullOrWhiteSpace(dto.Title))
                    continue;

                var rule = RuleSet.FromDto(dto);
                if (rule.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
                    rule.Id.Equals("universal", StringComparison.OrdinalIgnoreCase))
                {
                    master = rule with { Id = RuleSets.MasterRuleId, Title = "Основной ГОСТ", IsCustom = false };
                }
                else
                {
                    custom.Add(rule);
                }
            }

            SetRules(master, custom);
            _loaded = true;
            return true;
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[CustomRulesService] Повреждён JSON-файл: {ex.Message}");
            try
            {
                var backupPath = $"{_storageFilePath}.corrupted.{DateTime.UtcNow:yyyyMMddHHmmss}";
                File.Copy(_storageFilePath, backupPath, overwrite: true);
            }
            catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[CustomRulesService] Ошибка резервного копирования: {backupError.Message}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[CustomRulesService] Ошибка чтения файла: {ex.Message}");
        }

        _loaded = false;
        return false;
    }

    public async Task<bool> SaveRuleAsync(RuleSet rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        if (string.IsNullOrWhiteSpace(rule.Title))
            throw new ArgumentException("Название правила не может быть пустым.", nameof(rule));

        await _lock.WaitAsync();
        try {
            if (!_loaded && !await TryLoadRulesAsync())
                return false;

            bool isMaster = rule.Id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
                            rule.Id.Equals("universal", StringComparison.OrdinalIgnoreCase);

            var targetId = isMaster
                ? RuleSets.MasterRuleId
                : string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N")[..8] : rule.Id;
            var master = MasterRule;
            var custom = GetCustomRules().ToList();
            if (isMaster)
            {
                master = rule with { Id = targetId, Title = "Основной ГОСТ", IsCustom = false };
            }
            else
            {
                var updated = rule with { Id = targetId, IsCustom = true };
                int index = custom.FindIndex(item => item.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    custom[index] = updated;
                else
                    custom.Add(updated);
            }

            await PersistRulesAsync(master, custom);
            LastSavedRuleId = targetId;
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

        // Защита главного правила и системных Id от удаления
        if (id.Equals(RuleSets.MasterRuleId, StringComparison.OrdinalIgnoreCase) ||
            id.Equals("universal", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("study_bgtu", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("applications", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("contracts", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        await _lock.WaitAsync();
        try {
            if (!_loaded && !await TryLoadRulesAsync())
                return false;

            var custom = GetCustomRules().ToList();
            var index = custom.FindIndex(rule => rule.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            custom.RemoveAt(index);
            await PersistRulesAsync(MasterRule, custom);
            return true;
        } catch (Exception ex) {
            Debug.WriteLine($"[CustomRulesService] Ошибка удаления правила: {ex.Message}");
            return false;
        } finally {
            _lock.Release();
        }
    }

    private async Task PersistRulesAsync(RuleSet master, List<RuleSet> custom)
    {
        var target = Path.GetFullPath(_storageFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            var dtos = new List<CustomRuleDto> { master.ToDto() };
            dtos.AddRange(custom.Select(rule => rule.ToDto()));
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(dtos, JsonOptions));
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[CustomRulesService] Не удалось удалить временный файл: {ex.Message}");
            }
        }

        SetRules(master, custom);
    }

    private void SetRules(RuleSet master, List<RuleSet> custom)
    {
        lock (_customRules)
        {
            _masterRule = master;
            _customRules.Clear();
            _customRules.AddRange(custom);
        }
    }

    public RuleSet CreateNewRule()
    {
        string baseName = "Новое правило";
        string candidate = baseName;
        int counter = 2;
        lock (_customRules)
        {
            var existingTitles = new HashSet<string>(_customRules.Select(r => r.Title), StringComparer.OrdinalIgnoreCase);
            existingTitles.Add(_masterRule.Title);
            while (existingTitles.Contains(candidate))
            {
                candidate = $"{baseName} {counter++}";
            }
        }

        string newId = Guid.NewGuid().ToString("N")[..8];
        var newRule = _masterRule with
        {
            Id = newId,
            Title = candidate,
            IsCustom = true,
            Category = null,
            Description = "Пользовательское правило"
        };
        return newRule;
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
            return _masterRule;

        // 1. Поиск среди пользовательских правил
        lock (_customRules) {
            var customMatch = _customRules.FirstOrDefault(r =>
                (!string.IsNullOrWhiteSpace(r.Category) && r.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) ||
                r.Title.Contains(category, StringComparison.OrdinalIgnoreCase));
            if (customMatch != null)
                return customMatch;
        }

        // 2. Главное правило «Основной ГОСТ» по умолчанию
        return _masterRule;
    }
}
