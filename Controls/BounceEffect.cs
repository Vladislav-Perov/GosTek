/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Анимационный помощник и прикрепляемое поведение упругого отклика (Bounce Effect)
 * СОДЕРЖИМОЕ: BounceEffect, BounceBehavior
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Services.HapticHelper
 * ==============================================================================
 */
using GosTek.Services;

namespace GosTek.Controls;

/// <summary>
/// Анимационный хелпер для создания тактильного эффекта сжатия (Bounce) при нажатии на карточки и кнопки.
/// </summary>
public static class BounceEffect
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(BounceEffect), false,
        propertyChanged: OnIsEnabledChanged);

    public static bool GetIsEnabled(BindableObject view) => (bool)view.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject view, bool value) => view.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        if ((bool)newValue)
        {
            if (!view.Behaviors.Any(b => b is BounceBehavior))
                view.Behaviors.Add(new BounceBehavior());
        }
        else
        {
            var existing = view.Behaviors.FirstOrDefault(b => b is BounceBehavior);
            if (existing is not null)
                view.Behaviors.Remove(existing);
        }
    }

    /// <summary>
    /// Проигрывает плавную упругую анимацию нажатия ScaleTo(0.96) -> ScaleTo(1.0) с виброоткликом.
    /// </summary>
    public static async Task AnimateBounceAsync(this VisualElement element)
    {
        try
        {
            HapticHelper.Click();
            await element.ScaleToAsync(0.96, 60, Easing.CubicOut);
            await element.ScaleToAsync(1.0, 70, Easing.CubicIn);
        }
        catch
        {
            element.Scale = 1.0;
        }
    }
}

/// <summary>
/// Поведение MAUI, автоматически добавляющее анимацию сжатия и тактильный отклик к элементу.
/// </summary>
public class BounceBehavior : Behavior<View>
{
    private View? _attachedView;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _attachedView = bindable;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        bindable.GestureRecognizers.Add(tap);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        base.OnDetachingFrom(bindable);
        _attachedView = null;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
    }
}
