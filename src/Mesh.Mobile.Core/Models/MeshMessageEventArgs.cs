namespace Mesh.Mobile.Core.Models;

public sealed class MeshMessageEventArgs(byte src, string text) : EventArgs
{
    public byte Src { get; } = src;
    public string Text { get; } = text;
}
