using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Graphics;

namespace GosTek;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Убираем серые плашки: красим верхний статус-бар и нижнюю навигационную панель 
        // в темно-синий цвет интерфейса приложения (#0C1017)
        // SetStatusBarColor и SetNavigationBarColor помечены устаревшими с Android 15, но на 10-14 они нужны
#pragma warning disable CA1422
        if (Window != null) {
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#0C1017"));
            Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#0C1017"));
        }
#pragma warning restore CA1422
    }
}