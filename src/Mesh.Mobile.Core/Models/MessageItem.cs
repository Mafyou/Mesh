namespace Mesh.Mobile.Core.Models;

public readonly record struct MessageItem(byte Src, string Text, DateTimeOffset At)
{
    public MessageItem(byte src, string text) : this(src, text, DateTimeOffset.Now) { }

    public bool IsLocal => Src == 0x00;
    public bool IsRemote => !IsLocal;

    public string SrcLabel => IsLocal ? "Moi" : $"0x{Src:X2}";
    public string TimeLabel => At.LocalDateTime.ToString("HH:mm");
}
