namespace GosTek.Views;

/// <summary>
/// Переключатель тёмной/светлой темы. Сам следит за ThemeService,
/// поэтому страницам не нужно ничего синхронизировать в OnAppearing.
/// </summary>
public partial class ThemeToggle : ContentView
{
    public ThemeToggle()
    {
        InitializeComponent();

        Sync(animated: false);
        Loaded += (_, _) => Sync(animated: false);   // страница могла быть скрыта, пока тему меняли
        ThemeService.Changed += () => Sync(animated: true);
    }

    private void OnTapped(object? sender, TappedEventArgs e) => ThemeService.Toggle();

    private void Sync(bool animated)
    {
        bool dark = ThemeService.IsDark;
        ThemeIcon.Text = dark ? "🌙" : "☀️";

        double target = dark ? 0 : 18;

        if (animated && IsLoaded)
            _ = ToggleThumb.TranslateToAsync(target, 0, 150, Easing.CubicInOut);
        else
            ToggleThumb.TranslationX = target;
    }
}
