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
    }
}