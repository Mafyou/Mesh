using System.ComponentModel;

namespace Mesh.Mobile.Core.Models;

public class NodeContact : INotifyPropertyChanged
{
    public byte Id { get; init; }
    public string Label => Id == 0xFF ? "Tous" : $"0x{Id:X2}";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; Notify(nameof(IsSelected)); }
    }

    /* ---- Telemetry (from PING) ---- */
    private uint _uptimeSeconds;
    public uint UptimeSeconds
    {
        get => _uptimeSeconds;
        set { if (_uptimeSeconds == value) return; _uptimeSeconds = value; Notify(nameof(UptimeSeconds)); Notify(nameof(UptimeLabel)); }
    }

    private ushort _vbatMv;
    public ushort VbatMv
    {
        get => _vbatMv;
        set { if (_vbatMv == value) return; _vbatMv = value; Notify(nameof(VbatMv)); Notify(nameof(BatteryLabel)); Notify(nameof(HasTelemetry)); }
    }

    private ushort _txPackets;
    public ushort TxPackets
    {
        get => _txPackets;
        set { if (_txPackets == value) return; _txPackets = value; Notify(nameof(TxPackets)); }
    }

    private ushort _rxPackets;
    public ushort RxPackets
    {
        get => _rxPackets;
        set { if (_rxPackets == value) return; _rxPackets = value; Notify(nameof(RxPackets)); }
    }

    /* ---- Neighbor table ---- */
    private sbyte _rssi;
    public sbyte Rssi
    {
        get => _rssi;
        set { if (_rssi == value) return; _rssi = value; Notify(nameof(Rssi)); Notify(nameof(SignalLabel)); Notify(nameof(IsDirectNeighbor)); }
    }

    private sbyte _snr;
    public sbyte Snr
    {
        get => _snr;
        set { if (_snr == value) return; _snr = value; Notify(nameof(Snr)); Notify(nameof(SignalLabel)); }
    }

    private bool _isDirectNeighbor;
    public bool IsDirectNeighbor
    {
        get => _isDirectNeighbor;
        set { if (_isDirectNeighbor == value) return; _isDirectNeighbor = value; Notify(nameof(IsDirectNeighbor)); Notify(nameof(SignalLabel)); }
    }

    /* ---- Computed labels ---- */
    public bool HasTelemetry => VbatMv > 0 || UptimeSeconds > 0;

    public string BatteryLabel => VbatMv == 0
        ? string.Empty
        : $"{VbatMv / 1000.0:0.0} V";

    public string UptimeLabel
    {
        get
        {
            if (UptimeSeconds == 0) return string.Empty;
            var ts = TimeSpan.FromSeconds(UptimeSeconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}"
                : $"{ts.Minutes}m{ts.Seconds:D2}s";
        }
    }

    public string SignalLabel => IsDirectNeighbor ? $"{Rssi} dBm / SNR {Snr}" : string.Empty;

    public void ApplyPingPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 10) return;
        UptimeSeconds = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(payload);
        VbatMv        = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload[4..]);
        TxPackets     = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]);
        RxPackets     = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]);
    }

    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public event PropertyChangedEventHandler? PropertyChanged;
}
