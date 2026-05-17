namespace Mesh.Mobile;

public partial class App : Application
{
    private readonly SettingsService _settings;

    public App(IServiceProvider services, SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    /// <summary>
    /// Handles meshunited://node/add?id=XX&amp;alias=Name deep links.
    /// </summary>
    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);

        if (!uri.Scheme.Equals("meshunited", StringComparison.OrdinalIgnoreCase)) return;
        if (!uri.Host.Equals("node", StringComparison.OrdinalIgnoreCase)) return;
        if (!uri.AbsolutePath.Equals("/add", StringComparison.OrdinalIgnoreCase)) return;

        var query = uri.Query.TrimStart('?')
                       .Split('&', StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Split('=', 2))
                       .Where(p => p.Length == 2)
                       .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        var id    = query.GetValueOrDefault("id");
        var alias = query.GetValueOrDefault("alias");

        if (string.IsNullOrWhiteSpace(id)) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _settings.AddPreferredNodeId(id);
            MainPage?.DisplayAlert(
                "Nœud ajouté",
                $"Le nœud {alias ?? id} a été ajouté à vos favoris.",
                "OK");
        });
    }
}
