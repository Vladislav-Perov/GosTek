/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Управление компонентом скелетон-загрузки (LoadingSkeleton) и циклом мерцания (Shimmer)
 * СОДЕРЖИМОЕ: LoadingSkeleton
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.Controls
 * ==============================================================================
 */
namespace GosTek.Views.Controls;

public partial class LoadingSkeleton : ContentView
{
    private CancellationTokenSource? _shimmerCts;

    public static readonly BindableProperty IsLoadingProperty =
        BindableProperty.Create(
            nameof(IsLoading),
            typeof(bool),
            typeof(LoadingSkeleton),
            false,
            propertyChanged: (bindable, _, newValue) =>
            {
                if (bindable is LoadingSkeleton skeleton)
                {
                    if ((bool)newValue)
                        skeleton.StartShimmer();
                    else
                        skeleton.StopShimmer();
                }
            });

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public LoadingSkeleton()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Отображает скелетон и запускает плавный пульсирующий цикл мерцания (Shimmer).
    /// </summary>
    public void StartShimmer()
    {
        StopShimmer();
        IsVisible = true;
        Opacity = 1.0;

        _shimmerCts = new CancellationTokenSource();
        var ct = _shimmerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (ct.IsCancellationRequested)
                            return;
                        await this.FadeToAsync(0.4, 600, Easing.SinInOut);
                        if (ct.IsCancellationRequested)
                            return;
                        await this.FadeToAsync(1.0, 600, Easing.SinInOut);
                    });

                    await Task.Delay(50, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }, ct);
    }

    /// <summary>
    /// Останавливает мерцание, отменяет анимации и скрывает компонент.
    /// </summary>
    public void StopShimmer()
    {
        _shimmerCts?.Cancel();
        _shimmerCts?.Dispose();
        _shimmerCts = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            this.CancelAnimations();
            Opacity = 1.0;
            IsVisible = false;
        });
    }
}
