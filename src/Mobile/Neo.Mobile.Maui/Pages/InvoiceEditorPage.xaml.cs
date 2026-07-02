using Neo.Mobile.Maui.ViewModels;

namespace Neo.Mobile.Maui.Pages;

public partial class InvoiceEditorPage : ContentPage
{
    private readonly InvoiceEditorViewModel viewModel;

    public InvoiceEditorPage(InvoiceEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }
}
