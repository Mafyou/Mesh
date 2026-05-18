using Mesh.Mobile.Core.Models;

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

    [ObservableProperty]
    private bool _isNodeInfoVisible;

    [ObservableProperty]
    private string _nodeRadioText = string.Empty;

    [ObservableProperty]
    private string _nodePublicKeyText = string.Empty;

    [ObservableProperty]
    private bool _isDeviceInfoVisible;

    [ObservableProperty]
    private string _nodeFirmwareText = string.Empty;

    [ObservableProperty]
    private string _nodeBlePinText = string.Empty;

    public string AppVersion => AppInfo.VersionString;

    partial void OnUserAliasChanged(string value)
    {
        settingsService.UserAlias = value?.Trim() ?? string.Empty;
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        settingsService.NotificationsEnabled = value;
    }

    partial void OnDarkModeEnabledChanged(bool value)
    {
        if (Application.Current is not null)
            Application.Current.UserAppTheme = value ? AppTheme.Dark : AppTheme.Light;
    }

    public void Subscribe()
    {
        bleService.NodeInfoReceived += OnNodeInfoReceived;
        bleService.DeviceInfoReceived += OnDeviceInfoReceived;
    }

    public void Unsubscribe()
    {
        bleService.NodeInfoReceived -= OnNodeInfoReceived;
        bleService.DeviceInfoReceived -= OnDeviceInfoReceived;
    }

    private void OnNodeInfoReceived(object? sender, MeshCoreNodeInfo info)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyNodeInfo(info));
    }

    private void OnDeviceInfoReceived(object? sender, MeshCoreDeviceInfo info)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplyDeviceInfo(info));
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

        if (bleService.ConnectedNodeInfo is { } info)
            ApplyNodeInfo(info);
        else
            IsNodeInfoVisible = false;

        if (bleService.ConnectedDeviceInfo is { } devInfo)
            ApplyDeviceInfo(devInfo);
        else
            IsDeviceInfoVisible = false;
    }

    private void ApplyNodeInfo(MeshCoreNodeInfo info)
    {
        NodeRadioText = info.RadioSummary;
        NodePublicKeyText = info.PublicKeyHex;
        IsNodeInfoVisible = true;
    }

    private void ApplyDeviceInfo(MeshCoreDeviceInfo info)
    {
        NodeFirmwareText = string.IsNullOrWhiteSpace(info.Manufacturer)
            ? info.FirmwareVersionString
            : $"{info.FirmwareVersionString}  ·  {info.Manufacturer}";
        NodeBlePinText = info.BlePinDisplay;
        IsDeviceInfoVisible = true;
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
    private async Task ResetAsync()
    {
        bool confirmed = await Shell.Current.DisplayAlert(
            "Réinitialiser",
            "Effacer toute la configuration et revenir aux réglages d'usine ?",
            "Réinitialiser", "Annuler");

        if (!confirmed) return;

        settingsService.ResetAll();
        Refresh();
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var json = settingsService.ExportToJson();
        var path = Path.Combine(FileSystem.CacheDirectory, "mesh-config.json");
        await File.WriteAllTextAsync(path, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Exporter la configuration",
            File = new ShareFile(path, "application/json"),
        });
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Choisir un fichier de configuration",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, ["application/json"] },
                { DevicePlatform.iOS,     ["public.json"] },
                { DevicePlatform.WinUI,   [".json"] },
            }),
        });

        if (result is null) return;

        var json = await File.ReadAllTextAsync(result.FullPath);
        bool ok = settingsService.ImportFromJson(json);

        if (ok)
        {
            Refresh();
            await Shell.Current.DisplayAlert("Import réussi", "Configuration restaurée.", "OK");
        }
        else
        {
            await Shell.Current.DisplayAlert("Erreur", "Fichier de configuration invalide.", "OK");
        }
    }
}
