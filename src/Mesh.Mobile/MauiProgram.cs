namespace Mesh.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<BleService>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<MessageRepository>();
        builder.Services.AddSingleton<IMeshNotificationService, ShinyNotificationService>();

        builder.Services.AddSingleton<MainPageViewModel>();
        builder.Services.AddSingleton<NetworkPageViewModel>();
        builder.Services.AddTransient<NodesPageViewModel>();
        builder.Services.AddTransient<SettingsPageViewModel>();

        builder.Services.AddSingleton<App>();
        builder.Services.AddTransient<MessagesPage>();
        builder.Services.AddTransient<NetworkPage>();
        builder.Services.AddTransient<NodesPage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}