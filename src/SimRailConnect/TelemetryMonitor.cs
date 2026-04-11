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
using Il2Cpp;
using UnityEngine;

namespace SimRailConnect;

/// <summary>
/// Harmony postfix patch on <c>Pyscreen.Update()</c> that drives the telemetry
/// collection loop instead of using an IL2CPP-injected MonoBehaviour.
///
/// <para>
/// <b>Why Harmony instead of ClassInjector:</b><br/>
/// <c>ClassInjector.RegisterTypeInIl2Cpp&lt;T&gt;()</c> installs a global IL2CPP
/// hook (<c>Class_GetFieldDefaultValue_Hook</c>) that intercepts field-metadata
/// queries for <em>every</em> class during every scene load.  On specific scene
/// transitions in SimRail (train approaching the player, missions that spawn the
/// player outside the cab) the <c>Il2CppFieldInfo*</c> stored by that hook
/// becomes a dangling pointer and the runtime throws an
/// <see cref="System.AccessViolationException"/> — even when the injected class
/// has zero instance fields.  Removing <c>ClassInjector</c> entirely removes the
/// hook, and the crash is gone.
/// </para>
///
/// <para>
/// <b>Why <c>Pyscreen.Update()</c>:</b><br/>
/// <c>Pyscreen.Update()</c> is called by Unity every frame while the player is
/// seated in the cab with the WASM instrument-panel screen active.  It calls
/// <c>PyscreenDataSource.TransferData()</c>, which copies live simulation values
/// from each <c>PyscreenIOClassBase&lt;T&gt;.data</c> source array into the flat
/// display arrays — so by the time our postfix runs, the data arrays are freshly
/// written.  The postfix also receives the live <see cref="Pyscreen"/> instance,
/// giving us a direct reference to its <c>Source</c> field
/// (<see cref="VehiclePyscreenDataSource"/>) without any <c>FindObjectOfType</c>
/// scan on every tick.
/// </para>
/// </summary>
[HarmonyPatch(typeof(Pyscreen), "Update")]
internal static class TelemetryMonitor
{
    /// <summary>
    /// Postfix runs on the Unity main thread immediately after
    /// <c>Pyscreen.Update()</c> finishes (i.e. after <c>TransferData</c> has
    /// refreshed the data source arrays for this frame).
    /// </summary>
    private static void Postfix(Pyscreen __instance)
    {
        // ── Rate limiting ─────────────────────────────────────────────────────
        if (Time.time < TelemetryState.NextUpdate) return;
        TelemetryState.NextUpdate =
            Time.time + (TelemetryState.UpdateIntervalMs / 1000f);

        try
        {
            // ── Refresh the data-source cache ─────────────────────────────────
            //
            // Grab the VehiclePyscreenDataSource directly from the Pyscreen
            // instance that just ran Update() — no FindObjectOfType required.
            // GameBridge.SetDataSource is a no-op when the same instance is
            // passed again (avoids redundant PopulateSubCache calls).
            var source = __instance.Source?.TryCast<VehiclePyscreenDataSource>();
            if (source != null)
                GameBridge.SetDataSource(source);

            // ── Collect telemetry ─────────────────────────────────────────────
            TelemetryState.CurrentSnapshot = GameBridge.CollectTelemetry();

            // ── Drain HTTP-thread requests ────────────────────────────────────
            //
            // Writes, cache invalidations, and debug-snapshot requests are all
            // queued by the HTTP background thread and executed here — on the
            // Unity main thread — to keep every native Marshal.Read/Write call
            // on the same thread as IL2CPP's own TransferData().
            GameBridge.DrainPendingMainThreadOps();
        }
        catch (Exception ex)
        {
            Plugin.Logger.Warning($"[TelemetryMonitor] tick error: {ex}");
        }
    }
}
