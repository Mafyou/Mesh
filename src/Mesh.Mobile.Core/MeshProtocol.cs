using System.Buffers.Binary;

namespace Mesh.Mobile.Core;

public enum MeshPacketType : byte { Msg = 0, Ack = 1, Ping = 2, Neighbors = 4 }

public readonly record struct MeshPacket(byte Src, byte Dst, byte Channel, MeshPacketType Type, string Text, DateTimeOffset? SentAt = null);

public static class MeshProtocol
{
    public const byte Broadcast = 0xFF;
    public const byte PayloadVersion = 0x01;

    /// <summary>
    /// Wire format App→ESP: [dst:1B][channel:1B][0x01][ts:4B LE][text UTF-8]
    /// </summary>
    public static byte[] EncodeWrite(byte dst, byte channel, string text)
    {
        var ts = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var textBytes = Encoding.UTF8.GetBytes(text);
        var packet = new byte[2 + 1 + 4 + textBytes.Length];
        packet[0] = dst;
        packet[1] = channel;
        packet[2] = PayloadVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(3), ts);
        textBytes.CopyTo(packet, 7);
        return packet;
    }

    /// <summary>
    /// Wire format ESP→App: [src:1B][channel:1B][type:1B][payload...]
    /// For Msg: payload = [0x01][ts:4B LE][text UTF-8] or raw UTF-8
    /// For Ping: payload = [uptime:4B][vbat_mV:2B][tx:2B][rx:2B]
    /// For Neighbors: payload = [count:1B][{id, rssi_signed, snr_signed}...]
    /// </summary>
    public static MeshPacket? DecodeNotify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3)
            return null;

        var src     = data[0];
        var channel = data[1];
        var type    = (MeshPacketType)data[2];
        var payload = data[3..];

        DateTimeOffset? sentAt = null;
        string text;

        if (type == MeshPacketType.Msg && payload.Length >= 5 && payload[0] == PayloadVersion)
        {
            var ts = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
            sentAt = DateTimeOffset.FromUnixTimeSeconds(ts);
            text = payload.Length > 5 ? Encoding.UTF8.GetString(payload[5..]) : string.Empty;
        }
        else if (type == MeshPacketType.Msg)
        {
            text = payload.Length > 0 ? Encoding.UTF8.GetString(payload) : string.Empty;
        }
        else
        {
            text = string.Empty;
        }

        return new MeshPacket(src, Broadcast, channel, type, text, sentAt);
    }
}
