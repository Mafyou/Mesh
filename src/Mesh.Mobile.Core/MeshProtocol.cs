namespace Mesh.Mobile.Core;

public readonly record struct MeshPacket(byte Src, byte Dst, string Text);

public static class MeshProtocol
{
    public const byte Broadcast = 0xFF;

    public static byte[] EncodeWrite(byte dst, string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var packet = new byte[1 + textBytes.Length];
        packet[0] = dst;
        textBytes.CopyTo(packet, 1);
        return packet;
    }

    public static MeshPacket? DecodeNotify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
            return null;

        var src = data[0];
        var text = data.Length > 1
            ? Encoding.UTF8.GetString(data[1..])
            : string.Empty;

        return new MeshPacket(src, Broadcast, text);
    }
}
