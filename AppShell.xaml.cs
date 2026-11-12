/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Корневая Shell-навигация приложения, регистрация маршрутов и глобальная цепочка возврата по ESC / Назад
 * СОДЕРЖИМОЕ: AppShell
 * ЗАВИСИМОСТИ: Microsoft.Maui.Controls, GosTek.Views.FieldsPage, GosTek.Views.RuleEditorPage, GosTek.Views.Dialogs, GosTek.Views.MainPage
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

#if WINDOWS
            Loaded += (s, e) => HookGlobalEscape();
#endif
        }

        // Кнопка «Назад» на Android: диалоги, настройки, оверлеи или стандартный шаг назад
        protected override bool OnBackButtonPressed()
        {
            if (Views.Dialogs.TryDismiss())
                return true;

            if (Views.SettingsOverlay.IsOpen)
            {
                _ = Views.SettingsOverlay.CloseAsync();
                return true;
            }

            var currentPage = CurrentPage;
            if (currentPage is Views.FieldsPage fieldsPage && fieldsPage.TryDismissOverlays())
                return true;

            return base.OnBackButtonPressed();
        }

        /// <summary>
        /// Глобальная цепочка навигации при нажатии ESC на Windows:
        /// 1. Закрытие модальных окон, шторок настроек и оверлеев.
        /// 2. Возврат назад по стеку страниц (если NavigationStack.Count > 1).
        /// 3. Переход на Главную вкладку (MainPage), если открыта другая корневая вкладка.
        /// 4. Если пользователь уже на Главной и ничего не открыто — действие игнорируется.
        /// </summary>
        public static async Task<bool> HandleGlobalEscapeAsync()
        {
            // 1. ПРОВЕРКА МОДАЛОК / НАСТРОЕК / ОВЕРЛЕЕВ
            if (Views.Dialogs.TryDismiss())
                return true;

            if (Views.SettingsOverlay.IsOpen)
            {
                await Views.SettingsOverlay.CloseAsync();
                return true;
            }

            var currentPage = Shell.Current?.CurrentPage;

            if (currentPage is Views.FieldsPage fieldsPage && fieldsPage.TryDismissOverlays())
                return true;

            if (Shell.Current?.Navigation?.ModalStack?.Count > 0)
            {
                await Shell.Current.Navigation.PopModalAsync();
                return true;
            }

            // 2. ПРОВЕРКА ГЛУБИНЫ НАВИГАЦИИ (внутри страницы)
            var nav = Shell.Current?.Navigation;
            if (nav?.NavigationStack is { Count: > 1 })
            {
                await Shell.Current.GoToAsync("..");
                return true;
            }

            if (currentPage?.Navigation?.NavigationStack is { Count: > 1 })
            {
                await Shell.Current.GoToAsync("..");
                return true;
            }

            // 3. ПРОВЕРКА ВКЛАДКИ (переход на Главную с других корневых вкладок)
            if (currentPage is not null and not Views.MainPage)
            {
                await Shell.Current.GoToAsync("//MainPage");
                return true;
            }

            // 4. КОНЕЦ: на Главной странице без модалок ничего не делаем
            return false;
        }

#if WINDOWS
        private Microsoft.UI.Xaml.Input.KeyboardAccelerator? _globalEscape;
        private Microsoft.UI.Xaml.UIElement? _acceleratorRoot;

        /// <summary>Регистрация глобального перехвата клавиши Escape на окне Windows.</summary>
        public void HookGlobalEscape()
        {
            if (_globalEscape is not null)
                return;

            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window
                || window.Content is not Microsoft.UI.Xaml.UIElement root)
                return;

            _globalEscape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
            _globalEscape.Invoked += (s, args) =>
            {
                args.Handled = true;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await HandleGlobalEscapeAsync();
                });
            };

            root.KeyboardAccelerators.Add(_globalEscape);
            _acceleratorRoot = root;
        }
#endif
    }
}
