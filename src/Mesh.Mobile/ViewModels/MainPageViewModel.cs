using Mesh.Mobile.Core;
using Mesh.Mobile.Core.Models;

namespace Mesh.Mobile.ViewModels;

public partial class MainPageViewModel(BleService bleService, SettingsService settingsService, IMeshNotificationService notificationService) : ObservableObject
{
    private readonly BleService bleService = bleService;
    private readonly SettingsService settingsService = settingsService;
    private readonly IMeshNotificationService notificationService = notificationService;
    private bool _initialized;

    [ObservableProperty]
    private string _statusText = "Non connecté";

    [ObservableProperty]
    private Color _statusColor = GetColor("StatusDisconnected");

    [ObservableProperty]
    private string _scanButtonText = "Scanner";

    [ObservableProperty]
    private bool _isNodePickerVisible;

    [ObservableProperty]
    private bool _notificationsEnabled;

    [ObservableProperty]
    private string _preferredNodesText = "Nœuds favoris: aucun";

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isScanning;

    public ObservableCollection<IDevice> DiscoveredDevices => bleService.DiscoveredDevices;
    public ObservableCollection<NodeContact> KnownNodes => bleService.KnownNodes;

    public ObservableCollection<ChannelChip> ChannelChips { get; } =
    [
        new(0, "Public")  { IsSelected = true },
        new(1, "Équipe"),
        new(2, "Urgence"),
        new(3, "Privé"),
    ];

    private byte _selectedChannel = 0;
    public byte SelectedChannel
    {
        get => _selectedChannel;
        private set
        {
            if (_selectedChannel == value) return;
            _selectedChannel = value;
            RefreshMessages();
        }
    }

    private readonly ObservableCollection<MessageItem> _allMessages = [];
    public ObservableCollection<MessageItem> Messages { get; } = [];

    [ObservableProperty]
    private NodeContact _selectedNode = null!;

    [RelayCommand]
    private void SelectChannel(ChannelChip chip)
    {
        if (SelectedChannel == chip.Id) return;
        foreach (var c in ChannelChips) c.IsSelected = false;
        chip.IsSelected = true;
        SelectedChannel = chip.Id;
    }

    private void RefreshMessages()
    {
        Messages.Clear();
        foreach (var msg in _allMessages.Where(m => m.Channel == _selectedChannel))
            Messages.Add(msg);
    }

    [RelayCommand]
    private void SelectNode(NodeContact node)
    {
        if (ReferenceEquals(SelectedNode, node)) return;
        if (SelectedNode is not null) SelectedNode.IsSelected = false;
        SelectedNode = node;
        node.IsSelected = true;
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SelectedNode = bleService.KnownNodes[0]; // broadcast by default
        bleService.MessageReceived += OnMessageReceived;
        bleService.ConnectionChanged += OnConnectionChanged;
        bleService.DevicesUpdated += OnDevicesUpdated;

        NotificationsEnabled = settingsService.NotificationsEnabled;
        IsConnected = bleService.IsConnected;
        await notificationService.EnsurePermissionsAsync();
        UpdatePreferredNodesLabel();

        // Tenter la connexion auto si des nœuds favoris existent
        if (!bleService.IsConnected && settingsService.PreferredNodeIds.Any())
        {
            await ConnectPreferredAsync();
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (bleService.IsConnected)
        {
            await bleService.DisconnectAsync();
            return;
        }

        if (bleService.IsScanning)
        {
            await bleService.StopScanAsync();
            IsNodePickerVisible = false;
            ScanButtonText = "Scanner";
            StatusText = "Scan arrêté";
            return;
        }

#if ANDROID
        var status = await Permissions.RequestAsync<Permissions.Bluetooth>();
        if (status is not PermissionStatus.Granted)
        {
            StatusText = "Permission Bluetooth requise";
            StatusColor = GetColor("StatusError");
            return;
        }
#endif

        try
        {
            ScanButtonText = "Arrêter";
            StatusText = "Recherche…";
            StatusColor = GetColor("StatusScanning");
            IsNodePickerVisible = true;
            IsScanning = true;

            await bleService.StartScanAsync();
            IsScanning = bleService.IsScanning;
        }
        catch (Exception ex)
        {
            ScanButtonText = "Scanner";
            IsNodePickerVisible = false;
            StatusText = $"Erreur : {ex.Message}";
            StatusColor = GetColor("StatusError");
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = MessageText?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!bleService.IsConnected)
        {
            StatusText = "Connectez-vous à un nœud Mesh d'abord";
            StatusColor = GetColor("StatusError");
            return;
        }

        try
        {
            var formatted = settingsService.FormatOutgoing(text);
            await bleService.SendAsync(SelectedNode?.Id ?? 0xFF, SelectedChannel, formatted);
            var sent = new MessageItem(0x00, formatted, DateTimeOffset.Now, SelectedChannel);
            _allMessages.Add(sent);
            Messages.Add(sent);
            MessageText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"Erreur d'envoi : {ex.Message}";
            StatusColor = GetColor("StatusError");
        }
    }

    [RelayCommand]
    private async Task ConnectPreferredAsync()
    {
        StatusText = "Connexion prioritaire…";
        StatusColor = GetColor("StatusScanning");

        var connected = await bleService.TryConnectPreferredAsync(settingsService.PreferredNodeIds);
        if (!connected)
        {
            StatusText = "Aucun nœud favori joignable";
            StatusColor = GetColor("StatusError");
        }
    }

    [RelayCommand]
    private async Task NavigateToSettingsAsync()
        => await Shell.Current.GoToAsync(nameof(SettingsPage));

    [RelayCommand]
    public async Task ConnectSelectedNodeAsync(IDevice device)
    {
        StatusText = $"Connexion à {device.Name}…";
        StatusColor = GetColor("StatusScanning");

        try
        {
            await bleService.ConnectAsync(device);
            var nodeId = device.Id.ToString();
            settingsService.LastNodeId = nodeId;
            settingsService.AddPreferredNodeId(nodeId);
            UpdatePreferredNodesLabel();
        }
        catch (Exception ex)
        {
            StatusText = $"Erreur : {ex.Message}";
            StatusColor = GetColor("StatusError");
        }
    }

    partial void OnNotificationsEnabledChanged(bool value)
    {
        settingsService.NotificationsEnabled = value;
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        var name = bleService.ConnectedNodeName ?? "nœud";
        StatusText = connected ? $"Connecté à {name}" : "Non connecté";
        StatusColor = connected ? GetColor("StatusConnected") : GetColor("StatusDisconnected");
        ScanButtonText = connected ? "Déconnecter" : "Scanner";
        IsNodePickerVisible = false;
        IsConnected = connected;
        IsScanning = false;
    }

    private void OnDevicesUpdated(object? sender, EventArgs e)
    {
        if (bleService.IsScanning)
        {
            IsNodePickerVisible = true;
        }
    }

    private async void OnMessageReceived(object? sender, MeshMessageEventArgs e)
    {
        if (e.Type != MeshPacketType.Msg) return;   // PINGs/NEIGHBORS handled in BleService

        var item = new MessageItem(e.Src, e.Text, e.SentAt ?? DateTimeOffset.Now, e.Channel);
        _allMessages.Add(item);
        if (item.Channel == SelectedChannel)
            Messages.Add(item);
        if (NotificationsEnabled)
            await notificationService.ShowIncomingMessageAsync(e.Src, e.Text);
    }

    private void UpdatePreferredNodesLabel()
    {
        var preferred = settingsService.PreferredNodeIds;
        PreferredNodesText = preferred.Any()
            ? $"Nœuds favoris: {string.Join(", ", preferred.Take(3))}"
            : "Nœuds favoris: aucun";
    }

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;
}