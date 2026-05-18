using System.Buffers.Binary;
using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.Core;

public static class MeshCoreProtocol
{
    // Commands (App → Node)
    private const byte CMD_APP_START    = 0x01;
    private const byte CMD_SEND_MSG     = 0x03;
    private const byte CMD_DEVICE_QUERY = 0x16;

    // Packet types (Node → App)
    public const byte PKT_SELF_INFO   = 0x05;
    public const byte PKT_DEVICE_INFO = 0x0D;
    public const byte PKT_MSG         = 0x08;
    public const byte PKT_MSG_V3      = 0x11; // includes SNR

    public const string AppName = "MeshUnited";

    /// <summary>
    /// Handshake sent immediately after connecting to a MeshCore node.
    /// Format: [0x01][0x00 × 7 reserved][app_name UTF-8]
    /// </summary>
    public static byte[] EncodeAppStart()
    {
        var name = Encoding.UTF8.GetBytes(AppName);
        var packet = new byte[1 + 7 + name.Length]; // cmd + 7 reserved + name
        packet[0] = CMD_APP_START;
        // bytes 1-7: reserved zeros (already zero from array init)
        name.CopyTo(packet, 8);
        return packet;
    }

    /// <summary>
    /// Outgoing message. Format: [0x03][0x00][channel:1B][ts:4B LE][text UTF-8]
    /// </summary>
    public static byte[] EncodeMessage(byte channel, string text)
    {
        var ts = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var textBytes = Encoding.UTF8.GetBytes(text);
        var packet = new byte[3 + 4 + textBytes.Length];
        packet[0] = CMD_SEND_MSG;
        packet[1] = 0x00;
        packet[2] = channel;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), ts);
        textBytes.CopyTo(packet, 7);
        return packet;
    }

    /// <summary>
    /// Decodes an incoming notification from a MeshCore node.
    /// Returns null if the packet is not a message type we handle.
    /// </summary>
    public static (byte Channel, string Text, DateTimeOffset SentAt)? DecodeNotify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return null;

        byte type = data[0];

        if (type == PKT_MSG && data.Length >= 8)
        {
            // [0x08][channel][path_len][text_type][ts:4B LE][text UTF-8]
            var channel  = data[1];
            var ts       = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            var sentAt   = DateTimeOffset.FromUnixTimeSeconds(ts);
            var text     = data.Length > 8 ? Encoding.UTF8.GetString(data[8..]) : string.Empty;
            return (channel, text, sentAt);
        }

        if (type == PKT_MSG_V3 && data.Length >= 9)
        {
            // [0x11][channel][path_len][text_type][ts:4B LE][snr:1B][text UTF-8]
            var channel  = data[1];
            var ts       = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            var sentAt   = DateTimeOffset.FromUnixTimeSeconds(ts);
            var text     = data.Length > 9 ? Encoding.UTF8.GetString(data[9..]) : string.Empty;
            return (channel, text, sentAt);
        }

        return null;
    }

    /// <summary>
    /// Parses a PKT_SELF_INFO (0x05) notification sent by MeshCore after CMD_APP_START.
    /// Full layout (fw >= some version): [0x05][adv_type][tx_pwr][max_tx][pubkey:32][lat:4LE][lon:4LE]
    ///   [multi_ack][loc_policy][telemetry][manual_add][freq:4LE][bw:4LE][sf][cr][name UTF-8, null-term]
    /// Short layout (fw v11, Apr-2026): [0x05][adv_type][tx_pwr][max_tx][pubkey_partial:16] = 20 bytes.
    /// Fields beyond the received length are zero/empty.
    /// </summary>
    public static MeshCoreNodeInfo? ParseSelfInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != PKT_SELF_INFO) return null;

        var pubKey  = data.Length >= 36 ? data[4..36].ToArray() : (data.Length > 4 ? data[4..].ToArray() : []);
        var latRaw  = data.Length >= 44 ? BinaryPrimitives.ReadInt32LittleEndian(data[36..40])  : 0;
        var lonRaw  = data.Length >= 44 ? BinaryPrimitives.ReadInt32LittleEndian(data[40..44])  : 0;
        var freqRaw = data.Length >= 52 ? BinaryPrimitives.ReadUInt32LittleEndian(data[48..52]) : 0u;
        var bwRaw   = data.Length >= 56 ? BinaryPrimitives.ReadUInt32LittleEndian(data[52..56]) : 0u;
        var sf      = data.Length >= 57 ? data[56] : (byte)0;
        var cr      = data.Length >= 58 ? data[57] : (byte)0;

        var nameSpan = data.Length > 58 ? data[58..] : ReadOnlySpan<byte>.Empty;
        var nullIdx = nameSpan.IndexOf((byte)0);
        if (nullIdx >= 0) nameSpan = nameSpan[..nullIdx];

        return new MeshCoreNodeInfo(
            DeviceName:      nameSpan.IsEmpty ? string.Empty : Encoding.UTF8.GetString(nameSpan),
            PublicKey:       pubKey,
            FrequencyMhz:    freqRaw / 1000f,
            BandwidthKhz:    bwRaw   / 1000f,
            SpreadingFactor: sf,
            CodingRate:      cr,
            TxPower:         (sbyte)data[2],
            MaxTxPower:      data.Length >= 4 ? (sbyte)data[3] : (sbyte)0,
            Latitude:        latRaw  / 1_000_000f,
            Longitude:       lonRaw  / 1_000_000f
        );
    }

    /// <summary>
    /// Device query sent after handshake to retrieve firmware info.
    /// Format: [0x16][max_protocol_version]
    /// </summary>
    public static byte[] EncodeDeviceQuery() => [CMD_DEVICE_QUERY, 0x01];

    /// <summary>
    /// Parses a PKT_DEVICE_INFO (0x0D) response.
    /// Short layout (fw v11, Apr-2026, 20 bytes): [0x0D][fw_ver][max_contacts/2][max_grp_ch][pin:4LE][build_date:12]
    /// Full layout (82 bytes): appends [manufacturer:40][fw_ver_str:20][client_repeat][path_hash_mode]
    /// Fields beyond the received length are empty/false/0.
    /// </summary>
    public static MeshCoreDeviceInfo? ParseDeviceInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 20 || data[0] != PKT_DEVICE_INFO) return null;

        var pin    = BinaryPrimitives.ReadUInt32LittleEndian(data[4..8]);
        var build  = NullTermStr(data[8..Math.Min(20, data.Length)]);
        var manuf  = data.Length >= 60 ? NullTermStr(data[20..60]) : string.Empty;
        var verStr = data.Length >= 80 ? NullTermStr(data[60..80]) : string.Empty;

        return new MeshCoreDeviceInfo(
            FirmwareVersionNum:    data[1],
            MaxContacts:           (byte)(data[2] * 2),
            MaxGroupChannels:      data[3],
            BlePin:                pin,
            BuildDate:             build,
            Manufacturer:          manuf,
            FirmwareVersionString: verStr,
            ClientRepeat:          data.Length > 80 && data[80] != 0,
            PathHashMode:          data.Length > 81 ? data[81] : (byte)0);
    }

    private static string NullTermStr(ReadOnlySpan<byte> data)
    {
        var idx = data.IndexOf((byte)0);
        return Encoding.UTF8.GetString(idx >= 0 ? data[..idx] : data);
    }
}
