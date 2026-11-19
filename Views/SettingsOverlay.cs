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
    internal const int OverlayZIndex = 10000;

    private static SettingsPanel? s_panel;
    private static bool s_opening;

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
        if (s_opening || IsOpen)
            return;

        var page = targetPage ?? Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return;

        s_opening = true;
        try
        {
            var panel = GetOrCreatePanel();
            var grid = EnsureGrid(page);
            if (!ReferenceEquals(panel.Parent, grid))
            {
                if (panel.Parent is Layout oldParent)
                    oldParent.Children.Remove(panel);

                Grid.SetRowSpan(panel, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));
                panel.ZIndex = OverlayZIndex;
                grid.Children.Add(panel);
            }

            await panel.OpenAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SettingsOverlay] {ex}");
            var failedPanel = s_panel;
            s_panel = null;
            try
            {
                failedPanel?.Dismiss();
                if (page.Window is not null)
                    await page.DisplayAlertAsync("Настройки", "Не удалось открыть настройки. Попробуйте ещё раз.", "Понятно");
            }
            catch (Exception recoveryError)
            {
                Console.Error.WriteLine($"[SettingsOverlay] {recoveryError}");
            }
        }
        finally
        {
            s_opening = false;
        }
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
