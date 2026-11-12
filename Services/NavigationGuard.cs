/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Защита от параллельной двойной навигации в Shell (Double Navigation Guard)
 * СОДЕРЖИМОЕ: NavigationGuard
 * ЗАВИСИМОСТИ: System.Threading, System.Threading.Tasks
 * ==============================================================================
 */

namespace GosTek.Services;

public static class NavigationGuard
{
    private static int _isNavigating;

    /// <summary>
    /// Выполняет переход, предотвращая одновременные двойные клики и белый экран Shell.
    /// </summary>
    public static async Task<bool> NavigateAsync(Func<Task> navigateAction, int debounceMs = 200)
    {
        if (Interlocked.CompareExchange(ref _isNavigating, 1, 0) != 0)
            return false;

        try
        {
            await navigateAction();
            return true;
        }
        finally
        {
            if (debounceMs > 0)
                await Task.Delay(debounceMs);
            Interlocked.Exchange(ref _isNavigating, 0);
        }
    }

    /// <summary>
    /// Сброс флага для модульного тестирования.
    /// </summary>
    public static void ResetForTesting()
    {
        Interlocked.Exchange(ref _isNavigating, 0);
    }
}
