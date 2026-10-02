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
            return new Window(new AppShell());
        }
    }
}