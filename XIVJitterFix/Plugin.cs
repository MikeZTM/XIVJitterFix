using Dalamud.Game.Command;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Common.Lua;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace XIVJitterFix;

public sealed class Plugin : IDalamudPlugin
{
    private readonly nint hookAddr;

    private readonly IPluginLog logger;
    private readonly IFramework framework;
    private readonly WindowSystem windowSystem;
    private readonly IDalamudPluginInterface dalamudPluginInterface;
    private readonly ICommandManager commandManager;
    private readonly MainWindow mainWindow;
    private readonly Config pluginConfig;
    private readonly INotificationManager notificationManager;

    public unsafe Plugin(IPluginLog logger, ISigScanner sigScanner,
        IFramework framework, IDalamudPluginInterface dalamudPluginInterface, ICommandManager commandManager, INotificationManager notificationManager)
    {
        this.logger = logger;
        this.framework = framework;
        this.dalamudPluginInterface = dalamudPluginInterface;
        this.commandManager = commandManager;
        this.notificationManager = notificationManager;
        windowSystem = new("XIVJitterFix");
        pluginConfig = dalamudPluginInterface.GetPluginConfig() as Config ?? new();

        if (pluginConfig.Version == 0)
        {
            logger.Info("Migrating XIVJitterFix Config 0->1");
            if (pluginConfig.DownscaleBuffers == 0)
            {
                logger.Info("DownscaleBuffers was set to 0, setting SetDownscaleBuffers to true");
                pluginConfig.SetDownscaleBuffers = true;
            }
            pluginConfig.Version = 1;
            dalamudPluginInterface.SavePluginConfig(pluginConfig);
        }

        mainWindow = new MainWindow(pluginConfig, dalamudPluginInterface);
        windowSystem.AddWindow(mainWindow);

        commandManager.AddHandler("/jitterfix", new CommandInfo(OnCommand) 
        { 
            HelpMessage = "Open the XIVJitterFix config window.\n" +
            "/jitterfix jitter <value> → Sets jitter multiplier to a specific value.", ShowInHelp = true 
        });

        hookAddr = sigScanner.GetStaticAddressFromSig("48 8B 05 ?? ?? ?? ?? 0F B6 8B ?? ?? ?? ?? 88 48");

        framework.Update += Framework_Update;
        dalamudPluginInterface.UiBuilder.OpenConfigUi += UiBuilder_OpenConfigUi;
        dalamudPluginInterface.UiBuilder.Draw += UiBuilder_Draw;
    }


    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries); //Setting specific commands?

        if (splitArgs.Length == 0)
        {
            mainWindow.Toggle();
        }

        if(splitArgs.Length == 2)
        {
            if (splitArgs[0] == "jitter")
            {
                float jittermulti;
                if (float.TryParse(splitArgs[1].Replace(",", "."), CultureInfo.InvariantCulture.NumberFormat, out jittermulti))
                {
                    pluginConfig.JitterMultiplier = jittermulti;
                    dalamudPluginInterface.SavePluginConfig(pluginConfig);
                    notificationManager.AddNotification(new Notification() { Content = $"Jitter set to {jittermulti}", Title = "Jitter value change", Type = NotificationType.Success });
                }
                else
                {
                    logger.Warning("Provided value {0} is not a valid float number", splitArgs[1]);
                    notificationManager.AddNotification(new Notification() { Content = "Failed to set jitter to provided value", Title = "Jitter value change", Type = NotificationType.Error });
                }
            }
        }
    }

    private void UiBuilder_Draw()
    {
        windowSystem.Draw();
    }

    private void UiBuilder_OpenConfigUi()
    {
        mainWindow.Toggle();
    }

    private unsafe void Framework_Update(IFramework framework)
    {
        var prevValue1 = Marshal.ReadByte((nint) GraphicsConfig.Instance(), 0x19);
        var prevValue2 = Marshal.ReadByte((nint)GraphicsConfig.Instance(), 0x1a);

        if (prevValue1 != 1 || prevValue2 != 1)
        {
            Marshal.WriteByte((nint)GraphicsConfig.Instance(), 0x19, 1);
            Marshal.WriteByte((nint)GraphicsConfig.Instance(), 0x1a, 1);
            logger.Verbose("Detected change NpcGpose {0} / Cutscene {1} -> NpcGpose {2} / Cutscene {3}", prevValue1, prevValue2, 
                Marshal.ReadByte((nint)GraphicsConfig.Instance(), 0x19), Marshal.ReadByte((nint)GraphicsConfig.Instance(), 0x1a));
        }

        if (pluginConfig.JitterMultiplier != GraphicsConfig.Instance() -> JitterMultiplier)
        {
            logger.Verbose("Detected change JitterMult current {0} -> desired {1}", GraphicsConfig.Instance() -> JitterMultiplier, pluginConfig.JitterMultiplier);

            GraphicsConfig.Instance() -> JitterMultiplier = pluginConfig.JitterMultiplier;
        }
    }

    public void Dispose()
    {
        commandManager.RemoveHandler("/jitterfix");
        windowSystem.RemoveAllWindows();

        dalamudPluginInterface.UiBuilder.OpenConfigUi -= UiBuilder_OpenConfigUi;
        dalamudPluginInterface.UiBuilder.Draw -= UiBuilder_Draw;

        framework.Update -= Framework_Update;
    }
}
