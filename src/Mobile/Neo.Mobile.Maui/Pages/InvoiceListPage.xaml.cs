using Neo.Mobile.Maui.Services;
using Neo.Mobile.Maui.ViewModels;

namespace Neo.Mobile.Maui.Pages;

public partial class InvoiceListPage : ContentPage
{
    private readonly InvoiceListViewModel viewModel;
    private readonly AutoSyncService autoSyncService;

    public InvoiceListPage(InvoiceListViewModel viewModel, AutoSyncService autoSyncService)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.autoSyncService = autoSyncService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        autoSyncService.DataChanged += OnDataChanged;
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        autoSyncService.DataChanged -= OnDataChanged;
        base.OnDisappearing();
    }

    private async void OnDataChanged(object? sender, EventArgs e)
    {
        await viewModel.ReloadFromLocalAsync();
    }

    private void OnInvoiceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        if (e.CurrentSelection.FirstOrDefault() is InvoiceListItemViewModel item)
        {
            viewModel.OpenCommand.Execute(item);
        }
    }
}
