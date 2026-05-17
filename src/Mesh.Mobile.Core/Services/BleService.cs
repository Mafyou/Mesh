namespace Mesh.Mobile.Core.Services;

public class BleService
{
    public static readonly Guid NusServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NusRxCharUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NusTxCharUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;

    private IDevice? _connectedDevice;
    private ICharacteristic? _rxCharacteristic;
    private ICharacteristic? _txCharacteristic;
    private CancellationTokenSource? _scanCts;
    private bool _isMeshCore;

    public ObservableCollection<IDevice> DiscoveredDevices { get; } = new();
    public ObservableCollection<NodeContact> KnownNodes { get; } =
        [new NodeContact { Id = 0xFF, IsSelected = true }];

    public event EventHandler<MeshMessageEventArgs>? MessageReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler? DevicesUpdated;

    public bool IsConnected => _connectedDevice?.State == DeviceState.Connected;
    public bool IsScanning => _adapter.IsScanning;
    public string? ConnectedNodeName => _connectedDevice?.Name;
    public string? ConnectedNodeId => _connectedDevice?.Id.ToString().ToUpperInvariant();

    public BleService()
    {
        _ble = CrossBluetoothLE.Current;
        _adapter = CrossBluetoothLE.Current.Adapter;

        _adapter.DeviceDiscovered += OnDeviceDiscovered;
        _adapter.DeviceConnected += OnDeviceConnected;
        _adapter.DeviceDisconnected += OnDeviceDisconnected;
        _adapter.DeviceConnectionLost += OnDeviceConnectionLost;
    }

    public async Task StartScanAsync(CancellationToken stoppingToken = default)
    {
        if (_ble.State is not BluetoothState.On)
        {
            throw new InvalidOperationException("Bluetooth is not enabled.");
        }

        await StopScanAsync();
        DiscoveredDevices.Clear();

        _scanCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        _adapter.ScanMode = ScanMode.LowLatency;
        _adapter.ScanTimeout = 30000;
        // No hardware UUID filter: NimBLE puts the 128-bit NUS UUID in the scan response,
        // not the primary advertisement, so hardware filtering silently drops ESP32 devices.
        // Software filtering in OnDeviceDiscovered keeps only named devices.
        await _adapter.StartScanningForDevicesAsync(cancellationToken: _scanCts.Token);
    }

    public async Task StopScanAsync()
    {
        if (_adapter.IsScanning)
        {
            await _adapter.StopScanningForDevicesAsync();
        }

        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
    }

    public async Task ConnectAsync(IDevice device)
    {
        await StopScanAsync();
        await _adapter.ConnectToDeviceAsync(device);
    }

    public async Task<bool> TryConnectPreferredAsync(IReadOnlyList<string> preferredNodeIds, CancellationToken stoppingToken = default)
    {
        if (_ble.State is not BluetoothState.On)
        {
            return false;
        }

        if (!DiscoveredDevices.Any())
        {
            await StartScanAsync(stoppingToken);
        }

        var prioritized = OrderByPreference(preferredNodeIds);
        foreach (var device in prioritized)
        {
            try
            {
                await ConnectAsync(device);
                if (IsConnected)
                {
                    return true;
                }
            }
            catch
            {
                // try next candidate
            }
        }

        return false;
    }

    public async Task DisconnectAsync()
    {
        if (_connectedDevice is not null)
        {
            await _adapter.DisconnectDeviceAsync(_connectedDevice);
        }
    }

    public async Task SendAsync(byte dst, byte channel, string text)
    {
        if (_rxCharacteristic is null)
        {
            throw new InvalidOperationException("Not connected to a Mesh node.");
        }

        var packet = _isMeshCore
            ? MeshCoreProtocol.EncodeMessage(channel, text)
            : MeshProtocol.EncodeWrite(dst, channel, text);

        await _rxCharacteristic.WriteAsync(packet);
    }

    private IReadOnlyList<IDevice> OrderByPreference(IReadOnlyList<string> preferredNodeIds)
    {
        if (!preferredNodeIds.Any())
        {
            return [.. DiscoveredDevices];
        }

        var indexByNode = preferredNodeIds
            .Select((nodeId, index) => (nodeId, index))
            .ToDictionary(pair => pair.nodeId, pair => pair.index, StringComparer.OrdinalIgnoreCase);

        return [.. DiscoveredDevices.OrderBy(device =>
        {
            var nodeId = device.Id.ToString();
            return indexByNode.TryGetValue(nodeId, out var index) ? index : int.MaxValue;
        })];
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        // Accept Mesh United nodes ("Mesh-XX") and MeshCore nodes (any other named device).
        if (string.IsNullOrWhiteSpace(e.Device.Name)) return;

        if (DiscoveredDevices.All(device => device.Id != e.Device.Id))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DiscoveredDevices.Add(e.Device);
                DevicesUpdated?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private async void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        _connectedDevice = e.Device;
        _isMeshCore = MeshCoreProtocol.IsMeshCoreDevice(e.Device.Name);
        try
        {
            var service = await e.Device.GetServiceAsync(NusServiceUuid);
            if (service is null)
            {
                System.Diagnostics.Debug.WriteLine("[BleService] NUS service not found");
                await _adapter.DisconnectDeviceAsync(e.Device);
                return;
            }

            _rxCharacteristic = await service.GetCharacteristicAsync(NusRxCharUuid);
            _txCharacteristic = await service.GetCharacteristicAsync(NusTxCharUuid);
            if (_txCharacteristic is null)
            {
                System.Diagnostics.Debug.WriteLine("[BleService] NUS TX characteristic not found");
                await _adapter.DisconnectDeviceAsync(e.Device);
                return;
            }

            _txCharacteristic.ValueUpdated += OnTxValueUpdated;
            await _txCharacteristic.StartUpdatesAsync();

            if (_isMeshCore && _rxCharacteristic is not null)
            {
                var handshake = MeshCoreProtocol.EncodeAppStart();
                await _rxCharacteristic.WriteAsync(handshake);
                System.Diagnostics.Debug.WriteLine("[BleService] MeshCore handshake sent");
            }

            ConnectionChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleService] Setup failed: {ex.Message}");
            try { await _adapter.DisconnectDeviceAsync(e.Device); } catch { }
        }
    }

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        CleanupConnection();
        ConnectionChanged?.Invoke(this, false);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        CleanupConnection();
        ConnectionChanged?.Invoke(this, false);
    }

    private void CleanupConnection()
    {
        if (_txCharacteristic is not null)
        {
            _txCharacteristic.ValueUpdated -= OnTxValueUpdated;
        }

        _connectedDevice = null;
        _rxCharacteristic = null;
        _txCharacteristic = null;
        _isMeshCore = false;
    }

    private NodeContact GetOrAddNode(byte id)
    {
        var node = KnownNodes.FirstOrDefault(n => n.Id == id);
        if (node is null)
        {
            node = new NodeContact { Id = id };
            KnownNodes.Add(node);
        }
        return node;
    }

    private void OnTxValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var data = e.Characteristic.Value;
        if (data is null || data.Length < 1) return;

        if (_isMeshCore)
        {
            var msg = MeshCoreProtocol.DecodeNotify(data);
            if (msg is null) return;

            MainThread.BeginInvokeOnMainThread(() =>
                MessageReceived?.Invoke(this, new MeshMessageEventArgs(
                    0x00, msg.Value.Text, msg.Value.Channel, msg.Value.SentAt)));
            return;
        }

        if (data.Length < 3) return;
        var packet = MeshProtocol.DecodeNotify(data);
        if (packet is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var node = GetOrAddNode(packet.Value.Src);

            switch (packet.Value.Type)
            {
                case MeshPacketType.Ping:
                    node.ApplyPingPayload(data.AsSpan(3));
                    break;

                case MeshPacketType.Neighbors:
                    ApplyNeighborsPayload(data.AsSpan(3));
                    break;

                default:
                    MessageReceived?.Invoke(this, new MeshMessageEventArgs(
                        packet.Value.Src, packet.Value.Text,
                        packet.Value.Channel, packet.Value.SentAt, packet.Value.Type));
                    break;
            }
        });
    }

    private void ApplyNeighborsPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 1) return;
        var count = payload[0];

        for (int i = 0; i < count && (1 + i * 3 + 2) < payload.Length; i++)
        {
            var id   = payload[1 + i * 3];
            var rssi = (sbyte)payload[2 + i * 3];
            var snr  = (sbyte)payload[3 + i * 3];

            var node = GetOrAddNode(id);
            node.Rssi             = rssi;
            node.Snr              = snr;
            node.IsDirectNeighbor = true;
        }
    }
}
