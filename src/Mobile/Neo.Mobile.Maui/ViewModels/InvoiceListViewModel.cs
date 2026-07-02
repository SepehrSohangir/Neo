using System.Collections.ObjectModel;
using Neo.Contracts;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class InvoiceListItemViewModel
{
    public Guid InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string PersianStatus { get; init; } = string.Empty;
    public string DisplayUpdatedAt { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public string SyncTagText { get; init; } = string.Empty;
    public Color SyncTagBackground { get; init; } = Colors.Transparent;
    public Color SyncTagForeground { get; init; } = Colors.Black;

    public string TotalDisplay => $"{TotalAmount:N0} ریال";
}

public sealed class InvoiceListViewModel : ViewModelBase
{
    private readonly OfflineSyncService syncService;
    private readonly AutoSyncService autoSyncService;
    private bool isRefreshing;

    public ObservableCollection<InvoiceListItemViewModel> Invoices { get; } = [];

    public bool IsRefreshing
    {
        get => isRefreshing;
        set => SetProperty(ref isRefreshing, value);
    }

    public Command RefreshCommand { get; }
    public Command CreateCommand { get; }
    public Command<InvoiceListItemViewModel> OpenCommand { get; }
    public Command BackCommand { get; }

    public InvoiceListViewModel(OfflineSyncService syncService, AutoSyncService autoSyncService)
    {
        this.syncService = syncService;
        this.autoSyncService = autoSyncService;

        RefreshCommand = new Command(async () => await RefreshAsync());
        CreateCommand = new Command(async () => await Shell.Current.GoToAsync(nameof(Pages.InvoiceEditorPage)));
        OpenCommand = new Command<InvoiceListItemViewModel>(async item =>
            await Shell.Current.GoToAsync($"{nameof(Pages.InvoiceEditorPage)}?invoiceId={item.InvoiceId}"));
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
            var invoices = await syncService.GetInvoicesAsync();
            Invoices.Clear();

            foreach (var invoice in invoices)
            {
                var items = await syncService.GetInvoiceItemsAsync(invoice.InvoiceId);
                var syncTag = CreateSyncTag((SyncState)invoice.SyncState);

                Invoices.Add(new InvoiceListItemViewModel
                {
                    InvoiceId = invoice.InvoiceId,
                    InvoiceNumber = invoice.InvoiceNumber,
                    PersianStatus = invoice.PersianStatus,
                    DisplayUpdatedAt = invoice.DisplayUpdatedAt,
                    TotalAmount = items.Sum(x => x.LineTotal),
                    SyncTagText = syncTag.Text,
                    SyncTagBackground = syncTag.Background,
                    SyncTagForeground = syncTag.Foreground
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private static (string Text, Color Background, Color Foreground) CreateSyncTag(SyncState state) => state switch
    {
        SyncState.Synced => ("همگام شده", Color.FromArgb("#D1FAE5"), Color.FromArgb("#047857")),
        SyncState.Pending => ("همگام نشده", Color.FromArgb("#FEF3C7"), Color.FromArgb("#B45309")),
        SyncState.Failed => ("خطا در همگام‌سازی", Color.FromArgb("#FEE2E2"), Color.FromArgb("#B91C1C")),
        SyncState.Review => ("نیاز به بررسی", Color.FromArgb("#FFEDD5"), Color.FromArgb("#C2410C")),
        _ => ("نامشخص", Color.FromArgb("#F3F4F6"), Color.FromArgb("#6B7280"))
    };
}
