namespace Mesh.Mobile;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
