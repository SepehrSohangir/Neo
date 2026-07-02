using System.Collections.ObjectModel;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class DashboardTile
{
    public required string Title { get; init; }
    public required string Icon { get; init; }
    public required Color BackgroundColor { get; init; }
    public required bool IsEnabled { get; init; }
    public string? Route { get; init; }
}

public sealed class DashboardViewModel : ViewModelBase
{
    private readonly AppSession session;
    private readonly AutoSyncService autoSyncService;
    private readonly OfflineSyncService syncService;

    public ObservableCollection<DashboardTile> Tiles { get; } = [];

    public string WelcomeText => $"سلام، {session.DisplayName}";

    public Command<DashboardTile> OpenTileCommand { get; }
    public Command LogoutCommand { get; }

    public DashboardViewModel(AppSession session, AutoSyncService autoSyncService, OfflineSyncService syncService)
    {
        this.session = session;
        this.autoSyncService = autoSyncService;
        this.syncService = syncService;

        Tiles.Add(new DashboardTile
        {
            Title = "فروش",
            Icon = "🛒",
            BackgroundColor = Color.FromArgb("#EEF2FF"),
            IsEnabled = true,
            Route = nameof(Pages.InvoiceListPage)
        });
        Tiles.Add(new DashboardTile
        {
            Title = "رویدادها",
            Icon = "📡",
            BackgroundColor = Color.FromArgb("#ECFDF5"),
            IsEnabled = true,
            Route = nameof(Pages.ConsumedEventsPage)
        });
        Tiles.Add(new DashboardTile { Title = "خرید", Icon = "📦", BackgroundColor = Color.FromArgb("#F3F4F6"), IsEnabled = false });
        Tiles.Add(new DashboardTile { Title = "انبار", Icon = "🏪", BackgroundColor = Color.FromArgb("#F3F4F6"), IsEnabled = false });
        Tiles.Add(new DashboardTile { Title = "حسابداری", Icon = "📊", BackgroundColor = Color.FromArgb("#F3F4F6"), IsEnabled = false });
        Tiles.Add(new DashboardTile { Title = "تنظیمات", Icon = "⚙️", BackgroundColor = Color.FromArgb("#F3F4F6"), IsEnabled = false });

        OpenTileCommand = new Command<DashboardTile>(async tile => await OpenTileAsync(tile));
        LogoutCommand = new Command(async () => await LogoutAsync());
    }

    public Task RefreshAsync()
    {
        autoSyncService.Trigger();
        return Task.CompletedTask;
    }

    private async Task OpenTileAsync(DashboardTile tile)
    {
        if (!tile.IsEnabled || string.IsNullOrWhiteSpace(tile.Route))
        {
            return;
        }

        await Shell.Current.GoToAsync(tile.Route);
    }

    private async Task LogoutAsync()
    {
        autoSyncService.Stop();
        await syncService.LogoutAsync();
        await Shell.Current.GoToAsync("//LoginPage");
    }
}
