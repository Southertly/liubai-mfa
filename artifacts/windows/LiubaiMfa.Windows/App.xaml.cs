using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using IProtocolActivatedEventArgs = Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs;

namespace LiubaiMfa.Windows;

public partial class App : Application
{
    private static readonly object ActivationGate = new();
    private static readonly Queue<AppActivationArguments> PendingActivations = new();
    private static App? _current;

    private readonly DispatcherQueue _dispatcherQueue;
    private MainWindow? _window;
    private bool _isDrainScheduled;

    public App()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            Debug.WriteLine($"Unhandled application error: {args.Exception.GetType().Name}");
        };

        lock (ActivationGate)
        {
            _current = this;
        }
    }

    internal static void SetInitialActivation(AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        lock (ActivationGate)
        {
            PendingActivations.Enqueue(activation);
        }
    }

    internal static void OnAppInstanceActivated(object? sender, AppActivationArguments activation)
    {
        ArgumentNullException.ThrowIfNull(activation);
        App? app;
        lock (ActivationGate)
        {
            PendingActivations.Enqueue(activation);
            app = _current;
        }

        app?.ScheduleActivationDrain();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window ??= new MainWindow();
        _window.Activate();
        ScheduleActivationDrain();
    }

    private void ScheduleActivationDrain()
    {
        lock (ActivationGate)
        {
            if (_window is null || PendingActivations.Count == 0 || _isDrainScheduled)
            {
                return;
            }

            _isDrainScheduled = true;
        }

        if (_dispatcherQueue.TryEnqueue(() => ObserveActivationDrain(DrainActivationsAsync())))
        {
            return;
        }

        lock (ActivationGate)
        {
            _isDrainScheduled = false;
        }
    }

    private async Task DrainActivationsAsync()
    {
        var shouldReschedule = false;
        try
        {
            while (true)
            {
                AppActivationArguments activation;
                lock (ActivationGate)
                {
                    if (PendingActivations.Count == 0)
                    {
                        return;
                    }

                    activation = PendingActivations.Dequeue();
                }

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
            lock (ActivationGate)
            {
                _isDrainScheduled = false;
                shouldReschedule = _window is not null && PendingActivations.Count > 0;
            }

            if (shouldReschedule)
            {
                ScheduleActivationDrain();
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
}
