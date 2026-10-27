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
        App.SetInitialActivation(initial);
        current.Activated += App.OnAppInstanceActivated;
        var main = AppInstance.FindOrRegisterForKey("liubai-mfa-main");
        if (!main.IsCurrent)
        {
            current.Activated -= App.OnAppInstanceActivated;
            await main.RedirectActivationToAsync(initial);
            return;
        }

        Application.Start(callbackParams =>
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            _ = new App();
        });
    }
}
