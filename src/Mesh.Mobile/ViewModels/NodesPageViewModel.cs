using QRCoder;

namespace Mesh.Mobile.ViewModels;

public partial class NodesPageViewModel(BleService bleService) : ObservableObject
{
    private readonly BleService bleService = bleService;

    public ObservableCollection<IDevice> DiscoveredDevices => bleService.DiscoveredDevices;

    [ObservableProperty]
    private string _statusText = "Prêt à scanner";

    [ObservableProperty]
    private string _scanButtonText = "Scanner";

    [ObservableProperty]
    private bool _scanButtonEnabled = true;

    [ObservableProperty]
    private ImageSource? _qrCodeImageSource;

    [ObservableProperty]
    private bool _isQrVisible;

    [ObservableProperty]
    private string _connectedNodeInfo = string.Empty;

    public bool IsScanning => bleService.IsScanning;
    public bool IsConnected => bleService.IsConnected;

    public void Subscribe()
    {
        bleService.DevicesUpdated += OnDevicesUpdated;
        bleService.ConnectionChanged += OnConnectionChanged;
    }

    public void Unsubscribe()
    {
        bleService.DevicesUpdated -= OnDevicesUpdated;
        bleService.ConnectionChanged -= OnConnectionChanged;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (bleService.IsScanning)
        {
            await bleService.StopScanAsync();
            StatusText = "Scan arrêté";
            ScanButtonText = "Scanner";
            return;
        }

        var hasPermissions = await BlePermissionHelper.EnsureScanPermissionsAsync();
        if (!hasPermissions)
        {
            StatusText = "Permissions BLE refusées";
            return;
        }

        try
        {
            StatusText = "Scan en cours…";
            ScanButtonText = "Arrêter";
            await bleService.StartScanAsync();
            StatusText = bleService.DiscoveredDevices.Count > 0
                ? $"{bleService.DiscoveredDevices.Count} nœud(s) trouvé(s)"
                : "Aucun nœud Mesh détecté";
        }
        catch (Exception ex)
        {
            StatusText = $"Erreur : {ex.Message}";
        }
        finally
        {
            ScanButtonText = "Scanner";
        }
    }

    [RelayCommand]
    private async Task ConnectDeviceAsync(IDevice device)
    {
        if (device is null) return;

        var name = string.IsNullOrWhiteSpace(device.Name) ? "Nœud Mesh" : device.Name;
        StatusText = $"Connexion à {name}…";
        ScanButtonEnabled = false;

        try
        {
            await bleService.ConnectAsync(device);
        }
        catch (Exception ex)
        {
            StatusText = $"Connexion impossible : {ex.Message}";
            ScanButtonEnabled = true;
        }
    }

    private void OnDevicesUpdated(object? sender, EventArgs e)
    {
        StatusText = $"{bleService.DiscoveredDevices.Count} appareil(s) détecté(s)";
    }

    [RelayCommand]
    private void ShowQrCode()
    {
        var name = bleService.ConnectedNodeName ?? "Mesh";
        var id   = bleService.ConnectedNodeId ?? "00";

        var uri = $"meshunited://node/add?id={Uri.EscapeDataString(id)}&alias={Uri.EscapeDataString(name)}";
        ConnectedNodeInfo = $"{name}  ({id})";

        var qrGen  = new QRCodeGenerator();
        var qrData = qrGen.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var png    = new PngByteQRCode(qrData);
        var bytes  = png.GetGraphic(10);
        QrCodeImageSource = ImageSource.FromStream(() => new MemoryStream(bytes));
        IsQrVisible = true;
    }

    [RelayCommand]
    private void HideQrCode() => IsQrVisible = false;

    private void OnConnectionChanged(object? sender, bool connected)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            ScanButtonEnabled = true;
            IsQrVisible = false;
            if (connected)
            {
                var name = bleService.ConnectedNodeName ?? "Nœud Mesh";
                StatusText = $"Connecté à {name}";
                await Shell.Current.GoToAsync("//MessagesPage");
            }
            else
            {
                StatusText = "Nœud Mesh introuvable sur cet appareil";
            }
        });
    }
}
