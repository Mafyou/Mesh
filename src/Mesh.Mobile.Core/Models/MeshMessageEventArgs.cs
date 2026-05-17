namespace Mesh.Mobile.Core.Models;

public sealed class MeshMessageEventArgs(byte src, string text, byte channel = 0, DateTimeOffset? sentAt = null, MeshPacketType type = MeshPacketType.Msg) : EventArgs
{
    public byte Src { get; } = src;
    public string Text { get; } = text;
    public byte Channel { get; } = channel;
    public DateTimeOffset? SentAt { get; } = sentAt;
    public MeshPacketType Type { get; } = type;
}
