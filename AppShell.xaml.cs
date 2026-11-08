/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Корневая Shell-навигация приложения, регистрация маршрутов и обработка системного перехода назад
 * СОДЕРЖИМОЕ: AppShell
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.FieldsPage, GosTek.Views.RuleEditorPage
 * ==============================================================================
 */
namespace GosTek
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Окно без вкладки в таббаре, открывается поверх
            Routing.RegisterRoute("FieldsPage", typeof(Views.FieldsPage));
            Routing.RegisterRoute("RuleEditorPage", typeof(Views.RuleEditorPage));
        }

        // Кнопка «Назад» на Android сначала закрывает открытое окно подтверждения
        protected override bool OnBackButtonPressed()
            => Views.Dialogs.TryDismiss() || base.OnBackButtonPressed();
    }
}
