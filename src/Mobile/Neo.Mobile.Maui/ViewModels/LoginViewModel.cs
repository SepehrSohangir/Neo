using Neo.Mobile.Maui.Configuration;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private readonly OfflineSyncService syncService;
    private readonly AutoSyncService autoSyncService;
    private readonly AppSettings settings;

    private string username = "demo";
    public string Username
    {
        get => username;
        set => SetProperty(ref username, value);
    }

    private string password = "demo";
    public string Password
    {
        get => password;
        set => SetProperty(ref password, value);
    }

    public Command LoginCommand { get; }

    public LoginViewModel(OfflineSyncService syncService, AutoSyncService autoSyncService, AppSettings settings)
    {
        this.syncService = syncService;
        this.autoSyncService = autoSyncService;
        this.settings = settings;
        LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);
    }

    public async Task InitializeAsync()
    {
        await syncService.InitializeAsync();
    }

    private async Task LoginAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            LoginCommand.ChangeCanExecute();
            StatusMessage = string.Empty;

            await syncService.LoginAsync(Username, Password, settings.ServerBaseUrl);
            autoSyncService.Start();

            await Shell.Current.GoToAsync(nameof(Pages.DashboardPage));

            _ = BootstrapInBackgroundAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            LoginCommand.ChangeCanExecute();
        }
    }

    private async Task BootstrapInBackgroundAsync()
    {
        try
        {
            await syncService.EnsureSnapshotAsync();
            autoSyncService.Trigger();
        }
        catch
        {
            // Initial snapshot/sync runs silently after login.
        }
    }
}
