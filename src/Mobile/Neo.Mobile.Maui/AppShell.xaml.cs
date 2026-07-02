namespace Neo.Mobile.Maui;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(Pages.DashboardPage), typeof(Pages.DashboardPage));
		Routing.RegisterRoute(nameof(Pages.InvoiceListPage), typeof(Pages.InvoiceListPage));
		Routing.RegisterRoute(nameof(Pages.InvoiceEditorPage), typeof(Pages.InvoiceEditorPage));
		Routing.RegisterRoute(nameof(Pages.ConsumedEventsPage), typeof(Pages.ConsumedEventsPage));
	}
}
