using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ClassIsland.Core.Extensions.Registry;
using System;

namespace PgbShoutBridge;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        ShoutBridgeService.PluginConfigDir = PluginConfigFolder;
        try { services.AddSettingsPage<ShoutSettingsPage>(); } catch (Exception ex) { System.Console.WriteLine("注册设置页失败: " + ex); }
        try
        {
            services.AddNotificationProvider<ShoutNotificationProvider, ShoutNotificationProviderControl>();
            System.IO.File.AppendAllText(System.IO.Path.Combine(PluginConfigFolder ?? ".", "register.log"), DateTime.Now + "  AddNotificationProvider OK\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(PluginConfigFolder ?? ".", "register.log"), DateTime.Now + "  AddNotificationProvider FAILED: " + ex + "\n");
        }
        try
        {
            services.AddAction<ShoutBroadcastAction, ShoutBroadcastActionSettingsControl>();
            services.AddTrigger<ShoutTrigger>();
            services.AddRule("pgb.shoutbridge.receiving-shout", "正在接收批改邦喊话", "\uE7ED", settings => ShoutState.IsReceiving);
            System.IO.File.AppendAllText(System.IO.Path.Combine(PluginConfigFolder ?? ".", "register.log"), DateTime.Now + "  AddAction/AddRule OK\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(PluginConfigFolder ?? ".", "register.log"), DateTime.Now + "  AddAction/AddRule FAILED: " + ex.Message + "\n");
        }
        services.AddSingleton<ShoutBridgeService>();
        services.AddHostedService(sp => sp.GetRequiredService<ShoutBridgeService>());
    }
}
