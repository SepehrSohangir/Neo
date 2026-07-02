using System.Collections.ObjectModel;
using Neo.Mobile.Maui.Services;

namespace Neo.Mobile.Maui.ViewModels;

public sealed class InvoiceLineViewModel : ViewModelBase
{
    private Guid productId;
    public Guid ProductId
    {
        get => productId;
        set => SetProperty(ref productId, value);
    }

    private string productName = string.Empty;
    public string ProductName
    {
        get => productName;
        set => SetProperty(ref productName, value);
    }

    private decimal quantity = 1;
    public decimal Quantity
    {
        get => quantity;
        set
        {
            if (SetProperty(ref quantity, value))
            {
                RaisePropertyChanged(nameof(LineTotal));
                RaisePropertyChanged(nameof(LineTotalDisplay));
            }
        }
    }

    private decimal unitPrice;
    public decimal UnitPrice
    {
        get => unitPrice;
        set
        {
            if (SetProperty(ref unitPrice, value))
            {
                RaisePropertyChanged(nameof(LineTotal));
                RaisePropertyChanged(nameof(LineTotalDisplay));
            }
        }
    }

    public decimal LineTotal => Quantity * UnitPrice;
    public string LineTotalDisplay => $"{LineTotal:N0} ریال";

    public Guid InvoiceItemId { get; set; } = Guid.NewGuid();
}

[QueryProperty(nameof(InvoiceIdText), "invoiceId")]
public sealed class InvoiceEditorViewModel : ViewModelBase
{
    private readonly OfflineSyncService syncService;

    private Guid? invoiceId;
    private long serverVersion;

    public ObservableCollection<LocalProductPrice> Products { get; } = [];
    public ObservableCollection<InvoiceLineViewModel> Lines { get; } = [];

    private LocalProductPrice? selectedProduct;
    public LocalProductPrice? SelectedProduct
    {
        get => selectedProduct;
        set
        {
            if (SetProperty(ref selectedProduct, value))
            {
                AddLineCommand.ChangeCanExecute();
            }
        }
    }

    private string invoiceNumber = string.Empty;
    public string InvoiceNumber
    {
        get => invoiceNumber;
        set => SetProperty(ref invoiceNumber, value);
    }

    public string TotalAmountDisplay => $"{Lines.Sum(x => x.LineTotal):N0} ریال";

    public Command AddLineCommand { get; }
    public Command<InvoiceLineViewModel> RemoveLineCommand { get; }
    public Command SaveCommand { get; }
    public Command BackCommand { get; }

    public string? InvoiceIdText
    {
        set
        {
            if (Guid.TryParse(value, out var id))
            {
                invoiceId = id;
            }
        }
    }

    public InvoiceEditorViewModel(OfflineSyncService syncService)
    {
        this.syncService = syncService;

        AddLineCommand = new Command(AddLine, () => SelectedProduct is not null);
        RemoveLineCommand = new Command<InvoiceLineViewModel>(line =>
        {
            Lines.Remove(line);
            RaisePropertyChanged(nameof(TotalAmountDisplay));
            SaveCommand.ChangeCanExecute();
        });
        SaveCommand = new Command(async () => await SaveAsync(), () => Lines.Count > 0);
        BackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));

        Lines.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(TotalAmountDisplay));
            SaveCommand.ChangeCanExecute();
        };
    }

    public async Task InitializeAsync()
    {
        Products.Clear();
        foreach (var product in await syncService.GetProductsAsync())
        {
            Products.Add(product);
        }

        if (invoiceId.HasValue)
        {
            var invoice = await syncService.GetInvoiceAsync(invoiceId.Value);
            if (invoice is null)
            {
                StatusMessage = "فاکتور یافت نشد.";
                return;
            }

            InvoiceNumber = invoice.InvoiceNumber;
            serverVersion = invoice.ServerVersion;

            var items = await syncService.GetInvoiceItemsAsync(invoiceId.Value);
            Lines.Clear();
            foreach (var item in items)
            {
                Lines.Add(new InvoiceLineViewModel
                {
                    InvoiceItemId = item.InvoiceItemId,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice
                });
            }
        }
        else
        {
            InvoiceNumber = $"SO-{DateTime.Now:yyyyMMddHHmmss}";
        }

        RaisePropertyChanged(nameof(TotalAmountDisplay));
        StatusMessage = "اقلام کالا را اضافه کنید.";
    }

    private void AddLine()
    {
        if (SelectedProduct is null)
        {
            return;
        }

        var product = SelectedProduct;
        Lines.Add(new InvoiceLineViewModel
        {
            ProductId = product.ProductId,
            ProductName = product.Name,
            Quantity = 1,
            UnitPrice = product.UnitPrice
        });

        RaisePropertyChanged(nameof(TotalAmountDisplay));
        SaveCommand.ChangeCanExecute();
    }

    private async Task SaveAsync()
    {
        try
        {
            var items = Lines.Select(x => new LocalInvoiceItem
            {
                InvoiceItemId = x.InvoiceItemId,
                ProductId = x.ProductId,
                ProductName = x.ProductName,
                Quantity = x.Quantity,
                UnitPrice = x.UnitPrice,
                LineTotal = x.LineTotal
            }).ToList();

            await syncService.SaveSalesInvoiceAsync(
                items,
                InvoiceNumber,
                invoiceId,
                serverVersion);

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }
}
