using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Handlers;

namespace GosTek.Views.Controls;

internal static class InputChrome
{
    private static readonly ConditionalWeakTable<VisualElement, FocusOutline> Outlines = new();

    public static void Configure()
    {
        EntryHandler.Mapper.AppendToMapping(nameof(InputChrome), (handler, view) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
            TrackFocus(view);
        });

        EditorHandler.Mapper.AppendToMapping(nameof(InputChrome), (handler, view) =>
        {
#if ANDROID
            handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
            TrackFocus(view);
        });
    }

    private static void TrackFocus(IView view)
    {
        if (view is VisualElement input)
            Outlines.GetValue(input, static element => new FocusOutline(element));
    }

    private sealed class FocusOutline
    {
        private Border? _border;
        private double _previousThickness;

        public FocusOutline(VisualElement input)
        {
            input.Focused += OnFocused;
            input.Unfocused += RemoveOutline;
            input.Unloaded += RemoveOutline;
        }

        private void OnFocused(object? sender, FocusEventArgs e)
        {
            if (_border is not null || sender is not VisualElement input)
                return;

            for (var parent = input.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is Border { StrokeShape: RoundRectangle, StrokeThickness: > 0 } border)
                {
                    _border = border;
                    _previousThickness = border.StrokeThickness;
                    border.StrokeThickness = 2;
                    break;
                }
            }
        }

        private void RemoveOutline(object? sender, EventArgs e)
        {
            if (_border is { StrokeThickness: 2 } border)
                border.StrokeThickness = _previousThickness;

            _border = null;
        }
    }
}
