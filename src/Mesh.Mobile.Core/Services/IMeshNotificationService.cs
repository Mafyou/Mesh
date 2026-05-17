namespace Mesh.Mobile.Core.Services;

public interface IMeshNotificationService
{
    Task EnsurePermissionsAsync(CancellationToken stoppingToken = default);
    Task ShowIncomingMessageAsync(byte src, string text, CancellationToken stoppingToken = default);
}
