using System.Globalization;

namespace Mesh.Mobile.ViewModels;

public partial class MainPageViewModel(BleService bleService, SettingsService settingsService, IMeshNotificationService notificationService, MessageRepository messageRepository) : ObservableObject
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

    public bool CanSend => IsConnected && !string.IsNullOrWhiteSpace(MessageText);

    partial void OnIsConnectedChanged(bool value)     => OnPropertyChanged(nameof(CanSend));
    partial void OnMessageTextChanged(string value)   => OnPropertyChanged(nameof(CanSend));

    public ObservableCollection<IDevice> DiscoveredDevices => bleService.DiscoveredDevices;

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

    private readonly List<MessageItem> _allMessages = [];
    public ObservableCollection<ChatLine> Messages { get; } = [];

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
        DateOnly? lastDate = null;
        foreach (var msg in _allMessages.Where(m => m.Channel == _selectedChannel))
        {
            var msgDate = DateOnly.FromDateTime(msg.At.LocalDateTime);
            if (lastDate is null || msgDate != lastDate)
            {
                Messages.Add(new DateLine(FormatDateLabel(msg.At)));
                lastDate = msgDate;
            }
            Messages.Add(new MessageLine(msg));
        }
    }

    private MessageLine AddToMessages(MessageItem item)
    {
        var lastLine = Messages.OfType<MessageLine>().LastOrDefault();
        var itemDate = DateOnly.FromDateTime(item.At.LocalDateTime);
        if (lastLine is null || DateOnly.FromDateTime(lastLine.Item.At.LocalDateTime) != itemDate)
            Messages.Add(new DateLine(FormatDateLabel(item.At)));
        var line = new MessageLine(item);
        Messages.Add(line);
        return line;
    }

    private static string FormatDateLabel(DateTimeOffset at)
    {
        var today = DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime);
        var date  = DateOnly.FromDateTime(at.LocalDateTime);
        if (date == today) return "Aujourd'hui";
        if (date == today.AddDays(-1)) return "Hier";
        return at.LocalDateTime.ToString("d MMMM", CultureInfo.GetCultureInfo("fr-FR"));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        bleService.MessageReceived += OnMessageReceived;
        bleService.ConnectionChanged += OnConnectionChanged;
        bleService.DevicesUpdated += OnDevicesUpdated;
        bleService.BondingRequired += OnBondingRequired;

        NotificationsEnabled = settingsService.NotificationsEnabled;
        IsConnected = bleService.IsConnected;
        await notificationService.EnsurePermissionsAsync();
        UpdatePreferredNodesLabel();

        foreach (var msg in messageRepository.LoadAll())
            _allMessages.Add(msg);
        RefreshMessages();

        if (!bleService.IsConnected && settingsService.PreferredNodeIds.Any())
            await ConnectPreferredAsync();
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
        if (string.IsNullOrEmpty(text)) return;
        if (!bleService.IsConnected)
        {
            StatusText = "Connectez-vous à un nœud Mesh d'abord";
            StatusColor = GetColor("StatusError");
            return;
        }

        var formatted = settingsService.FormatOutgoing(text);
        var sent = new MessageItem(0x00, formatted, DateTimeOffset.Now, SelectedChannel);
        _allMessages.Add(sent);
        var line = AddToMessages(sent);
        MessageText = string.Empty;

        try
        {
            await bleService.SendAsync(SelectedChannel, formatted);
            messageRepository.Append(sent);
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch (Exception ex)
        {
            line.IsFailed = true;
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
    private async Task CopyMessageAsync(MessageItem item)
    {
        await Clipboard.SetTextAsync(item.MessageBody);
        HapticFeedback.Default.Perform(HapticFeedbackType.Click);
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
            var nodeId = $"{device.Id}";
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
            IsNodePickerVisible = true;
    }

    private async void OnMessageReceived(object? sender, MeshMessageEventArgs e)
    {
        var item = new MessageItem(e.Src, e.Text, e.SentAt ?? DateTimeOffset.Now, e.Channel);
        _allMessages.Add(item);
        messageRepository.Append(item);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (item.Channel == SelectedChannel)
                AddToMessages(item);
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        });
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

    private void OnBondingRequired(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Shell.Current.DisplayAlertAsync(
                "Association Bluetooth requise",
                "Ce nœud MeshCore nécessite un appairage sécurisé.\n\n" +
                "Si une fenêtre système apparaît, entrez le code PIN affiché sur l'écran du nœud " +
                "(ou 123456 pour les nœuds sans écran).",
                "OK");
        });
    }

    private static Color GetColor(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Transparent;
}
