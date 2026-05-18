using System.Text.Json;
using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.Services;

public class MessageRepository
{
    private const int MaxPerChannel = 300;

    private readonly string _dataDir = FileSystem.AppDataDirectory;

    public IEnumerable<MessageItem> LoadAll()
    {
        var all = new List<MessageItem>();
        for (byte ch = 0; ch < 8; ch++)
        {
            var path = GetPath(ch);
            if (File.Exists(path))
                all.AddRange(Load(ch));
        }
        all.Sort((a, b) => a.At.CompareTo(b.At));
        return all;
    }

    public void Append(MessageItem item)
    {
        var messages = Load(item.Channel);
        messages.Add(item);
        if (messages.Count > MaxPerChannel)
            messages = messages[^MaxPerChannel..];
        File.WriteAllText(GetPath(item.Channel),
            JsonSerializer.Serialize(messages.Select(m => new Stored(m.Src, m.Text, m.At, m.Channel))));
    }

    private List<MessageItem> Load(byte channel)
    {
        var path = GetPath(channel);
        if (!File.Exists(path)) return [];
        try
        {
            var stored = JsonSerializer.Deserialize<List<Stored>>(File.ReadAllText(path));
            return stored?.Select(s => new MessageItem(s.Src, s.Text, s.At, s.Channel)).ToList() ?? [];
        }
        catch { return []; }
    }

    private string GetPath(byte channel) =>
        Path.Combine(_dataDir, $"messages_ch{channel}.json");

    private record struct Stored(byte Src, string Text, DateTimeOffset At, byte Channel);
}
