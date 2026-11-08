/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Корневой класс приложения .NET MAUI, управление жизненным циклом и окном Windows
 * СОДЕРЖИМОЕ: App
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.ThemeService
 * ==============================================================================
 */
namespace GosTek
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            ThemeService.Init();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
#if WINDOWS
            // Меньше этого окно уменьшить нельзя: иначе элементы шапки наезжают друг на друга
            window.MinimumWidth = 500;
            window.MinimumHeight = 700;
#endif
            return window;
        }
    }
}