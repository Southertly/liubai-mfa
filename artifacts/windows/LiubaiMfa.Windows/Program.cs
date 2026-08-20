using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace LiubaiMfa.Windows;

internal static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var current = AppInstance.GetCurrent();
        var initial = current.GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey("liubai-mfa-main");
        if (!main.IsCurrent)
        {
            await main.RedirectActivationToAsync(initial);
            return;
        }

        App.SetInitialActivation(initial);
        main.Activated += App.OnAppInstanceActivated;
        Application.Start(callbackParams =>
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            _ = new App();
        });
    }
}
