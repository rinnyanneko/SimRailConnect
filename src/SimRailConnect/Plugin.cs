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
using HarmonyLib;
using MelonLoader;

// MelonLoader mod registration — must be at assembly scope (outside any namespace).
[assembly: MelonInfo(typeof(SimRailConnect.Plugin), SimRailConnect.Plugin.PluginName, SimRailConnect.Plugin.PluginVersion, "rinnyanneko")]
[assembly: MelonGame]

namespace SimRailConnect;

public class Plugin : MelonMod
{
    public const string PluginName = "SimRailConnect";
    public const string PluginVersion = "1.0.0";

    /// <summary>
    /// Per-mod logger instance.  Assigned once in <see cref="OnInitializeMelon"/>
    /// so that <c>GameBridge</c>, <c>TelemetryMonitor</c> and <c>HttpApiServer</c>
    /// can write structured log entries without importing MelonLoader directly.
    /// </summary>
    internal static MelonLogger.Instance Logger = null!;

    internal static HttpApiServer? ApiServer { get; private set; }

    private HarmonyLib.Harmony? _harmony;

    public override void OnInitializeMelon()
    {
        Logger = base.LoggerInstance;
        Logger.Msg($"{PluginName} v{PluginVersion} loading...");

        try
        {
            // ── Read config (UserData/MelonPreferences.cfg) ───────────────────
            var category = MelonPreferences.CreateCategory("SimRailConnect");

            var port = category.CreateEntry(
                "Port", 5555,
                "HTTP API server port");

            var updateInterval = category.CreateEntry(
                "UpdateIntervalMs", 100,
                "Telemetry update interval in milliseconds");

            var enableWrite = category.CreateEntry(
                "EnableWriteApi", true,
                "Allow external programs to modify game state via POST /api/write");

            TelemetryState.UpdateIntervalMs = updateInterval.Value;

            // ── Start HTTP API server ─────────────────────────────────────────
            ApiServer = new HttpApiServer(port.Value, enableWrite.Value);
            ApiServer.Start();

            Logger.Msg($"HTTP API server started on {ApiServer.BoundPrefix}");
            Logger.Msg($"Telemetry update interval: {updateInterval.Value}ms");
            Logger.Msg($"Write API: {(enableWrite.Value ? "ENABLED" : "DISABLED")}");

            // ── Apply Harmony patches ─────────────────────────────────────────
            //
            // We patch Pyscreen.Update() (the game's WASM instrument-panel update
            // loop) instead of using ClassInjector.RegisterTypeInIl2Cpp<T>().
            //
            // Why: ClassInjector installs a GLOBAL IL2CPP hook
            // (Class_GetFieldDefaultValue_Hook) that intercepts field-default-value
            // queries for ALL classes during every scene load.  On specific scene
            // transitions in SimRail (train approaching the player, missions that
            // spawn the player outside the cab) the Il2CppFieldInfo* stored by that
            // hook becomes a dangling pointer and the runtime crashes with
            // AccessViolationException — even when the injected class has zero
            // instance fields.  Removing ClassInjector entirely removes the hook
            // and the crash is gone.
            //
            // Patching Pyscreen.Update() gives us:
            //   • A guaranteed main-thread callback every frame while in the cab.
            //   • The live VehiclePyscreenDataSource reference (via __instance.Source).
            //   • No IL2CPP class injection at all → no hook → no crash.
            _harmony = new HarmonyLib.Harmony("com.simrailconnect.api");
            _harmony.PatchAll(typeof(Plugin).Assembly);

            Logger.Msg("Harmony patches applied (Pyscreen.Update hooked)");
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
        ApiServer?.Stop();
        _harmony?.UnpatchSelf();
        GameBridge.InvalidateCache();
    }

    // ── Scene lifecycle ───────────────────────────────────────────────────────
    //
    // When the game loads a scene in which the player spawns on a platform
    // (i.e. outside the cab), the previously-cached VehiclePyscreenDataSource
    // and its sub-objects belong to the scene being destroyed.  If a train
    // happens to approach at that moment, Pyscreen.Update() fires on the
    // incoming train before our cache is refreshed, and accessing the stale
    // sub-cache pointers causes an AccessViolationException.
    //
    // Invalidating the cache in OnSceneWasUnloaded guarantees that all dangling
    // native-object references are cleared before any new Pyscreen.Update() tick
    // can run, because MelonLoader calls this callback from the Unity main thread
    // during the scene-unload sequence — before new objects are spawned.

    // Terrain-chunk scenes (world streamer tiles) fire the same callbacks as
    // major scenes but are irrelevant for telemetry.  We still call
    // InvalidateCache() on unload (safety), but suppress log noise.
    private static bool IsTerrainChunk(string sceneName) =>
        sceneName.Contains("_terrain_");

    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        // Terrain-chunk scenes are pure geometry tiles — they never own
        // VehiclePyscreenDataSource or any train objects. Calling InvalidateCache()
        // on every tile swap would reset _dataFieldOffset every ~1 second while
        // driving, forcing a new Marshal.ReadIntPtr probe loop every tick and
        // eventually triggering an AccessViolationException on a garbage pointer.
        if (IsTerrainChunk(sceneName)) return;
        Logger.Msg($"[Scene] Unloaded '{sceneName}' (index={buildIndex}) — invalidating telemetry cache");
        GameBridge.InvalidateCache();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        if (!IsTerrainChunk(sceneName))
            Logger.Msg($"[Scene] Loaded '{sceneName}' (index={buildIndex})");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (!IsTerrainChunk(sceneName))
            Logger.Msg($"[Scene] Initialized '{sceneName}' (index={buildIndex}) — ready for telemetry");
    }
}
