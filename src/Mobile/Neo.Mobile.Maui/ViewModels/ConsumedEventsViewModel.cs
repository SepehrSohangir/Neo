using System.Collections.ObjectModel;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class ConsumedEventItemViewModel
{
    public Guid EventId { get; init; }
    public string EventTypeLabel { get; init; } = string.Empty;
    public string ShortEventId { get; init; } = string.Empty;
    public long ServerSequence { get; init; }
    public string AppliedAtDisplay { get; init; } = string.Empty;

    public string SequenceDisplay => $"ترتیب سرور: {ServerSequence:N0}";
}

public sealed class ConsumedEventsViewModel : ViewModelBase
{
    private readonly OfflineSyncService syncService;
    private readonly AutoSyncService autoSyncService;
    private bool isRefreshing;

    public ObservableCollection<ConsumedEventItemViewModel> Events { get; } = [];

    public bool IsRefreshing
    {
        get => isRefreshing;
        set => SetProperty(ref isRefreshing, value);
    }

    public Command RefreshCommand { get; }
    public Command BackCommand { get; }

    public ConsumedEventsViewModel(OfflineSyncService syncService, AutoSyncService autoSyncService)
    {
        this.syncService = syncService;
        this.autoSyncService = autoSyncService;

        RefreshCommand = new Command(async () => await RefreshAsync());
        BackCommand = new Command(async () => await Shell.Current.GoToAsync(nameof(Pages.DashboardPage)));
    }

    public async Task LoadAsync()
    {
        await LoadFromLocalAsync();
        autoSyncService.Trigger();
    }

    public async Task RefreshAsync()
    {
        try
        {
            IsRefreshing = true;
            await LoadFromLocalAsync();
            autoSyncService.Trigger();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public async Task ReloadFromLocalAsync() => await LoadFromLocalAsync();

    private async Task LoadFromLocalAsync()
    {
        try
        {
            var events = await syncService.GetConsumedEventsAsync();
            Events.Clear();

            foreach (var item in events)
            {
                Events.Add(new ConsumedEventItemViewModel
                {
                    EventId = item.EventId,
                    EventTypeLabel = item.PersianEventType,
                    ShortEventId = item.ShortEventId,
                    ServerSequence = item.ServerSequence,
                    AppliedAtDisplay = item.DisplayAppliedAt
                });
            }

            StatusMessage = events.Count == 0
                ? "هنوز رویدادی از سرور دریافت نشده است."
                : $"{events.Count} رویداد مصرف‌شده";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
