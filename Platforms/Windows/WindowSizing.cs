/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Фиксация минимальных габаритов окна 800x600 на Windows WinUI 3 через Win32 Subclassing (WM_GETMINMAXINFO)
 * СОДЕРЖИМОЕ: WindowSizing
 * ЗАВИСИМОСТИ: Microsoft.UI.Xaml, WinRT.Interop, System.Runtime.InteropServices
 * ==============================================================================
 */
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GosTek.Platforms.Windows;

/// <summary>
/// Платформенный помощник для установки аппаратных ограничений минимального размера окна WinUI 3.
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
            if (window.AppWindow is not { } appWindow)
                return;

            if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
                return;

            void UpdateDragRectangles()
            {
                try
                {
                    var titleBar = appWindow.TitleBar;
                    if (titleBar == null)
                        return;

                    var hwnd = WindowNative.GetWindowHandle(window);
                    if (hwnd == IntPtr.Zero)
                        return;

                    double scale = 1.0;
                    if (window.Content?.XamlRoot is { } xamlRoot && xamlRoot.RasterizationScale > 0)
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

                    int titleBarHeight = (int)Math.Round(48 * scale);
                    int totalWidth = appWindow.Size.Width;
                    int totalHeight = appWindow.Size.Height;

                    // Если окно свернуто или еще не имеет физических размеров на экране
                    if (totalWidth <= 0 || totalHeight <= 0)
                        return;

                    int leftInset = titleBar.LeftInset;
                    int rightInset = titleBar.RightInset;
                    int dragWidth = totalWidth - leftInset - rightInset;

                    // Жесткая проверка: предотвращаем создание RectInt32 с отрицательными или нулевыми габаритами
                    if (dragWidth <= 0 || titleBarHeight <= 0)
                        return;

                    var dragRect = new global::Windows.Graphics.RectInt32(leftInset, 0, dragWidth, titleBarHeight);
                    titleBar.SetDragRectangles(new[] { dragRect });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WindowSizing] SetDragRectangles safely skipped: {ex}");
                }
            }

            // Безопасная первичная установка
            UpdateDragRectangles();

            // Обновление при готовности XAML-дерева / активации окна
            window.Activated += (_, _) => UpdateDragRectangles();

            // Обновление ТОЛЬКО при реальном изменении размера окна, исключая вызовы во время движения
            appWindow.Changed += (_, args) =>
            {
                try
                {
                    if (args.DidSizeChange)
                    {
                        UpdateDragRectangles();
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
