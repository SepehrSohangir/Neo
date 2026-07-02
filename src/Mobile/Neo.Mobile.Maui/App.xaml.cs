namespace Neo.Mobile.Maui;

public partial class App : Application
{
	private readonly AppShell shell;

	public App(AppShell shell)
	{
		InitializeComponent();
		this.shell = shell;
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(shell);
	}
}
