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

    // Программная отрисовка: в эмуляторах (BlueStacks, Android Studio без GPU)
    // аппаратный режим теряет текст — кнопки видны, надписей нет. Формы клиента
    // простые, на телефоне программная отрисовка работает без задержек.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .With(new AndroidPlatformOptions { RenderingMode = [AndroidRenderingMode.Software] });
}
