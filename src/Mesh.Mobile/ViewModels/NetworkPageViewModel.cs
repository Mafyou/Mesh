namespace Mesh.Mobile.ViewModels;

public partial class NetworkPageViewModel(BleService bleService) : ObservableObject
{
    private readonly BleService bleService = bleService;

    [ObservableProperty]
    private string _scanButtonText = "Relancer le scan réseau";

    [ObservableProperty]
    private string _scanStatusText = "Dernière mise à jour : à l'instant";

    [ObservableProperty]
    private int _nodeCount;

    [ObservableProperty]
    private int _messageCount;

    [ObservableProperty]
    private Color _bleStatusColor = GetColor("StatusDisconnected");

    [ObservableProperty]
    private Color _loraStatusColor = GetColor("DarkAccentLora");

    [ObservableProperty]
    private double _signalProgress;

    [ObservableProperty]
    private string _signalText = "Signal actuel : n/a";

    public void Initialize()
    {
        bleService.ConnectionChanged += OnConnectionChanged;
        bleService.DevicesUpdated += OnDevicesUpdated;
        bleService.MessageReceived += OnMessageReceived;
        NodeCount = bleService.DiscoveredDevices.Count;
        UpdateBleStatus();
    }

    public void Cleanup()
    {
        bleService.ConnectionChanged -= OnConnectionChanged;
        bleService.DevicesUpdated -= OnDevicesUpdated;
        bleService.MessageReceived -= OnMessageReceived;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        bool hasPermissions = await BlePermissionHelper.EnsureScanPermissionsAsync();
        if (!hasPermissions)
        {
            ScanStatusText = "Permissions BLE refusées";
            return;
        }

        try
        {
            if (bleService.IsScanning)
            {
                await bleService.StopScanAsync();
                ScanStatusText = "Scan réseau arrêté";
                ScanButtonText = "Relancer le scan réseau";
                return;
            }

            ScanButtonText = "Arrêter le scan";
            ScanStatusText = "Scan réseau en cours…";
            await bleService.StartScanAsync();
            NodeCount = bleService.DiscoveredDevices.Count;
            ScanStatusText = $"Scan terminé : {NodeCount} nœud(s) détecté(s)";
        }
        catch (Exception ex)
        {
            ScanStatusText = $"Erreur scan : {ex.Message}";
        }
        finally
        {
            ScanButtonText = "Relancer le scan réseau";
        }
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        UpdateBleStatus();
    }

    private void OnDevicesUpdated(object? sender, EventArgs e)
    {
        NodeCount = bleService.DiscoveredDevices.Count;
    }

    private void OnMessageReceived(object? sender, MeshMessageEventArgs e)
    {
        MessageCount++;
    }

    private void UpdateBleStatus()
    {
        BleStatusColor = bleService.IsConnected
            ? GetColor("StatusConnected")
            : GetColor("StatusDisconnected");

        if (bleService.IsConnected)
        {
            SignalProgress = 0.75;
            SignalText = "Signal actuel : -65 dBm (fort)";
        }
        else
        {
            SignalProgress = 0;
            SignalText = "Signal actuel : n/a";
        }
    }

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;
}
