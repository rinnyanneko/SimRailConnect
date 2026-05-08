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

public class Plugin : MelonPlugin
{
    public const string PluginName = "SimRailConnect";
    public const string PluginVersion = "1.0.0";

    /// <summary>
    /// Per-plugin logger instance.  Assigned once in <see cref="OnInitializeMelon"/>
    /// so that <c>WebSocketApiServer</c> can write structured log entries
    /// without importing MelonLoader directly.
    /// </summary>
    internal static MelonLogger.Instance Logger = null!;

    internal static WebSocketApiServer? WebSocketServer { get; private set; }

    public override void OnInitializeMelon()
    {
        Logger = base.LoggerInstance;
        Logger.Msg($"{PluginName} v{PluginVersion} loading...");

        try
        {
            var gameBasePath = AppContext.BaseDirectory.TrimEnd(
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

            var enableTelemetryPatch = category.CreateEntry(
                "EnableTelemetryPatch", false,
                "Reserved for a future native telemetry assembly. This managed plugin never patches IL2CPP.");

            var apiToken = category.CreateEntry(
                "ApiToken", "",
                "Optional token required by WebSocket clients; blank disables token auth");

            TelemetryState.UpdateIntervalMs = updateInterval.Value;
            TelemetryState.PublishSnapshot(TelemetrySnapshot.CreateInactive("No telemetry provider has published data yet."));

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
            Logger.Msg("WebSocket API is running. Telemetry stays inactive until a provider publishes snapshots.");

            if (enableTelemetryPatch.Value)
                Logger.Warning("EnableTelemetryPatch is reserved for provider builds and is ignored by this core plugin.");

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

    // MelonPlugin intentionally avoids MelonMod scene callbacks and support
    // components. On this SimRail/MelonLoader build, the IL2CPP support-module
    // field-default hook can crash during WorldStreamerPlayer startup even when
    // our telemetry Harmony patch is disabled.
}
