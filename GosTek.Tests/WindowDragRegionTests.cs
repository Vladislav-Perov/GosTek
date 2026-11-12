/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для расчета геометрии Drag Region на Windows (Этап 6)
 * СОДЕРЖИМОЕ: WindowDragRegionTests
 * ЗАВИСИМОСТИ: GosTek.Platforms.Windows.WindowSizing, Xunit
 * ==============================================================================
 */

using GosTek.Platforms.Windows;
using Xunit;

namespace GosTek.Tests;

public class WindowDragRegionTests
{
    [Fact]
    public void CalculateDragRect_StandardDpi_ExpandedRail()
    {
        // 100% DPI (scale 1.0), окно 1200px, развернутый rail 220px, кнопки окна 140px
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 1200,
            rasterizationScale: 1.0,
            railWidthDips: 220,
            leftInsetPixels: 0,
            rightInsetPixels: 140,
            titleBarHeightDips: 48);

        Assert.Equal(220, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(840, rect.Width);
        Assert.Equal(48, rect.Height);
    }

    [Fact]
    public void CalculateDragRect_StandardDpi_CollapsedRail()
    {
        // 100% DPI (scale 1.0), окно 1200px, свернутый rail 64px, кнопки окна 140px
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 1200,
            rasterizationScale: 1.0,
            railWidthDips: 64,
            leftInsetPixels: 0,
            rightInsetPixels: 140,
            titleBarHeightDips: 48);

        Assert.Equal(64, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(996, rect.Width);
        Assert.Equal(48, rect.Height);
    }

    [Fact]
    public void CalculateDragRect_StandardDpi_DisabledRail_StartsAtZero()
    {
        // Полноэкранный режим без rail (FieldsPage / RuleEditorPage): railWidthDips = 0
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 1000,
            rasterizationScale: 1.0,
            railWidthDips: 0,
            leftInsetPixels: 0,
            rightInsetPixels: 140,
            titleBarHeightDips: 48);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(860, rect.Width);
        Assert.Equal(48, rect.Height);
    }

    [Fact]
    public void CalculateDragRect_HighDpi150_ScalesCorrectly()
    {
        // 150% DPI (scale 1.5), окно 1920px, развернутый rail 220 DIP (= 330 px), кнопки окна 210 px
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 1920,
            rasterizationScale: 1.5,
            railWidthDips: 220,
            leftInsetPixels: 0,
            rightInsetPixels: 210,
            titleBarHeightDips: 48);

        Assert.Equal(330, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(1380, rect.Width); // 1920 - 330 - 210 = 1380
        Assert.Equal(72, rect.Height);  // 48 * 1.5 = 72
    }

    [Fact]
    public void CalculateDragRect_UltraDpi200_CollapsedRail()
    {
        // 200% DPI (scale 2.0), окно 2560px, свернутый rail 64 DIP (= 128 px), кнопки окна 280 px
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 2560,
            rasterizationScale: 2.0,
            railWidthDips: 64,
            leftInsetPixels: 0,
            rightInsetPixels: 280,
            titleBarHeightDips: 48);

        Assert.Equal(128, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(2152, rect.Width); // 2560 - 128 - 280 = 2152
        Assert.Equal(96, rect.Height);  // 48 * 2.0 = 96
    }

    [Fact]
    public void CalculateDragRect_NarrowOrMinimizedWindow_ReturnsEmptyRect()
    {
        // Окно свернуто или слишком узкое
        var rect = WindowSizing.CalculateDragRect(
            totalWidthPixels: 200,
            rasterizationScale: 1.0,
            railWidthDips: 220,
            leftInsetPixels: 0,
            rightInsetPixels: 140,
            titleBarHeightDips: 48);

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }
}
