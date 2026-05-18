namespace Mesh.Mobile.Core.Models;

public sealed class MeshMessageEventArgs(byte src, string text, byte channel = 0, DateTimeOffset? sentAt = null) : EventArgs
{
    public byte Src { get; } = src;
    public string Text { get; } = text;
    public byte Channel { get; } = channel;
    public DateTimeOffset? SentAt { get; } = sentAt;
}
