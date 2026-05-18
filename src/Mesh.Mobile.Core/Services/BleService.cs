namespace Mesh.Mobile.Core.Services;

public class BleService
{
    public static readonly Guid NusServiceUuid = Guid.Parse("6E400001-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NusRxCharUuid = Guid.Parse("6E400002-B5A3-F393-E0A9-E50E24DCCA9E");
    public static readonly Guid NusTxCharUuid = Guid.Parse("6E400003-B5A3-F393-E0A9-E50E24DCCA9E");

    private readonly IBluetoothLE _ble;
    private readonly IAdapter _adapter;

    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private IDevice? _connectedDevice;
    private ICharacteristic? _rxCharacteristic;
    private ICharacteristic? _txCharacteristic;
    private CancellationTokenSource? _scanCts;

    public ObservableCollection<IDevice> DiscoveredDevices { get; } = new();

    public event EventHandler<MeshMessageEventArgs>? MessageReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler? DevicesUpdated;
    public event EventHandler? BondingRequired;
    public event EventHandler<MeshCoreNodeInfo>? NodeInfoReceived;
    public event EventHandler<MeshCoreDeviceInfo>? DeviceInfoReceived;

    public MeshCoreNodeInfo? ConnectedNodeInfo { get; private set; }
    public MeshCoreDeviceInfo? ConnectedDeviceInfo { get; private set; }

    public bool IsConnected => _connectedDevice?.State == DeviceState.Connected;
    public bool IsScanning => _adapter.IsScanning;
    public string? ConnectedNodeName => _connectedDevice?.Name;
    public string? ConnectedNodeId => _connectedDevice is null ? null : $"{_connectedDevice.Id}".ToUpperInvariant();

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
        // Prevent concurrent connections: auto-connect (TryConnectPreferredAsync) and a
        // manual tap (NodesPage/MessagesPage) can race and both reach ConnectToDeviceAsync,
        // which opens two separate GATT sessions on Android.
        if (!await _connectLock.WaitAsync(0)) return;
        try
        {
            if (IsConnected) return;
            await StopScanAsync();
            await _adapter.ConnectToDeviceAsync(device);
        }
        finally
        {
            _connectLock.Release();
        }
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

    public async Task SendAsync(byte channel, string text)
    {
        if (_rxCharacteristic is null)
            throw new InvalidOperationException("Not connected to a MeshCore node.");

        var packet = MeshCoreProtocol.EncodeMessage(channel, text);
        System.Diagnostics.Debug.WriteLine($"[BleService] SendMsg ch={channel} len={packet.Length} bytes={Convert.ToHexString(packet)}");
        try
        {
            await _rxCharacteristic.WriteAsync(packet);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleService] SendMsg failed: {ex.GetType().Name}: {ex.Message}");
            throw;
        }
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
            var nodeId = $"{device.Id}";
            return indexByNode.TryGetValue(nodeId, out var index) ? index : int.MaxValue;
        })];
    }

    private void OnDeviceDiscovered(object? sender, DeviceEventArgs e)
    {
        if (!AdvertisesNusService(e.Device)) return;

        if (DiscoveredDevices.All(device => device.Id != e.Device.Id))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                DiscoveredDevices.Add(e.Device);
                DevicesUpdated?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    private static bool AdvertisesNusService(IDevice device)
    {
        // Fast path: MeshCore nodes advertise by name prefix
        if (!string.IsNullOrWhiteSpace(device.Name) &&
            device.Name.StartsWith("MeshCore-", StringComparison.OrdinalIgnoreCase))
            return true;

        // NimBLE puts the 128-bit NUS UUID in the scan response, not the primary advertisement;
        // Plugin.BLE merges both into AdvertisementRecords so we can still match here.
        var nusBytes = NusServiceUuid.ToByteArray();
        return device.AdvertisementRecords.Any(r =>
            (r.Type == AdvertisementRecordType.UuidsComplete128Bit ||
             r.Type == AdvertisementRecordType.UuidsIncomplete128Bit) &&
            ContainsUuid128(r.Data, nusBytes));
    }

    private static bool ContainsUuid128(byte[]? data, byte[] uuid)
    {
        if (data is null || data.Length < 16) return false;
        for (var i = 0; i <= data.Length - 16; i += 16)
            if (data.AsSpan(i, 16).SequenceEqual(uuid))
                return true;
        return false;
    }

    private async void OnDeviceConnected(object? sender, DeviceEventArgs e)
    {
        _connectedDevice = e.Device;
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

            // Only use write-without-response when PROPERTY_WRITE_NO_RESPONSE is genuinely
            // present. Plugin.BLE's internal CanWriteWithoutResponse has a quirk where it can
            // return true even when that flag is absent, so we check the Properties bitmask
            // directly to avoid silently sending ATT Write Commands to a characteristic that
            // expects ATT Write Requests (which would cause the remote to silently drop writes).
            if (_rxCharacteristic is not null &&
                _rxCharacteristic.Properties.HasFlag(CharacteristicPropertyType.WriteWithoutResponse))
            {
                _rxCharacteristic.WriteType = CharacteristicWriteType.WithoutResponse;
            }

            // Signal connected immediately so the UI responds without waiting for the handshake.
            // CMD_APP_START requires BLE bonding (ESP_GATT_PERM_WRITE_ENC_MITM);
            // bonding may take many seconds if the user must enter a PIN in the system dialog.
            ConnectionChanged?.Invoke(this, true);

            if (_rxCharacteristic is not null && _connectedDevice is not null)
                await SendMeshCoreHandshakeAsync(_connectedDevice, _rxCharacteristic);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleService] Setup failed: {ex.Message}");
            try { await _adapter.DisconnectDeviceAsync(e.Device); } catch { }
        }
    }

    private async Task SendMeshCoreHandshakeAsync(IDevice device, ICharacteristic rxChar)
    {
#if ANDROID
        // MeshCore RX characteristic requires ESP_GATT_PERM_WRITE_ENC_MITM.  Any write without
        // a prior bond fails with INSUFFICIENT_AUTHENTICATION.  Initiate bonding explicitly
        // so the system pairing dialog appears before we attempt the handshake write, giving the
        // user time to enter the static PIN (printed on the node or displayed on its screen;
        // default for headless firmware is 123456).
        var nativeDevice = device.NativeDevice as Android.Bluetooth.BluetoothDevice;
        if (nativeDevice is not null && nativeDevice.BondState != Android.Bluetooth.Bond.Bonded)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BleService] MeshCore: bond state={nativeDevice.BondState} — initiating pairing");
            MainThread.BeginInvokeOnMainThread(() => BondingRequired?.Invoke(this, EventArgs.Empty));

            using var bondCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var bonded = await EnsureBondedAsync(nativeDevice, bondCts.Token);
            if (!bonded)
            {
                System.Diagnostics.Debug.WriteLine("[BleService] MeshCore: pairing incomplete — handshake aborted");
                return;
            }
            System.Diagnostics.Debug.WriteLine("[BleService] MeshCore: paired — writing handshake");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[BleService] MeshCore: already paired");
        }
#endif

        var handshake = MeshCoreProtocol.EncodeAppStart();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await rxChar.WriteAsync(handshake, cts.Token);
            System.Diagnostics.Debug.WriteLine("[BleService] MeshCore handshake sent");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[BleService] MeshCore handshake: write timed out (10s)");
            return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BleService] MeshCore handshake: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        // fw v11 (Apr-2026) pushes PKT_SELF_INFO + PKT_DEVICE_INFO proactively on CCCD write,
        // before CMD_APP_START is even sent. CMD_DEVICE_QUERY is redundant for this firmware.
    }

#if ANDROID
    private static async Task<bool> EnsureBondedAsync(Android.Bluetooth.BluetoothDevice nativeDevice, CancellationToken stoppingToken)
    {
        if (nativeDevice.BondState == Android.Bluetooth.Bond.Bonded) return true;

        var tcs = new TaskCompletionSource<bool>();
        var receiver = new BondStateReceiver(nativeDevice.Address!, tcs);
        Android.App.Application.Context.RegisterReceiver(
            receiver,
            new Android.Content.IntentFilter(Android.Bluetooth.BluetoothDevice.ActionBondStateChanged));
        try
        {
            // If bonding is already in progress (e.g. triggered by a prior failed write), just wait.
            if (nativeDevice.BondState != Android.Bluetooth.Bond.Bonding)
            {
                if (!nativeDevice.CreateBond())
                {
                    System.Diagnostics.Debug.WriteLine("[BleService] CreateBond() returned false");
                    return nativeDevice.BondState == Android.Bluetooth.Bond.Bonded;
                }
            }
            using (stoppingToken.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[BleService] Bond wait timed out");
            return false;
        }
        finally
        {
            try { Android.App.Application.Context.UnregisterReceiver(receiver); } catch { }
        }
    }

    private sealed class BondStateReceiver : Android.Content.BroadcastReceiver
    {
        private readonly string _address;
        private readonly TaskCompletionSource<bool> _tcs;

        internal BondStateReceiver(string address, TaskCompletionSource<bool> tcs)
        {
            _address = address;
            _tcs = tcs;
        }

        public override void OnReceive(Android.Content.Context? context, Android.Content.Intent? intent)
        {
#pragma warning disable CA1422
            var device = intent?.GetParcelableExtra(Android.Bluetooth.BluetoothDevice.ExtraDevice)
                as Android.Bluetooth.BluetoothDevice;
#pragma warning restore CA1422
            if (device?.Address != _address) return;

            var state = (Android.Bluetooth.Bond)(intent?.GetIntExtra(
                Android.Bluetooth.BluetoothDevice.ExtraBondState,
                (int)Android.Bluetooth.Bond.None) ?? (int)Android.Bluetooth.Bond.None);

            System.Diagnostics.Debug.WriteLine($"[BleService] Bond state → {state}");
            if (state == Android.Bluetooth.Bond.Bonded)
                _tcs.TrySetResult(true);
            else if (state == Android.Bluetooth.Bond.None)
                _tcs.TrySetResult(false);
            // Bond.Bonding = still in progress, keep waiting
        }
    }
#endif

    private void OnDeviceDisconnected(object? sender, DeviceEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[BleService] DeviceDisconnected: {e.Device.Name} ({e.Device.Id})");
        CleanupConnection();
        ConnectionChanged?.Invoke(this, false);
    }

    private void OnDeviceConnectionLost(object? sender, DeviceErrorEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[BleService] ConnectionLost: {e.Device.Name} ({e.Device.Id})");
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
        ConnectedNodeInfo = null;
        ConnectedDeviceInfo = null;
    }

    private void OnTxValueUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var data = e.Characteristic.Value;
        if (data is null || data.Length < 1) return;

        System.Diagnostics.Debug.WriteLine(
            $"[BleService] RX type=0x{data[0]:X2} len={data.Length} bytes={Convert.ToHexString(data)}");

        if (data[0] == MeshCoreProtocol.PKT_SELF_INFO)
        {
            var info = MeshCoreProtocol.ParseSelfInfo(data);
            if (info is not null)
            {
                ConnectedNodeInfo = info;
                System.Diagnostics.Debug.WriteLine(
                    $"[BleService] PKT_SELF_INFO: '{info.DeviceName}'  txPwr={info.TxPower}dBm  {info.RadioSummary}");
                MainThread.BeginInvokeOnMainThread(() =>
                    NodeInfoReceived?.Invoke(this, info));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BleService] PKT_SELF_INFO parse failed len={data.Length}");
            }
            return;
        }

        if (data[0] == MeshCoreProtocol.PKT_DEVICE_INFO)
        {
            var devInfo = MeshCoreProtocol.ParseDeviceInfo(data);
            if (devInfo is not null)
            {
                ConnectedDeviceInfo = devInfo;
                System.Diagnostics.Debug.WriteLine(
                    $"[BleService] PKT_DEVICE_INFO: fw={devInfo.FirmwareVersionNum} build='{devInfo.BuildDate}' PIN={devInfo.BlePinDisplay}");
                MainThread.BeginInvokeOnMainThread(() =>
                    DeviceInfoReceived?.Invoke(this, devInfo));
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[BleService] PKT_DEVICE_INFO parse failed len={data.Length}");
            }
            return;
        }

        var msg = MeshCoreProtocol.DecodeNotify(data);
        if (msg is null)
        {
            System.Diagnostics.Debug.WriteLine($"[BleService] unhandled type=0x{data[0]:X2} len={data.Length}");
            return;
        }

        // 0xFF = "unknown network sender" — channel messages have no sender ID in MeshCore
        MainThread.BeginInvokeOnMainThread(() =>
            MessageReceived?.Invoke(this, new MeshMessageEventArgs(
                0xFF, msg.Value.Text, msg.Value.Channel, msg.Value.SentAt)));
    }
}
