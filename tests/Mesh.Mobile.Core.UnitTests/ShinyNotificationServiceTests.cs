using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Mesh.Mobile.Core.Services;
using Shouldly;
using Shiny.Notifications;

namespace Mesh.Mobile.Core.UnitTests;

public class ShinyNotificationServiceTests
{
    [Fact]
    public async Task EnsurePermissionsAsync_WhenNotificationManagerIsMissing_DoesNothing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var sut = new ShinyNotificationService(services);

        await sut.EnsurePermissionsAsync();
    }

    [Fact]
    public async Task ShowIncomingMessageAsync_WhenNotificationManagerExists_SendsExpectedNotification()
    {
        Notification? sentNotification = null;
        var manager = NotificationManagerProxyFactory.Create((method, args) =>
        {
            if (method.Name == nameof(INotificationManager.Send))
            {
                sentNotification = args?[0] as Notification;
            }

            return ProxyReturn.Create(method.ReturnType);
        });

        var services = new ServiceCollection()
            .AddSingleton(manager)
            .BuildServiceProvider();

        var sut = new ShinyNotificationService(services);

        await sut.ShowIncomingMessageAsync(0x3C, "Bonjour");

        sentNotification.ShouldNotBeNull();
        sentNotification!.Title.ShouldBe("Message 0x3C");
        sentNotification.Message.ShouldBe("Bonjour");
        sentNotification.Channel.ShouldBe("mesh_messages");
        sentNotification.Payload.ShouldContainKey("src");
        sentNotification.Payload["src"].ShouldBe("3C");
    }

    [Fact]
    public async Task EnsurePermissionsAsync_WhenNotificationManagerExists_RequestsAccess()
    {
        var requested = false;
        var manager = NotificationManagerProxyFactory.Create((method, args) =>
        {
            if (method.Name == nameof(INotificationManager.RequestAccess))
            {
                requested = true;
            }

            return ProxyReturn.Create(method.ReturnType);
        });

        var services = new ServiceCollection()
            .AddSingleton(manager)
            .BuildServiceProvider();

        var sut = new ShinyNotificationService(services);

        await sut.EnsurePermissionsAsync();

        requested.ShouldBeTrue();
    }

    private class NotificationManagerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            targetMethod.ShouldNotBeNull();
            return Handler?.Invoke(targetMethod!, args);
        }
    }

    private static class NotificationManagerProxyFactory
    {
        public static INotificationManager Create(Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create<INotificationManager, NotificationManagerProxy>();
            ((NotificationManagerProxy)(object)proxy).Handler = handler;
            return proxy;
        }
    }

    private static class ProxyReturn
    {
        public static object? Create(Type returnType)
        {
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var fromResult = typeof(Task)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == nameof(Task.FromResult) && method.IsGenericMethodDefinition);

                var value = resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return fromResult.MakeGenericMethod(resultType).Invoke(null, [value]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
