using Neo.Mobile.Maui.Services;
using Neo.Mobile.Maui.ViewModels;

namespace Neo.Mobile.Maui.Pages;

public partial class ConsumedEventsPage : ContentPage
{
    private readonly ConsumedEventsViewModel viewModel;
    private readonly AutoSyncService autoSyncService;

    public ConsumedEventsPage(ConsumedEventsViewModel viewModel, AutoSyncService autoSyncService)
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
}
