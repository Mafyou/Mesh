namespace Mesh.Mobile.ViewModels;

public partial class SettingsPageViewModel(SettingsService settingsService, BleService bleService) : ObservableObject
{
    private readonly SettingsService settingsService = settingsService;
    private readonly BleService bleService = bleService;

    [ObservableProperty]
    private string _userAlias = settingsService.UserAlias;

    [ObservableProperty]
    private bool _notificationsEnabled = settingsService.NotificationsEnabled;

    [ObservableProperty]
    private bool _darkModeEnabled = Application.Current?.UserAppTheme == AppTheme.Dark;

    [ObservableProperty]
    private string _connectedNodeText = "Non connecté";

    [ObservableProperty]
    private Color _connectedDotColor = GetColor("StatusDisconnected");

    public string AppVersion => AppInfo.VersionString;

    partial void OnNotificationsEnabledChanged(bool value)
    {
        settingsService.NotificationsEnabled = value;
    }

    partial void OnDarkModeEnabledChanged(bool value)
    {
        if (Application.Current is not null)
            Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
    }

    public void Refresh()
    {
        UserAlias = settingsService.UserAlias;
        NotificationsEnabled = settingsService.NotificationsEnabled;
        DarkModeEnabled = Application.Current?.UserAppTheme == AppTheme.Dark;
        var connected = bleService.IsConnected;
        ConnectedNodeText = connected
            ? $"{bleService.ConnectedNodeName}  ·  {bleService.ConnectedNodeId}"
            : "Non connecté";
        ConnectedDotColor = GetColor(connected ? "StatusConnected" : "StatusDisconnected");
    }

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;

    [ObservableProperty]
    private string _diagnosticsStatusText = string.Empty;

    [RelayCommand]
    private async Task RunDiagnosticsAsync()
    {
        var hasPermissions = await BlePermissionHelper.EnsureScanPermissionsAsync();
        if (!hasPermissions)
        {
            DiagnosticsStatusText = "Permissions BLE refusées";
            return;
        }

        try
        {
            DiagnosticsStatusText = "Diagnostic BLE : vérification des permissions OK";
        }
        catch (Exception ex)
        {
            DiagnosticsStatusText = $"Erreur diagnostic : {ex.Message}";
        }
    }

    [RelayCommand]
    private void Save()
    {
        settingsService.UserAlias = UserAlias?.Trim() ?? string.Empty;
    }
}
