using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace Mesh.Mobile
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // Push the layout up when the soft keyboard appears so the input bar stays visible
            Window!.SetSoftInputMode(SoftInput.AdjustResize);
        }
    }
}
