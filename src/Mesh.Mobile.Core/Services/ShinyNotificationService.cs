namespace Mesh.Mobile.Core.Services;

public sealed class ShinyNotificationService(IServiceProvider serviceProvider) : IMeshNotificationService
{
    private const string ChannelId = "mesh_messages";

    public async Task EnsurePermissionsAsync(CancellationToken stoppingToken = default)
    {
        var manager = serviceProvider.GetService<INotificationManager>();
        if (manager is null)
        {
            return;
        }

        _ = await manager.RequestAccess();
    }

    public async Task ShowIncomingMessageAsync(byte src, string text, CancellationToken stoppingToken = default)
    {
        var manager = serviceProvider.GetService<INotificationManager>();
        if (manager is null)
        {
            return;
        }

        await manager.Send(new Notification
        {
            Title = $"Message 0x{src:X2}",
            Message = text,
            Channel = ChannelId,
            Payload = new Dictionary<string, string>
            {
                ["src"] = $"{src:X2}"
            }
        });
    }
}
