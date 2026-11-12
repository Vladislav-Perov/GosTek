/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Корневой класс приложения .NET MAUI, управление жизненным циклом и окном Windows
 * СОДЕРЖИМОЕ: App
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.ThemeService, GosTek.Services.TemplateSeeder
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
            _ = GosTek.Services.TemplateSeeder.EnsureDefaultTemplatesAsync();
            _ = Task.Run(() => FileSaveService.ClearTemp());
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
#if WINDOWS
            // Минимальные десктопные пропорции окна: 800x600
            window.MinimumWidth = 800;
            window.MinimumHeight = 600;
            window.Created += (_, _) =>
            {
                ThemeService.ApplyPlatformTheme(window);
                (window.Page as AppShell)?.HookGlobalEscape();
            };
#endif
            return window;
        }
    }
}
