using System.Buffers.Binary;

namespace Mesh.Mobile.Core;

public static class MeshCoreProtocol
{
    // Commands (App → Node)
    private const byte CMD_APP_START  = 0x01;
    private const byte CMD_SEND_MSG   = 0x03;

    // Packet types (Node → App)
    public const byte PKT_SELF_INFO   = 0x05;
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

    public static bool IsMeshCoreDevice(string? name)
        => !string.IsNullOrWhiteSpace(name)
           && !name.StartsWith("Mesh-", StringComparison.OrdinalIgnoreCase);
}
