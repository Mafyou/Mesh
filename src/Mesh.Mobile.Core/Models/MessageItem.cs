namespace Mesh.Mobile.Core.Models;

public readonly record struct MessageItem(byte Src, string Text, DateTimeOffset At, byte Channel = 0)
{
    public MessageItem(byte src, string text) : this(src, text, DateTimeOffset.Now) { }

    public bool IsLocal  => Src == 0x00;
    public bool IsRemote => !IsLocal;

    public string SrcLabel => Src switch
    {
        0x00 => "Moi",
        0xFF => "Réseau",
        _    => $"0x{Src:X2}",
    };

    // Parses optional "[Alias]: body" prefix used by some MeshCore nodes
    public string SenderAlias
    {
        get
        {
            if (IsLocal) return "Moi";
            var (alias, _) = ParseAlias();
            return alias ?? SrcLabel;
        }
    }

    public string MessageBody
    {
        get
        {
            var (_, body) = ParseAlias();
            return body ?? Text;
        }
    }

    public string TimeLabel => $"{At.LocalDateTime:HH:mm}";

    private (string? Alias, string? Body) ParseAlias()
    {
        if (!IsRemote || Text.Length < 4 || Text[0] != '[') return (null, null);
        var close = Text.IndexOf(']', 1);
        if (close < 2 || close + 2 >= Text.Length || Text[close + 1] != ':') return (null, null);
        return (Text[1..close], Text[(close + 2)..].TrimStart());
    }
}
