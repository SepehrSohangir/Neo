namespace Neo.Mobile.Maui.Services;

public sealed class AutoSyncService(OfflineSyncService syncService, AppSession session)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Timer? timer;
    private bool started;

    /// <summary>Fired after a successful background sync so lists can refresh from local SQLite only.</summary>
    public event EventHandler? DataChanged;

    public void Start()
    {
        if (started)
        {
            return;
        }

        started = true;
        Connectivity.ConnectivityChanged += OnConnectivityChanged;
        timer = new Timer(_ => Trigger(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45));
        Trigger();
    }

    public void Stop()
    {
        started = false;
        Connectivity.ConnectivityChanged -= OnConnectivityChanged;
        timer?.Dispose();
        timer = null;
    }

    public void Trigger() => _ = RunSyncAsync();

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess == NetworkAccess.Internet)
        {
            Trigger();
        }
    }

    private async Task RunSyncAsync()
    {
        if (!session.IsLoggedIn || Connectivity.NetworkAccess != NetworkAccess.Internet)
        {
            return;
        }

        if (!await gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await syncService.SyncSilentlyAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Background sync failures are intentionally silent.
        }
        finally
        {
            gate.Release();
        }
    }
}
