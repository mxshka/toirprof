using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace ToirProf.Mobile.Droid;

[global::Android.App.Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    protected MainApplication(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
        // Возможности телефона подставляются до того, как Avalonia создаст оболочку приложения.
        App.Platform = new AndroidPlatform();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
