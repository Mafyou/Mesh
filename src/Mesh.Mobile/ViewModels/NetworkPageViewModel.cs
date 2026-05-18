using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.ViewModels;

public partial class NetworkPageViewModel(BleService bleService, MessageRepository messageRepository) : ObservableObject
{
    private readonly BleService bleService = bleService;
    private readonly MessageRepository messageRepository = messageRepository;
    private bool _subscribed;

    [ObservableProperty] private Color  _bleStatusColor  = GetColor("StatusDisconnected");
    [ObservableProperty] private string _bleStatusText   = "Non connecté";
    [ObservableProperty] private bool   _isNodeConnected;

    [ObservableProperty] private string _radioSummary    = "—";
    [ObservableProperty] private string _firmwareSummary = "—";

    [ObservableProperty] private int    _nodeCount;
    [ObservableProperty] private int    _messageCount;

    [ObservableProperty] private string _scanButtonText  = "Scanner les nœuds";
    [ObservableProperty] private string _scanStatusText  = string.Empty;

    public void Initialize()
    {
        NodeCount    = bleService.DiscoveredDevices.Count;
        MessageCount = messageRepository.LoadAll().Count(m => m.IsRemote);
        ApplyConnectionState(bleService.IsConnected);

        if (bleService.ConnectedNodeInfo   is { } ni) ApplyNodeInfo(ni);
        if (bleService.ConnectedDeviceInfo is { } di) ApplyDeviceInfo(di);

        if (_subscribed) return;
        _subscribed = true;

        bleService.ConnectionChanged  += OnConnectionChanged;
        bleService.DevicesUpdated     += OnDevicesUpdated;
        bleService.MessageReceived    += OnMessageReceived;
        bleService.NodeInfoReceived   += OnNodeInfoReceived;
        bleService.DeviceInfoReceived += OnDeviceInfoReceived;
    }

    public void Cleanup()
    {
        if (!_subscribed) return;
        _subscribed = false;

        bleService.ConnectionChanged  -= OnConnectionChanged;
        bleService.DevicesUpdated     -= OnDevicesUpdated;
        bleService.MessageReceived    -= OnMessageReceived;
        bleService.NodeInfoReceived   -= OnNodeInfoReceived;
        bleService.DeviceInfoReceived -= OnDeviceInfoReceived;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        bool ok = await BlePermissionHelper.EnsureScanPermissionsAsync();
        if (!ok) { ScanStatusText = "Permissions BLE refusées"; return; }

        try
        {
            if (bleService.IsScanning)
            {
                await bleService.StopScanAsync();
                ScanStatusText  = "Scan arrêté";
                ScanButtonText  = "Scanner les nœuds";
                return;
            }
            ScanButtonText  = "Arrêter";
            ScanStatusText  = "Scan en cours…";
            await bleService.StartScanAsync();
            NodeCount       = bleService.DiscoveredDevices.Count;
            ScanStatusText  = $"{NodeCount} nœud(s) détecté(s)";
        }
        catch (Exception ex) { ScanStatusText = $"Erreur : {ex.Message}"; }
        finally               { ScanButtonText = "Scanner les nœuds"; }
    }

    private void OnConnectionChanged(object? sender, bool connected)
        => MainThread.BeginInvokeOnMainThread(() => ApplyConnectionState(connected));

    private void OnDevicesUpdated(object? sender, EventArgs e)
        => NodeCount = bleService.DiscoveredDevices.Count;

    private void OnMessageReceived(object? sender, MeshMessageEventArgs e)
        => MessageCount++;

    private void OnNodeInfoReceived(object? sender, MeshCoreNodeInfo info)
        => MainThread.BeginInvokeOnMainThread(() => ApplyNodeInfo(info));

    private void OnDeviceInfoReceived(object? sender, MeshCoreDeviceInfo info)
        => MainThread.BeginInvokeOnMainThread(() => ApplyDeviceInfo(info));

    private void ApplyConnectionState(bool connected)
    {
        BleStatusColor  = GetColor(connected ? "StatusConnected" : "StatusDisconnected");
        BleStatusText   = connected
            ? $"Connecté · {bleService.ConnectedNodeName}"
            : "Non connecté";
        IsNodeConnected = connected;
        if (!connected)
        {
            RadioSummary    = "—";
            FirmwareSummary = "—";
        }
    }

    private void ApplyNodeInfo(MeshCoreNodeInfo info)
        => RadioSummary = info.RadioSummary;

    private void ApplyDeviceInfo(MeshCoreDeviceInfo info)
        => FirmwareSummary = string.IsNullOrWhiteSpace(info.Manufacturer)
            ? info.FirmwareVersionString
            : $"{info.FirmwareVersionString} · {info.Manufacturer}";

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c
            ? c : Colors.Transparent;
}
