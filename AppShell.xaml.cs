namespace GosTek
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Окно без вкладки в таббаре, открывается поверх: FieldsPage?id=ip
            Routing.RegisterRoute("FieldsPage", typeof(Views.FieldsPage));
        }

        // Кнопка «Назад» на Android сначала закрывает открытое окно подтверждения
        protected override bool OnBackButtonPressed()
            => Views.Dialogs.TryDismiss() || base.OnBackButtonPressed();
    }
}