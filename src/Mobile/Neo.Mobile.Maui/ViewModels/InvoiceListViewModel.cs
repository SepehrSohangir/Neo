using System.Collections.ObjectModel;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class InvoiceListItemViewModel
{
    public Guid InvoiceId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string PersianStatus { get; init; } = string.Empty;
    public string DisplayUpdatedAt { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }

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
                Invoices.Add(new InvoiceListItemViewModel
                {
                    InvoiceId = invoice.InvoiceId,
                    InvoiceNumber = invoice.InvoiceNumber,
                    PersianStatus = invoice.PersianStatus,
                    DisplayUpdatedAt = invoice.DisplayUpdatedAt,
                    TotalAmount = items.Sum(x => x.LineTotal)
                });
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
