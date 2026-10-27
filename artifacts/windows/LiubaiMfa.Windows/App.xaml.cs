using LiubaiMfa.Windows.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using IProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace LiubaiMfa.Windows;

public partial class App : Application
{
    private static readonly ActivationDispatchQueue<AppActivationArguments> PendingActivations = new();
    private static App? _current;

    private readonly DispatcherQueue _dispatcherQueue;
    private MainWindow? _window;
    private int _windowReady;

    public App()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Debug.WriteLine($"Unhandled application error: {args.Exception.GetType().Name}");
        };

        Volatile.Write(ref _current, this);
    }

    internal static void SetInitialActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        _ = PendingActivations.TryEnqueue(activation);
    }

    internal static DispatcherQueue? CurrentDispatcherQueue => Volatile.Read(ref _current)?._dispatcherQueue;

    internal static void OnAppInstanceActivated(object? sender, AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        if (!PendingActivations.TryEnqueue(activation))
        {
            return;
        }

        Volatile.Read(ref _current)?.RequestActivationDrain();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => StopActivationDispatch();
        }

        _window.Activate();
        Volatile.Write(ref _windowReady, 1);
        RequestActivationDrain();
    }

    private void RequestActivationDrain()
    {
        if (Volatile.Read(ref _windowReady) == 0 || !PendingActivations.TryReserveDrain())
        {
            return;
        }

        ScheduleReservedDrain();
    }

    private void ScheduleReservedDrain()
    {
        if (_dispatcherQueue.TryEnqueue(DispatchActivationDrain))
        {
            return;
        }

        PendingActivations.Stop();
    }

    private void DispatchActivationDrain()
    {
        if (!PendingActivations.TryBeginDrain())
        {
            return;
        }

        ObserveActivationDrain(DrainActivationsAsync());
    }

    private async Task DrainActivationsAsync()
    {
        try
        {
            while (PendingActivations.TryTake(out var activation))
            {
                try
                {
                    await HandleActivationAsync(activation);
                }
                catch (Exception exception)
                {
                    Debug.WriteLine($"Activation routing failed: {exception.GetType().Name}");
                }
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Activation routing failed: {exception.GetType().Name}");
        }
        finally
        {
            if (PendingActivations.CompleteDrain())
            {
                ScheduleReservedDrain();
            }
        }
    }

    private async Task HandleActivationAsync(AppActivationArguments activation)
    {
        if (_window is null)
        {
            return;
        }

        _window.Activate();
        if (activation.Kind != ExtendedActivationKind.Protocol
            || activation.Data is not IProtocolActivatedEventArgs protocolArgs)
        {
            return;
        }

        await _window.HandleProtocolActivationAsync(protocolArgs.Uri);
    }

    private static void ObserveActivationDrain(Task drainTask)
    {
        if (drainTask.IsCompletedSuccessfully)
        {
            return;
        }

        _ = drainTask.ContinueWith(
            static completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    Debug.WriteLine($"Activation routing failed: {exception.GetType().Name}");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void StopActivationDispatch()
    {
        Volatile.Write(ref _windowReady, 0);
        PendingActivations.Stop();
        Interlocked.CompareExchange(ref _current, null, this);
    }
}
