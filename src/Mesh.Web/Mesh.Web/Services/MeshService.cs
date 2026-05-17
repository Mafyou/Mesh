using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Mesh.Web.Client.Services;
using Microsoft.AspNetCore.Hosting;

namespace Mesh.Web.Services;

public sealed class MeshService : IMeshService
{
    private readonly Lock _msgLock = new();
    private readonly List<MeshMessage> _messages = [];
    private readonly ConcurrentDictionary<byte, NodeEntry> _nodes = new();
    private readonly string _historyPath;
    private int _unsavedCount;

    public event EventHandler? Changed;

    public IReadOnlyList<MeshMessage> Messages
    {
        get
        {
            lock (_msgLock)
            {
                return [.. _messages];
            }
        }
    }

    public IReadOnlyList<NodeInfo> Nodes => [.. _nodes.Values.Select(entry => entry.Info).OrderBy(node => node.Id)];
    public IReadOnlyList<TopologyEdge> Topology => BuildTopology(Messages);

    public MeshService(IWebHostEnvironment env)
    {
        _historyPath = Path.Combine(env.ContentRootPath, "mesh_history.json");
        LoadHistory();
    }

    public IReadOnlyList<MeshMessage> GetMessages(byte? nodeId = null)
    {
        if (nodeId is null)
        {
            return Messages;
        }

        lock (_msgLock)
        {
            return [.. _messages.Where(message => message.Src == nodeId || message.Dst == nodeId)];
        }
    }

    public async Task HandleNodeAsync(WebSocket ws, CancellationToken stoppingToken)
    {
        var header = new byte[32];
        var result = await ws.ReceiveAsync(header, stoppingToken);
        if (result.Count < 1)
        {
            return;
        }

        var nodeId = header[0];
        var name = result.Count > 1
            ? Encoding.UTF8.GetString(header, 1, result.Count - 1)
            : $"Mesh-{nodeId:X2}";

        var now = DateTimeOffset.UtcNow;
        var entry = new NodeEntry(new NodeInfo(name, nodeId, now, now), ws);
        _nodes[nodeId] = entry;
        NotifyChanged();

        var buffer = new byte[256];
        try
        {
            while (ws.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                result = await ws.ReceiveAsync(buffer, stoppingToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.Count < 1)
                {
                    continue;
                }

                var src = buffer[0];
                var text = result.Count > 1 ? Encoding.UTF8.GetString(buffer, 1, result.Count - 1) : string.Empty;
                AddMessage(new MeshMessage(src, 0xFF, text, DateTimeOffset.UtcNow));

                if (_nodes.TryGetValue(nodeId, out var existing))
                {
                    _nodes[nodeId] = existing with { Info = existing.Info with { LastSeen = DateTimeOffset.UtcNow } };
                }

                NotifyChanged();
            }
        }
        finally
        {
            _nodes.TryRemove(nodeId, out _);
            SaveHistory();
            NotifyChanged();
        }
    }

    public async Task SendAsync(byte dst, string text, CancellationToken stoppingToken = default)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var packet = new byte[1 + textBytes.Length];
        packet[0] = dst;
        textBytes.CopyTo(packet, 1);

        foreach (var entry in _nodes.Values)
        {
            await entry.SendLock.WaitAsync(stoppingToken);
            try
            {
                if (entry.Socket.State == WebSocketState.Open)
                {
                    await entry.Socket.SendAsync(packet, WebSocketMessageType.Binary, true, stoppingToken);
                }
            }
            finally
            {
                entry.SendLock.Release();
            }
        }

        AddMessage(new MeshMessage(0x00, dst, text, DateTimeOffset.UtcNow));
        NotifyChanged();
    }

    private static IReadOnlyList<TopologyEdge> BuildTopology(IReadOnlyList<MeshMessage> messages)
    {
        var grouped = messages
            .GroupBy(message => (message.Src, message.Dst))
            .Select(group => new TopologyEdge(group.Key.Src, group.Key.Dst, group.Count(), group.Max(message => message.At)))
            .OrderByDescending(edge => edge.Weight)
            .ThenBy(edge => edge.Src)
            .ThenBy(edge => edge.Dst)
            .ToList();

        return grouped;
    }

    private void AddMessage(MeshMessage message)
    {
        lock (_msgLock)
        {
            _messages.Add(message);
            if (_messages.Count > 500)
            {
                _messages.RemoveAt(0);
            }

            _unsavedCount++;
        }

        if (_unsavedCount >= 10)
        {
            _ = Task.Run(SaveHistory);
        }
    }

    private void LoadHistory()
    {
        if (!File.Exists(_historyPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_historyPath, Encoding.UTF8);
            var messages = JsonSerializer.Deserialize<List<MeshMessage>>(json);
            if (messages is null)
            {
                return;
            }

            lock (_msgLock)
            {
                _messages.AddRange(messages.TakeLast(500));
            }
        }
        catch
        {
            // ignore corrupt history file
        }
    }

    private void SaveHistory()
    {
        List<MeshMessage> snapshot;
        lock (_msgLock)
        {
            snapshot = [.. _messages];
            _unsavedCount = 0;
        }

        try
        {
            var tempPath = _historyPath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot), Encoding.UTF8);
            File.Move(tempPath, _historyPath, overwrite: true);
        }
        catch
        {
            // ignore save errors
        }
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed record NodeEntry(NodeInfo Info, WebSocket Socket)
    {
        public SemaphoreSlim SendLock { get; } = new(1, 1);
    }
}
