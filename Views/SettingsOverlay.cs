/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Глобальный оверлей шторки настроек приложения поверх любой активной страницы
 * СОДЕРЖИМОЕ: SettingsOverlay
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.SettingsPanel
 * ==============================================================================
 */
namespace GosTek.Views;

/// <summary>
/// Глобальный менеджер панели настроек: отображает выезжающую боковую шторку
/// поверх текущей активной страницы без перехода на другие вкладки.
/// </summary>
public static class SettingsOverlay
{
    private static SettingsPanel? s_panel;

    /// <summary>Открыта ли в данный момент панель настроек.</summary>
    public static bool IsOpen => s_panel?.IsShown ?? false;

    /// <summary>Событие удаления данных пользователем.</summary>
    public static event Action? DataCleared;

    private static SettingsPanel GetOrCreatePanel()
    {
        if (s_panel is null)
        {
            s_panel = new SettingsPanel();
            s_panel.DataCleared += () => DataCleared?.Invoke();
        }
        return s_panel;
    }

    /// <summary>
    /// Открыть шторку настроек поверх текущей активной страницы.
    /// </summary>
    public static async Task ShowAsync(Page? targetPage = null)
    {
        var page = targetPage ?? Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        var panel = GetOrCreatePanel();

        // Если панель уже прикреплена к другой странице, отсоединяем её
        if (panel.Parent is Layout oldParent)
        {
            if (page is ContentPage cp && ReferenceEquals(oldParent, cp.Content))
            {
                // Уже прикреплена к целевой странице
            }
            else
            {
                oldParent.Children.Remove(panel);
            }
        }

        var grid = EnsureGrid(page);
        if (!grid.Children.Contains(panel))
        {
            if (grid.RowDefinitions.Count > 0)
                Grid.SetRowSpan(panel, Math.Max(1, grid.RowDefinitions.Count));
            if (grid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));

            panel.ZIndex = 10000;
            grid.Children.Add(panel);
        }

        await panel.OpenAsync();
    }

    /// <summary>
    /// Закрыть шторку настроек.
    /// </summary>
    public static async Task CloseAsync()
    {
        if (s_panel is not null && s_panel.IsShown)
            await s_panel.CloseAsync();
    }

    /// <summary>
    /// Принудительно и немедленно закрывает панель настроек и отсоединяет её от визуального дерева.
    /// Вызывается при навигации и смене страниц для предотвращения залипания оверлея.
    /// </summary>
    public static void Dismiss()
    {
        if (s_panel is not null)
            s_panel.Dismiss();
    }

    private static Grid EnsureGrid(Page page)
    {
        if (page is ContentPage { Content: Grid grid })
            return grid;

        if (page is not ContentPage contentPage)
            throw new InvalidOperationException("Оверлей настроек можно показать только на ContentPage.");

        var wrapper = new Grid();
        var old = contentPage.Content;
        contentPage.Content = null;

        if (old is not null)
            wrapper.Add(old);

        contentPage.Content = wrapper;
        return wrapper;
    }
}
