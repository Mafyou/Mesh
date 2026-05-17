namespace Mesh.Mobile.Core.Services;

public class SettingsService
{
    private const string KeyAlias = "user_alias";
    private const string KeyLastNodeId = "last_node_id";
    private const string KeyPreferredNodeIds = "preferred_node_ids";
    private const string KeyNotificationsEnabled = "notifications_enabled";

    public string UserAlias
    {
        get => Preferences.Default.Get(KeyAlias, "");
        set => Preferences.Default.Set(KeyAlias, value);
    }

    public string LastNodeId
    {
        get => Preferences.Default.Get(KeyLastNodeId, "");
        set => Preferences.Default.Set(KeyLastNodeId, value);
    }

    public bool NotificationsEnabled
    {
        get => Preferences.Default.Get(KeyNotificationsEnabled, true);
        set => Preferences.Default.Set(KeyNotificationsEnabled, value);
    }

    public IReadOnlyList<string> PreferredNodeIds
    {
        get => ParsePreferredNodeIds(Preferences.Default.Get(KeyPreferredNodeIds, ""));
        set => Preferences.Default.Set(KeyPreferredNodeIds, SerializePreferredNodeIds(value));
    }

    public void AddPreferredNodeId(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        var list = PreferredNodeIds.ToList();
        var normalized = nodeId.Trim().ToUpperInvariant();
        if (!list.Contains(normalized))
        {
            list.Insert(0, normalized);
            PreferredNodeIds = list;
        }
    }

    public static IReadOnlyList<string> ParsePreferredNodeIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToUpperInvariant())
            .Distinct()];
    }

    public static string SerializePreferredNodeIds(IEnumerable<string> values)
    {
        var normalized = values
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .Select(nodeId => nodeId.Trim().ToUpperInvariant())
            .Distinct();

        return string.Join(',', normalized);
    }

    public static string FormatOutgoing(string alias, string text) =>
        string.IsNullOrWhiteSpace(alias) ? text : $"{alias}: {text}";

    public string FormatOutgoing(string text) => FormatOutgoing(UserAlias, text);
}
