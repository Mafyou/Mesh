namespace Mesh.Web.Client.Services;

public readonly record struct NodeInfo(string Name, byte Id, DateTimeOffset ConnectedAt, DateTimeOffset LastSeen);

public readonly record struct MeshMessage(byte Src, byte Dst, string Text, DateTimeOffset At);

public readonly record struct TopologyEdge(byte Src, byte Dst, int Weight, DateTimeOffset LastSeen);

public interface IMeshService
{
    IReadOnlyList<MeshMessage> Messages { get; }
    IReadOnlyList<NodeInfo> Nodes { get; }
    IReadOnlyList<TopologyEdge> Topology { get; }

    event EventHandler? Changed;

    IReadOnlyList<MeshMessage> GetMessages(byte? nodeId = null);
    Task SendAsync(byte dst, string text, CancellationToken ct = default);
}
