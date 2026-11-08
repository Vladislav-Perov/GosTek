/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Управление компонентом наложения загрузки (LoadingOverlay), bindable свойства и отображение
 * СОДЕРЖИМОЕ: LoadingOverlay
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.Controls
 * ==============================================================================
 */
namespace GosTek.Views.Controls;

public partial class LoadingOverlay : ContentView
{
    public static readonly BindableProperty IsLoadingProperty =
        BindableProperty.Create(
            nameof(IsLoading),
            typeof(bool),
            typeof(LoadingOverlay),
            false,
            propertyChanged: (bindable, _, newValue) => {
                if (bindable is LoadingOverlay control) {
                    bool loading = (bool)newValue;
                    control.IsVisible = loading;
                    control.InputTransparent = !loading;
                    if (control.Spinner != null)
                        control.Spinner.IsRunning = loading;
                }
            });

    public static readonly BindableProperty StatusTextProperty =
        BindableProperty.Create(
            nameof(StatusText),
            typeof(string),
            typeof(LoadingOverlay),
            "Загрузка...",
            propertyChanged: (bindable, _, newValue) => {
                if (bindable is LoadingOverlay control && control.StatusLabel != null)
                    control.StatusLabel.Text = (string?)newValue ?? "Загрузка...";
            });

    public static readonly BindableProperty SubstatusTextProperty =
        BindableProperty.Create(
            nameof(SubstatusText),
            typeof(string),
            typeof(LoadingOverlay),
            string.Empty,
            propertyChanged: (bindable, _, newValue) => {
                if (bindable is LoadingOverlay control && control.SubstatusLabel != null) {
                    var text = (string?)newValue ?? string.Empty;
                    control.SubstatusLabel.Text = text;
                    control.SubstatusLabel.IsVisible = !string.IsNullOrWhiteSpace(text);
                }
            });

    public static readonly BindableProperty AnimationSourceProperty =
        BindableProperty.Create(
            nameof(AnimationSource),
            typeof(ImageSource),
            typeof(LoadingOverlay),
            null,
            propertyChanged: (bindable, _, newValue) => {
                if (bindable is LoadingOverlay control) {
                    var source = newValue as ImageSource;
                    if (control.AnimatedGifImage != null) {
                        control.AnimatedGifImage.Source = source;
                        control.AnimatedGifImage.IsVisible = source != null;
                    }
                    if (control.Spinner != null)
                        control.Spinner.IsVisible = source == null;
                }
            });

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string SubstatusText
    {
        get => (string)GetValue(SubstatusTextProperty);
        set => SetValue(SubstatusTextProperty, value);
    }

    public ImageSource? AnimationSource
    {
        get => (ImageSource?)GetValue(AnimationSourceProperty);
        set => SetValue(AnimationSourceProperty, value);
    }

    public LoadingOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Активирует оверлей загрузки с установкой статуса и подстатуса.
    /// </summary>
    public void Show(string status = "Загрузка...", string? substatus = null)
    {
        MainThread.BeginInvokeOnMainThread(() => {
            StatusText = status;
            SubstatusText = substatus ?? string.Empty;
            IsLoading = true;
        });
    }

    /// <summary>
    /// Деактивирует и скрывает оверлей загрузки.
    /// </summary>
    public void Hide()
    {
        MainThread.BeginInvokeOnMainThread(() => {
            IsLoading = false;
        });
    }

    /// <summary>
    /// Динамически обновляет отображаемый текст без скрытия оверлея.
    /// </summary>
    public void UpdateStatus(string status, string? substatus = null)
    {
        MainThread.BeginInvokeOnMainThread(() => {
            StatusText = status;
            SubstatusText = substatus ?? string.Empty;
        });
    }
}
