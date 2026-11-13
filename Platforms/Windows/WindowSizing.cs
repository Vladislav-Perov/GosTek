/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Фиксация минимальных габаритов окна 800x600 на Windows WinUI 3 через Win32 Subclassing (WM_GETMINMAXINFO)
 *              и нативная область перетаскивания (Drag Region) с учетом ширины Navigation Rail
 * СОДЕРЖИМОЕ: WindowSizing
 * ЗАВИСИМОСТИ: Microsoft.UI.Xaml, WinRT.Interop, System.Runtime.InteropServices, Microsoft.Maui.Controls
 * ==============================================================================
 */
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GosTek.Platforms.Windows;

/// <summary>
/// Платформенный помощник для установки аппаратных ограничений минимального размера окна WinUI 3
/// и конфигурации нативной области перетаскивания (Drag Region).
/// Перехватывает Win32-сообщение WM_GETMINMAXINFO, запрещая уменьшение окна меньше 800x600 с учетом DPI.
/// Оптимизирован для максимальной производительности: мгновенный возврат DefSubclassProc для всех прочих сообщений,
/// кэширование DPI без вызовов P/Invoke на кадрах движения и прямое чтение памяти без маршалинга структуры.
/// </summary>
public static class WindowSizing
{
    public const int MinWidthDips = 800;
    public const int MinHeightDips = 600;

    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint WM_DPICHANGED = 0x02E0;
    private const uint WM_NCDESTROY = 0x0082;

    // Смещения полей ptMinTrackSize.x и ptMinTrackSize.y в структуре MINMAXINFO
    private const int PtMinTrackSizeXOffset = 24;
    private const int PtMinTrackSizeYOffset = 28;
    private static readonly UIntPtr SubclassId = (UIntPtr)101;

    // Кэшированные значения минимальных размеров в физических пикселях (с учетом DPI)
    private static int s_cachedMinWidthPixels;
    private static int s_cachedMinHeightPixels;

    // Ссылка на текущее активное окно для динамического обновления Drag Region
    private static WeakReference<Microsoft.UI.Xaml.Window>? s_activeWindowRef;

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    // Статическая ссылка на делегат защищает от сбора сборщиком мусора (.NET GC)
    private static readonly SubclassProc s_subclassProc = SubclassWndProc;

    /// <summary>
    /// Вычисляет геометрию прямоугольника области перетаскивания (Drag Region) окна.
    /// Смещается вправо на ширину Navigation Rail (220px или 64px),
    /// чтобы верхняя часть боковой панели (логотип, тумблер сворачивания) получала события клика мыши.
    /// Чистая функция, покрытая модульными тестами.
    /// </summary>
    public static global::Windows.Graphics.RectInt32 CalculateDragRect(
        int totalWidthPixels,
        double rasterizationScale,
        int railWidthDips,
        int leftInsetPixels = 0,
        int rightInsetPixels = 0,
        int titleBarHeightDips = 48)
    {
        double scale = rasterizationScale > 0 ? rasterizationScale : 1.0;
        int titleBarHeight = (int)Math.Round(titleBarHeightDips * scale);
        int railWidthPixels = (int)Math.Round(railWidthDips * scale);

        int startX = Math.Max(leftInsetPixels, railWidthPixels);
        int dragWidth = totalWidthPixels - startX - rightInsetPixels;

        if (dragWidth <= 0 || titleBarHeight <= 0)
            return new global::Windows.Graphics.RectInt32(0, 0, 0, 0);

        return new global::Windows.Graphics.RectInt32(startX, 0, dragWidth, titleBarHeight);
    }

    /// <summary>
    /// Получить текущую ширину левой панели (Navigation Rail) в DIP.
    /// </summary>
    public static int GetCurrentRailWidthDips()
    {
        if (Shell.Current is { } shell)
        {
            if (shell.FlyoutBehavior == FlyoutBehavior.Disabled ||
                shell.CurrentPage is Views.RuleEditorPage)
            {
                return 0;
            }

            try
            {
                return Preferences.Default.Get("rail_collapsed", false) ? 64 : 220;
            }
            catch
            {
                return 220;
            }
        }
        return 220;
    }

    /// <summary>
    /// Зарегистрировать перехватчик WM_GETMINMAXINFO и задать минимальный размер окна.
    /// </summary>
    public static void EnforceMinSize(Microsoft.UI.Xaml.Window window)
    {
        var hwnd = WindowNative.GetWindowHandle(window);
        if (hwnd == IntPtr.Zero)
            return;

        uint dpi = GetDpiForWindow(hwnd);
        UpdateCachedDpi(dpi);

        SetWindowSubclass(hwnd, s_subclassProc, SubclassId, IntPtr.Zero);

        if (window.AppWindow is { } appWindow)
        {
            var size = appWindow.Size;
            int targetWidth = Math.Max(size.Width, s_cachedMinWidthPixels);
            int targetHeight = Math.Max(size.Height, s_cachedMinHeightPixels);

            if (targetWidth != size.Width || targetHeight != size.Height)
            {
                appWindow.Resize(new global::Windows.Graphics.SizeInt32(targetWidth, targetHeight));
            }
        }
    }

    /// <summary>
    /// Настроить нативную область перетаскивания (Drag Region) окна Windows через AppWindow.TitleBar.SetDragRectangles.
    /// Предотвращает зависания UI-потока при drag окна за счет прямого делегирования hit-testing подсистеме Win32/DWM.
    /// Содержит строгую валидацию геометрии и обработку исключений для защиты от краша при инициализации и закрытии окна.
    /// </summary>
    public static void ConfigureDragRegion(Microsoft.UI.Xaml.Window window)
    {
        try
        {
            s_activeWindowRef = new WeakReference<Microsoft.UI.Xaml.Window>(window);

            if (window.AppWindow is not { } appWindow)
                return;

            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                return;

            // Первичная установка
            UpdateDragRegion(window);

            // Обновление при готовности XAML-дерева / активации окна
            window.Activated += (_, _) => UpdateDragRegion(window);

            // Обновление ТОЛЬКО при реальном изменении размера окна, исключая вызовы во время движения
            appWindow.Changed += (_, args) =>
            {
                try
                {
                    if (args.DidSizeChange)
                    {
                        UpdateDragRegion(window);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowSizing] appWindow.Changed safely handled: {ex}");
                }
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSizing] ConfigureDragRegion failed safely: {ex}");
        }
    }

    /// <summary>
    /// Запрос на пересчет и обновление Drag Region активного окна.
    /// Вызывается при сворачивании/разворачивании Navigation Rail или переходе на страницы без панели.
    /// </summary>
    public static void UpdateDragRegion(Microsoft.UI.Xaml.Window? window = null)
    {
        try
        {
            Microsoft.UI.Xaml.Window? targetWindow = window;
            if (targetWindow == null && s_activeWindowRef != null && s_activeWindowRef.TryGetTarget(out var activeWin))
            {
                targetWindow = activeWin;
            }

            if (targetWindow?.AppWindow is not { } appWindow)
                return;

            var titleBar = appWindow.TitleBar;
            if (titleBar == null)
                return;

            var hwnd = WindowNative.GetWindowHandle(targetWindow);
            if (hwnd == IntPtr.Zero)
                return;

            double scale = 1.0;
            if (targetWindow.Content?.XamlRoot is { } xamlRoot && xamlRoot.RasterizationScale > 0)
            {
                scale = xamlRoot.RasterizationScale;
            }
            else
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi > 0)
                {
                    scale = dpi / 96.0;
                }
            }

            if (scale <= 0)
                scale = 1.0;

            int totalWidth = appWindow.Size.Width;
            int totalHeight = appWindow.Size.Height;

            // Если окно свернуто или еще не имеет физических размеров на экране
            if (totalWidth <= 0 || totalHeight <= 0)
                return;

            int railWidthDips = GetCurrentRailWidthDips();
            int leftInset = titleBar.LeftInset;
            int rightInset = titleBar.RightInset;

            var dragRect = CalculateDragRect(totalWidth, scale, railWidthDips, leftInset, rightInset, 48);

            // Если доступная ширина перетаскивания валидна
            if (dragRect.Width > 0 && dragRect.Height > 0)
            {
                titleBar.SetDragRectangles(new[] { dragRect });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WindowSizing] UpdateDragRegion safely handled: {ex}");
        }
    }

    /// <summary>
    /// Пересчитать кэшированные минимальные размеры окна в физических пикселях по DPI.
    /// Вызывается только при инициализации и при обработке WM_DPICHANGED.
    /// </summary>
    public static void UpdateCachedDpi(uint dpi)
    {
        double scale = (dpi > 0 ? dpi : 96) / 96.0;
        s_cachedMinWidthPixels = (int)(MinWidthDips * scale);
        s_cachedMinHeightPixels = (int)(MinHeightDips * scale);
    }

    private static IntPtr SubclassWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        switch (uMsg)
        {
            case WM_GETMINMAXINFO:
            {
                DefSubclassProc(hWnd, uMsg, wParam, lParam);

                if (lParam != IntPtr.Zero)
                {
                    int currentMinX = Marshal.ReadInt32(lParam, PtMinTrackSizeXOffset);
                    int currentMinY = Marshal.ReadInt32(lParam, PtMinTrackSizeYOffset);

                    if (currentMinX < s_cachedMinWidthPixels)
                        Marshal.WriteInt32(lParam, PtMinTrackSizeXOffset, s_cachedMinWidthPixels);

                    if (currentMinY < s_cachedMinHeightPixels)
                        Marshal.WriteInt32(lParam, PtMinTrackSizeYOffset, s_cachedMinHeightPixels);
                }
                return IntPtr.Zero;
            }

            case WM_DPICHANGED:
            {
                uint newDpi = (uint)(wParam.ToInt64() & 0xFFFF);
                if (newDpi == 0)
                    newDpi = GetDpiForWindow(hWnd);

                UpdateCachedDpi(newDpi);
                return DefSubclassProc(hWnd, uMsg, wParam, lParam);
            }

            case WM_NCDESTROY:
            {
                RemoveWindowSubclass(hWnd, s_subclassProc, uIdSubclass);
                return DefSubclassProc(hWnd, uMsg, wParam, lParam);
            }

            default:
                return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
    }
}
