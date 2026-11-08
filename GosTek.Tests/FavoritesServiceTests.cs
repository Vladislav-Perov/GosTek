/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты сервиса управления избранными шаблонами (FavoritesService)
 * СОДЕРЖИМОЕ: FavoritesServiceTests
 * ЗАВИСИМОСТИ: GosTek.Services, Xunit
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class FavoritesServiceTests
{
    private readonly FakePreferences _fakePrefs;
    private readonly FavoritesService _service;

    public FavoritesServiceTests()
    {
        _fakePrefs = new FakePreferences();
        FavoritesService.PreferencesProvider = _fakePrefs;
        _service = new FavoritesService();
    }

    [Fact]
    public void GetAll_InitiallyEmpty_ReturnsEmptyList()
    {
        var items = _service.GetAll();
        Assert.Empty(items);
    }

    [Fact]
    public void IsFavorite_NonExistent_ReturnsFalse()
    {
        Assert.False(_service.IsFavorite("template_1"));
        Assert.False(_service.IsFavorite(null));
        Assert.False(_service.IsFavorite(""));
    }

    [Fact]
    public void Toggle_AddAndRemove_TogglesCorrectly()
    {
        // Добавление
        bool added = _service.Toggle("template_1");
        Assert.True(added);
        Assert.True(_service.IsFavorite("template_1"));
        Assert.Contains("template_1", _service.GetAll());

        // Удаление
        bool removed = _service.Toggle("template_1");
        Assert.False(removed);
        Assert.False(_service.IsFavorite("template_1"));
        Assert.DoesNotContain("template_1", _service.GetAll());
    }

    [Fact]
    public void Toggle_MultipleItems_MaintainsOrder()
    {
        _service.Toggle("template_1");
        _service.Toggle("template_2");

        var all = _service.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("template_2", all[0]); // последняя добавленная первая
        Assert.Equal("template_1", all[1]);
    }

    [Fact]
    public void Clear_RemovesAllFavorites()
    {
        _service.Toggle("t1");
        _service.Toggle("t2");
        Assert.Equal(2, _service.GetAll().Count);

        _service.Clear();
        Assert.Empty(_service.GetAll());
    }
}
