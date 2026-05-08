// SPDX-License-Identifier: GPL-3.0-or-later
/*
    SimRailConnect
    Copyright © 2026 rinnyanneko

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/
using System;
using System.IO;
using System.Reflection;
using MelonLoader;

// MelonLoader plugin registration — must be at assembly scope (outside any namespace).
[assembly: MelonInfo(typeof(SimRailConnect.Plugin), SimRailConnect.Plugin.PluginName, SimRailConnect.Plugin.PluginVersion, "rinnyanneko")]
[assembly: MelonGame]

namespace SimRailConnect;

public class Plugin : MelonMod
{
    public const string PluginName = "SimRailConnect";
    public const string PluginVersion = "0.0.1";

    /// <summary>
    /// Per-plugin logger instance.  Assigned once in <see cref="OnInitializeMelon"/>
    /// so that <c>WebSocketApiServer</c> can write structured log entries
    /// without importing MelonLoader directly.
    /// </summary>
    internal static MelonLogger.Instance Logger = null!;

    internal static WebSocketApiServer? WebSocketServer { get; private set; }
#if SIMRAIL_IL2CPP
    private readonly PyscreenTelemetryCollector _telemetryCollector = new();
#endif

    public override void OnInitializeMelon()
    {
        Logger = base.LoggerInstance;
        Logger.Msg($"{PluginName} v{PluginVersion} loading...");

        try
        {
            var gameBasePath = !string.IsNullOrWhiteSpace(MelonLoader.Utils.MelonEnvironment.GameRootDirectory)
                ? MelonLoader.Utils.MelonEnvironment.GameRootDirectory
                : AppContext.BaseDirectory;
            gameBasePath = gameBasePath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var melonLoaderPath = Path.Combine(gameBasePath, "MelonLoader");
            var il2CppAssembliesPath = Path.Combine(melonLoaderPath, "Il2CppAssemblies");
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            // ── Read config (UserData/MelonPreferences.cfg) ───────────────────
            var category = MelonPreferences.CreateCategory("SimRailConnect");

            var updateInterval = category.CreateEntry(
                "UpdateIntervalMs", 100,
                "Telemetry update interval in milliseconds");

            var webSocketPort = category.CreateEntry(
                "WebSocketPort", 5556,
                "Local WebSocket API server port");

            var webSocketMaxClients = category.CreateEntry(
                "WebSocketMaxClients", 3,
                "Maximum concurrent WebSocket clients");

            var webSocketDefaultRateHz = category.CreateEntry(
                "WebSocketDefaultRateHz", 10,
                "Default WebSocket telemetry push rate in Hz");

            var webSocketMaxRateHz = category.CreateEntry(
                "WebSocketMaxRateHz", 20,
                "Maximum per-client WebSocket telemetry push rate in Hz");

            var webSocketPayloadLimitBytes = category.CreateEntry(
                "WebSocketPayloadLimitBytes", 16384,
                "Maximum inbound WebSocket JSON payload size in bytes");

            var enablePyscreenTelemetry = category.CreateEntry(
                "EnablePyscreenTelemetry", true,
                "Enable read-only SimRail Pyscreen telemetry collection on the Unity main thread.");

            var apiToken = category.CreateEntry(
                "ApiToken", "",
                "Optional token required by WebSocket clients; blank disables token auth");

            TelemetryState.UpdateIntervalMs = updateInterval.Value;
            TelemetryState.PublishSnapshot(TelemetrySnapshot.CreateInactive("Waiting for SimRail Pyscreen telemetry source."));
#if SIMRAIL_IL2CPP
            _telemetryCollector.IsEnabled = enablePyscreenTelemetry.Value;
#endif

            WebSocketServer = new WebSocketApiServer(
                webSocketPort.Value,
                webSocketMaxClients.Value,
                webSocketDefaultRateHz.Value,
                webSocketMaxRateHz.Value,
                webSocketPayloadLimitBytes.Value,
                apiToken.Value);
            WebSocketServer.Start();

            Logger.Msg($"WebSocket API server started on {WebSocketServer.Url}");
            Logger.Msg($"Loaded assembly path: {assemblyPath}");
            Logger.Msg($"Detected game path: {gameBasePath}");
            Logger.Msg($"Detected Il2CppAssemblies path: {il2CppAssembliesPath} (exists={Directory.Exists(il2CppAssembliesPath)})");
            Logger.Msg($"Telemetry update interval: {updateInterval.Value}ms");
#if SIMRAIL_IL2CPP
            Logger.Msg($"Pyscreen telemetry collector enabled: {_telemetryCollector.IsEnabled}");
#else
            Logger.Warning("Built without generated SimRail IL2CPP references; telemetry collector is unavailable.");
#endif

            Logger.Msg($"{PluginName} loaded successfully!");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to load {PluginName}: {ex}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        Logger.Msg($"{PluginName} unloading...");
        WebSocketServer?.Stop();
        TelemetryState.ClearSnapshot();
    }

    public override void OnUpdate()
    {
#if SIMRAIL_IL2CPP
        _telemetryCollector.Update();
#endif
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
#if SIMRAIL_IL2CPP
        _telemetryCollector.Invalidate($"scene loaded: {sceneName} ({buildIndex})");
#endif
        Logger.Msg($"Scene loaded: {sceneName} ({buildIndex})");
    }

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
#if SIMRAIL_IL2CPP
        _telemetryCollector.Invalidate($"scene unloaded: {sceneName} ({buildIndex})");
#endif
        Logger.Msg($"Scene unloaded: {sceneName} ({buildIndex})");
    }
}
