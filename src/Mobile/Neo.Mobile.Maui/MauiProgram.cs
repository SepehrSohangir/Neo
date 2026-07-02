using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Neo.Mobile.Maui.Configuration;
using Neo.Mobile.Maui.Services;
using Neo.Mobile.Maui.ViewModels;
using Neo.Mobile.Maui.Pages;

namespace Neo.Mobile.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("Vazir.ttf", "Vazir");
				fonts.AddFont("Vazir-Bold.ttf", "VazirBold");
				fonts.AddFont("Vazir-Medium.ttf", "VazirMedium");
				fonts.AddFont("Vazir-Light.ttf", "VazirLight");
			});

		var settings = AppSettingsLoader.Load();
		builder.Services.AddSingleton(settings);
		builder.Services.AddSingleton<AppSession>();
		builder.Services.AddSingleton<MobileRepository>();
		builder.Services.AddHttpClient<SyncApiClient>();
		builder.Services.AddSingleton<OfflineSyncService>();
		builder.Services.AddSingleton<AutoSyncService>();

		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<InvoiceListViewModel>();
		builder.Services.AddTransient<InvoiceEditorViewModel>();
		builder.Services.AddTransient<ConsumedEventsViewModel>();

		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<InvoiceListPage>();
		builder.Services.AddTransient<InvoiceEditorPage>();
		builder.Services.AddTransient<ConsumedEventsPage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
