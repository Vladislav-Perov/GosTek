using Android.App;
using Android.Runtime;

// Приложение работает без сети. Доступ в интернет нужен только отладчику и Hot Reload,
// поэтому в Release-сборке этого разрешения нет.
#if DEBUG
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]
#endif

namespace GosTek
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
