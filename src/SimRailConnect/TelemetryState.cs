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

namespace SimRailConnect;

/// <summary>
/// Thread-safe shared state for the telemetry pipeline.
/// Plain static class — never registered with IL2CPP.  All telemetry driving
/// is now done via a Harmony postfix on <c>Pyscreen.Update()</c>; there is no
/// ClassInjector usage and therefore no <c>Class_GetFieldDefaultValue_Hook</c>.
/// </summary>
internal static class TelemetryState
{
    /// <summary>
    /// Telemetry poll interval in milliseconds.
    /// Written once by <see cref="Plugin"/> at startup; read by the
    /// <see cref="TelemetryMonitor"/> Harmony patch on every <c>Pyscreen.Update</c> tick.
    /// </summary>
    public static int UpdateIntervalMs = 100;

    /// <summary>
    /// Unity time (seconds) at which the next telemetry snapshot should be taken.
    /// Read and written by the <see cref="TelemetryMonitor"/> Harmony postfix
    /// (Unity main thread) to rate-limit collections to <see cref="UpdateIntervalMs"/>.
    /// </summary>
    public static float NextUpdate = 0f;

    /// <summary>
    /// Latest telemetry snapshot.
    /// <para>
    /// Thread-safety: on .NET 6 (64-bit), object reference reads and writes are
    /// guaranteed to be atomic (ECMA-335 §I.12.6.6).  <c>volatile</c> adds the
    /// necessary acquire/release memory barriers so the HTTP thread always sees
    /// the latest reference written by the Unity main thread — without the
    /// overhead of a <c>lock</c> statement being acquired on the hot update path.
    /// </para>
    /// </summary>
    private static volatile TelemetrySnapshot? _currentSnapshot;

    public static TelemetrySnapshot? CurrentSnapshot
    {
        get => _currentSnapshot;
        set => _currentSnapshot = value;
    }
}
