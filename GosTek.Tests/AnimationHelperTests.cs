/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для утилиты анимаций AnimationHelper
 * СОДЕРЖИМОЕ: AnimationHelperTests
 * ЗАВИСИМОСТИ: xUnit, Microsoft.Maui.Controls, GosTek.Helpers.AnimationHelper
 * ==============================================================================
 */
using GosTek.Helpers;
using Microsoft.Maui.Controls;
using Xunit;

namespace GosTek.Tests;

public class AnimationHelperTests
{
    [Fact]
    public async Task AnimateStaggeredEntranceAsync_NullCollection_DoesNotThrow()
    {
        await AnimationHelper.AnimateStaggeredEntranceAsync(null);
    }

    [Fact]
    public async Task AnimateStaggeredEntranceAsync_EmptyCollection_DoesNotThrow()
    {
        await AnimationHelper.AnimateStaggeredEntranceAsync(Array.Empty<VisualElement>());
    }

    [Fact]
    public async Task AnimateStaggeredEntranceAsync_CollectionWithNulls_DoesNotThrow()
    {
        await AnimationHelper.AnimateStaggeredEntranceAsync(new VisualElement?[] { null, null });
    }

    [Fact]
    public async Task AnimatePulseAsync_NullElement_DoesNotThrow()
    {
        await AnimationHelper.AnimatePulseAsync(null);
    }

    [Fact]
    public void StopPulse_NullElement_DoesNotThrow()
    {
        AnimationHelper.StopPulse(null);
    }
}
