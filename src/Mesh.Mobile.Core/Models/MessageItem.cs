namespace Mesh.Mobile.Core.Models;

public readonly record struct MessageItem(byte Src, string Text, DateTimeOffset At, byte Channel = 0)
{
    public MessageItem(byte src, string text) : this(src, text, DateTimeOffset.Now) { }

    public bool IsLocal => Src == 0x00;
    public bool IsRemote => !IsLocal;

    public string SrcLabel => IsLocal ? "Moi" : $"0x{Src:X2}";
    public string TimeLabel => $"{At.LocalDateTime:HH:mm}";
    public string ChannelBadge => Channel == 0 ? string.Empty : $"#ch{Channel}";
    public bool HasChannelBadge => Channel > 0;
}
