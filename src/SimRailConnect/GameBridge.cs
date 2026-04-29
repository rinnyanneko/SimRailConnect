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
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;

namespace SimRailConnect;

/// <summary>
/// Bridges between IL2CPP game objects and managed .NET types.
/// All game object access MUST happen on the Unity main thread.
/// </summary>
public static class GameBridge
{
    // ─── Float indices (PyscreenGeneralFloat) ────────────────────────────────
    private const int F_LIGHT_LEVEL = 0;
    private const int F_RADIO_VOLUME = 1;
    private const int F_VELOCITY = 2;
    private const int F_EIMP_T_PD = 3;
    private const int F_EIMP_T_ITOTHV = 4;
    private const int F_SPEEDCTRL = 5;
    private const int F_SPEEDCTRLPOWER = 6;
    private const int F_VOLTAGE = 7;
    private const int F_NEW_SPEED = 8;
    private const int F_PANTPRESS = 9;
    private const int F_DISTANCE_COUNTER = 10;
    private const int F_TRAIN_LENGTH = 11;
    private const int F_DISTANCE_DRIVEN = 12;
    private const int F_PNEUMATIC_BRAKE_STATUS = 13;
    private const int F_LV_STATUS = 14;
    private const int F_PANTOGRAPH_COMPRESSOR = 15;
    private const int F_EIMP_T_TRACTIONFORCE = 16;
    private const int F_COMBUSTION_RPM = 17;
    private const int F_COMBUSTION_COOLANT = 18;
    private const int F_EIMP_T_FD = 19;
    private const int F_EIMP_T_TRACTIONPERCENT = 20;

    // ─── Bool indices (PyscreenGeneralBool) ──────────────────────────────────
    private const int B_SHP = 0;
    private const int B_CA = 1;
    private const int B_BATTERY = 2;
    private const int B_CONVERTER = 3;
    private const int B_SANDING = 4;
    private const int B_SPEEDCTRLACTIVE = 5;
    private const int B_SPEEDCTRLSTANDBY = 6;
    private const int B_RADIO_ACTIVE = 7;
    private const int B_MAIN_READY = 8;
    private const int B_LIGHTS_COMPARTMENTS = 9;
    private const int B_FIRE_DETECTION = 10;
    private const int B_ALARM_ACTIVE = 11;
    private const int B_AC_STATUS = 12;
    private const int B_DIESEL_MODE = 13;
    private const int B_RADIO_NOISE = 14;
    private const int B_RADIO_NIGHTMODE = 15;
    private const int B_RADIO_VOLUMEMODE = 16;

    // ─── Int indices (PyscreenGeneralInt) ────────────────────────────────────
    private const int I_SECONDS = 0;
    private const int I_MINUTES = 1;
    private const int I_HOURS = 2;
    private const int I_RADIO_CHANNEL = 3;
    private const int I_TRAIN_STATIONCOUNT = 4;
    private const int I_UNIT_NO = 5;
    private const int I_CAR_NO = 6;
    private const int I_CAB = 7;
    private const int I_LIGHTS_FRONT = 8;
    private const int I_LIGHTS_REAR = 9;
    private const int I_DIRECTION = 10;
    private const int I_MAINCTRL_POS = 11;
    private const int I_MAINCTRL_ACTUAL = 12;
    private const int I_DAY = 26;
    private const int I_MONTH = 27;
    private const int I_YEAR = 28;
    private const int I_ABS_STATUS = 29;
    private const int I_SOLO_DRIVE = 30;
    private const int I_SILENT_MODE = 31;
    private const int I_HVAC_ACTIVE = 33;
    private const int I_ED_BRAKE = 34;
    private const int I_BRAKE_DELAY = 35;
    private const int I_SPEEDCTRL_STATUS = 37;
    private const int I_CABIN_DIR = 38;
    private const int I_SAG_STATUS = 39;
    private const int I_COMBUSTION_ACTIVE = 40;
    private const int I_DAY_OF_WEEK = 41;
    private const int I_SCREEN_BRIGHTNESS = 42;

    // ─── EIMPPN pressure indices ─────────────────────────────────────────────
    private const int P_BC = 0;
    private const int P_BP = 1;
    private const int P_SP = 2;
    private const int P_CP = 3;

    // ─── Brake bool indices ───────────────────────────────────────────────────
    private const int BR_SPRING_ACTIVE = 0;
    private const int BR_SPRING_SHUTOFF = 1;

    // ─── EMU bool indices ─────────────────────────────────────────────────────
    private const int E_DOORS = 0;
    private const int E_DOORS_NO = 1;
    private const int E_DOORS_L = 2;
    private const int E_DOORS_R = 3;
    private const int E_DOORSTEP_L = 4;
    private const int E_DOORSTEP_R = 5;
    private const int E_SLIP = 6;
    private const int E_BRAKES = 7;

    // ─── Cached references ────────────────────────────────────────────────────
    //
    // Data is read directly from each typed source object's data[0, fieldIndex]
    // 2D array — NOT from the flat floatValues/intValues/boolValues arrays on
    // PyscreenDataSource, which are only populated when the in-cab Pyscreen
    // display is actively rendered.
    //
    // Class hierarchy (confirmed from decompiled source):
    //   PyscreenDataSource (MonoBehaviour) — holds flat arrays + source lists
    //   VehiclePyscreenDataSource : PyscreenDataSource — aggregator, holds refs
    //   PyscreenGeneralFloat : PyscreenIOClassBase<double> — NOT PyscreenDataSource
    //   PyscreenEIMPPN       : PyscreenIOClassBase<double> — NOT PyscreenDataSource
    //   PyscreenGeneralInt   : PyscreenIOClassBase<int>    — NOT PyscreenDataSource
    //   PyscreenGeneralBool  : PyscreenIOClassBase<bool>   — NOT PyscreenDataSource
    //   PyscreenBrakes       : PyscreenIOClassBase<bool>   — NOT PyscreenDataSource
    //   PyscreenEMU          : PyscreenIOClassBase<bool>   — NOT PyscreenDataSource
    //
    // The live VehiclePyscreenDataSource reference is injected by the Harmony
    // patch in TelemetryMonitor (Pyscreen.Update postfix) via SetDataSource().
    // FindObjectOfType is not used for cache acquisition; scene-wide scans during
    // streamed scenario transitions can hand us wrappers with fragile lifetimes.

    private static VehiclePyscreenDataSource? _cachedDataSource;
    private static PyscreenGeneralFloat? _cachedGf;   // generalFloat  — double data
    private static PyscreenGeneralInt? _cachedGi;   // generalInt    — int data
    private static PyscreenGeneralBool? _cachedGb;   // generalBool   — bool data
    private static PyscreenEIMPPN? _cachedPn;   // eimppn        — pressure doubles
    private static PyscreenBrakes? _cachedBr;   // brakes        — brake bools
    private static PyscreenEMU? _cachedEm;   // emu           — EMU bools
    private static NextStationPanel? _cachedNsp;  // NextStationPanel for station info

    // Native pointer of the cached data source.  Compared in SetDataSource instead
    // of managed reference equality, because Il2CppInterop's TryCast<T>() allocates
    // a new managed wrapper object on every call even for the same native instance.
    private static IntPtr _cachedDataSourcePtr = IntPtr.Zero;

    // ─── Pending main-thread operations ──────────────────────────────────────
    //
    // All three of these are produced by WebSocket/network threads and consumed
    // by the Unity main thread inside DrainPendingMainThreadOps(), which is called
    // from TelemetryMonitor.Postfix every tick.
    //
    // This ensures that every native-memory access (Marshal.ReadIntPtr, WriteInt64,
    // DescribeArrayCache, etc.) happens exclusively on the Unity main thread and
    // therefore cannot race with IL2CPP's own TransferData() call or with
    // OnSceneWasUnloaded().

    /// <summary>
    /// Write operations submitted via WebSocket command messages, to be applied on the main thread.
    /// </summary>
    private static readonly ConcurrentQueue<QueuedWriteCommand> _pendingWrites = new();

    /// <summary>
    /// Applied write results built on the Unity main thread and consumed by
    /// WebSocket/network threads. Result objects contain only managed data.
    /// </summary>
    private static readonly ConcurrentQueue<CommandResult> _completedWrites = new();

    /// <summary>
    /// Set by WebSocket invalidate requests to request a cache wipe on the next tick.
    /// </summary>
    private static volatile bool _pendingInvalidate;

    /// <summary>
    /// Set by WebSocket debug requests to request a debug snapshot on the next tick.
    /// Cleared and replaced by <see cref="_latestDebugSnapshot"/> by the main thread.
    /// </summary>
    private static volatile bool _debugRequested;

    /// <summary>
    /// Stores the last debug snapshot built on the main thread for retrieval by the
    /// WebSocket thread via <see cref="GetDebugSnapshot"/>.
    /// </summary>
    private static volatile object? _latestDebugSnapshot;

    private static double _lastVelocityKmh;
    private static DateTime _lastVelocitySampleUtc;
    private static bool _hasLastVelocitySample;

    // ─── Data source acquisition ──────────────────────────────────────────────

    /// <summary>
    /// Called by the <see cref="TelemetryMonitor"/> Harmony patch with the live
    /// <see cref="VehiclePyscreenDataSource"/> obtained from <c>Pyscreen.__instance.Source</c>.
    /// Populates sub-object caches when the source changes; no-ops on repeat calls
    /// with the same native instance so the hot path pays only a pointer comparison.
    /// Must be called on the Unity main thread.
    /// </summary>
    public static void SetDataSource(VehiclePyscreenDataSource ds)
    {
        // Use native pointer equality — managed wrapper == would always be false
        // because TryCast<T>() allocates a fresh wrapper object on every invocation.
        var nativePtr = IL2CPP.Il2CppObjectBaseToPtr(ds);
        if (nativePtr != IntPtr.Zero && nativePtr == _cachedDataSourcePtr) return;
        _cachedDataSourcePtr = nativePtr;
        _cachedDataSource = ds;
        PopulateSubCache(ds);
        Plugin.Logger.Msg("[GameBridge] Data source set via Harmony patch");
    }

    /// <summary>
    /// Caches the typed sub-object references from <paramref name="ds"/> so
    /// that individual Read calls can access <c>data[0, index]</c> directly
    /// without any Cast calls at runtime.
    /// </summary>
    private static void PopulateSubCache(VehiclePyscreenDataSource ds)
    {
        try { _cachedGf = ds.generalFloat; } catch { _cachedGf = null; }
        try { _cachedGi = ds.generalInt; } catch { _cachedGi = null; }
        try { _cachedGb = ds.generalBool; } catch { _cachedGb = null; }
        try { _cachedPn = ds.eimppn; } catch { _cachedPn = null; }
        try { _cachedBr = ds.brakes; } catch { _cachedBr = null; }
        try { _cachedEm = ds.emu; } catch { _cachedEm = null; }
        // NextStationPanel is separate and has no stable owner reference here.
        // Leave it uncached instead of scanning the scene during train streaming.
        _cachedNsp = null;

        // Do not probe native field offsets during scene/scenario startup.
        // The confirmed layout offset is used directly; debug requests can inspect
        // array reachability later, after the scene is stable and only on the main thread.
        Plugin.Logger.Msg(
            $"[GameBridge] Sub-cache set (offset={_dataFieldOffset}): " +
            $"gf={_cachedGf != null}, gi={_cachedGi != null}, gb={_cachedGb != null}, " +
            $"pn={_cachedPn != null}, br={_cachedBr != null}, em={_cachedEm != null}, " +
            $"nsp={_cachedNsp != null}");
    }

    // ─── Telemetry collection ─────────────────────────────────────────────────

    /// <summary>
    /// Collect a full telemetry snapshot from the game.
    /// Must be called on the Unity main thread.
    /// </summary>
    public static TelemetrySnapshot CollectTelemetry()
    {
        var snapshot = new TelemetrySnapshot { Timestamp = DateTime.UtcNow };

        if (_cachedDataSource == null)
        {
            snapshot.IsActive = false;
            return snapshot;
        }

        snapshot.IsActive = true;

        try
        {
            snapshot.Train = CollectTrainInfo();
            snapshot.Brakes = CollectBrakeInfo();
            snapshot.Electrical = CollectElectricalInfo();
            snapshot.Safety = CollectSafetyInfo();
            snapshot.Doors = CollectDoorInfo();
            snapshot.Controls = CollectControlInfo();
            snapshot.Station = CollectStationInfo();
            snapshot.Environment = CollectEnvironmentInfo();
        }
        catch (Exception ex)
        {
            Plugin.Logger.Warning($"CollectTelemetry partial failure: {ex}");
        }

        return snapshot;
    }

    // ─── Sub-collectors ───────────────────────────────────────────────────────

    private static TrainInfo CollectTrainInfo()
    {
        var info = new TrainInfo();
        try
        {
            info.Velocity = ReadFloat(F_VELOCITY);
            info.NewSpeed = ReadFloat(F_NEW_SPEED);
            info.DistanceDriven = ReadFloat(F_DISTANCE_DRIVEN);
            info.DistanceCounter = ReadFloat(F_DISTANCE_COUNTER);
            info.TrainLength = ReadFloat(F_TRAIN_LENGTH);
            info.Direction = ReadInt(I_DIRECTION);
            info.CabinDirection = ReadInt(I_CABIN_DIR);
            info.Cab = ReadInt(I_CAB);
            info.UnitNo = ReadInt(I_UNIT_NO);
            info.CarNo = ReadInt(I_CAR_NO);
            info.VelocityInt = (int)Math.Round(info.Velocity);

            var now = DateTime.UtcNow;
            if (_hasLastVelocitySample)
            {
                var elapsed = (now - _lastVelocitySampleUtc).TotalSeconds;
                if (elapsed > 0.001 && elapsed < 5)
                    info.Acceleration = ((info.Velocity - _lastVelocityKmh) / 3.6) / elapsed;
            }
            _lastVelocityKmh = info.Velocity;
            _lastVelocitySampleUtc = now;
            _hasLastVelocitySample = true;
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectTrainInfo: {ex.Message}"); }
        return info;
    }

    private static BrakeInfo CollectBrakeInfo()
    {
        var info = new BrakeInfo();
        try
        {
            info.BC = ReadPressure(P_BC);
            info.BP = ReadPressure(P_BP);
            info.SP = ReadPressure(P_SP);
            info.CP = ReadPressure(P_CP);
            info.PneumaticBrakeStatus = ReadFloat(F_PNEUMATIC_BRAKE_STATUS);
            info.SpringActive = ReadBrake(BR_SPRING_ACTIVE);
            info.SpringShutoff = ReadBrake(BR_SPRING_SHUTOFF);
            info.EdBrakeActive = ReadInt(I_ED_BRAKE);
            info.BrakeDelayFlag = ReadInt(I_BRAKE_DELAY);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectBrakeInfo: {ex.Message}"); }
        return info;
    }

    private static ElectricalInfo CollectElectricalInfo()
    {
        var info = new ElectricalInfo();
        try
        {
            info.Voltage = ReadFloat(F_VOLTAGE);
            info.TractionForce = ReadFloat(F_EIMP_T_TRACTIONFORCE);
            info.TractionPercent = ReadFloat(F_EIMP_T_TRACTIONPERCENT);
            info.MotorFrequency = ReadFloat(F_EIMP_T_FD);
            info.PowerConsumption = ReadFloat(F_EIMP_T_PD);
            info.CurrentVoltageRatio = ReadFloat(F_EIMP_T_ITOTHV);
            info.PantographPressure = ReadFloat(F_PANTPRESS);
            info.LowVoltageStatus = ReadFloat(F_LV_STATUS);
            info.PantographCompressorStatus = ReadFloat(F_PANTOGRAPH_COMPRESSOR);
            info.Converter = ReadBool(B_CONVERTER);
            info.AcStatus = ReadBool(B_AC_STATUS);
            info.Battery = ReadBool(B_BATTERY);
            info.CombustionEngineActive = ReadInt(I_COMBUSTION_ACTIVE);
            info.CombustionEngineRPM = ReadFloat(F_COMBUSTION_RPM);
            info.CoolantTemperature = ReadFloat(F_COMBUSTION_COOLANT);
            info.DieselMode = ReadBool(B_DIESEL_MODE);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectElectricalInfo: {ex.Message}"); }
        return info;
    }

    private static SafetyInfo CollectSafetyInfo()
    {
        var info = new SafetyInfo();
        try
        {
            info.SHP = ReadBool(B_SHP);
            info.CA = ReadBool(B_CA);
            info.AlarmActive = ReadBool(B_ALARM_ACTIVE);
            info.FireDetectionActive = ReadBool(B_FIRE_DETECTION);
            info.AbsStatus = ReadInt(I_ABS_STATUS);
            info.SagStatus = ReadInt(I_SAG_STATUS);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectSafetyInfo: {ex.Message}"); }
        return info;
    }

    private static DoorInfo CollectDoorInfo()
    {
        var info = new DoorInfo();
        try
        {
            info.Doors = ReadEmu(E_DOORS);
            info.DoorsNo = ReadEmu(E_DOORS_NO);
            info.DoorsLeft = ReadEmu(E_DOORS_L);
            info.DoorsRight = ReadEmu(E_DOORS_R);
            info.DoorstepLeft = ReadEmu(E_DOORSTEP_L);
            info.DoorstepRight = ReadEmu(E_DOORSTEP_R);
            info.Slip = ReadEmu(E_SLIP);
            info.BrakesEngaged = ReadEmu(E_BRAKES);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectDoorInfo: {ex.Message}"); }
        return info;
    }

    private static ControlInfo CollectControlInfo()
    {
        var info = new ControlInfo();
        try
        {
            info.MainCtrlPos = ReadInt(I_MAINCTRL_POS);
            info.MainCtrlActualPos = ReadInt(I_MAINCTRL_ACTUAL);
            info.SpeedCtrl = ReadFloat(F_SPEEDCTRL);
            info.SpeedCtrlPower = ReadFloat(F_SPEEDCTRLPOWER);
            info.SpeedCtrlActive = ReadBool(B_SPEEDCTRLACTIVE);
            info.SpeedCtrlStandby = ReadBool(B_SPEEDCTRLSTANDBY);
            info.SpeedCtrlStatus = ReadInt(I_SPEEDCTRL_STATUS);
            info.Sanding = ReadBool(B_SANDING);
            info.SoloDriveActive = ReadInt(I_SOLO_DRIVE);
            info.SilentModeActive = ReadInt(I_SILENT_MODE);
            info.HvacActive = ReadInt(I_HVAC_ACTIVE);
            info.LightsFront = ReadInt(I_LIGHTS_FRONT);
            info.LightsRear = ReadInt(I_LIGHTS_REAR);
            info.LightsCompartments = ReadBool(B_LIGHTS_COMPARTMENTS);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectControlInfo: {ex.Message}"); }
        return info;
    }

    private static StationInfo CollectStationInfo()
    {
        var info = new StationInfo();
        try
        {
            info.StationCount = ReadInt(I_TRAIN_STATIONCOUNT);

            var panel = _cachedNsp;
            if (panel != null)
            {
                // m_NextStation and m_Distance are private TextMeshProUGUI fields.
                // Access via the generated IL2CPP interop properties.
                try { info.NextStation = panel.m_NextStation?.text; } catch { }
                try { info.Distance = panel.m_Distance?.text; } catch { }
            }
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectStationInfo: {ex.Message}"); }
        return info;
    }

    private static EnvironmentInfo CollectEnvironmentInfo()
    {
        var info = new EnvironmentInfo();
        try
        {
            info.Hours = ReadInt(I_HOURS);
            info.Minutes = ReadInt(I_MINUTES);
            info.Seconds = ReadInt(I_SECONDS);
            info.Day = ReadInt(I_DAY);
            info.Month = ReadInt(I_MONTH);
            info.Year = ReadInt(I_YEAR);
            info.DayOfWeek = ReadInt(I_DAY_OF_WEEK);
            info.LightLevel = ReadFloat(F_LIGHT_LEVEL);
            info.RadioActive = ReadBool(B_RADIO_ACTIVE);
            info.RadioChannel = ReadInt(I_RADIO_CHANNEL);
            info.RadioVolume = ReadFloat(F_RADIO_VOLUME);
            info.RadioNoise = ReadBool(B_RADIO_NOISE);
            info.RadioNightMode = ReadBool(B_RADIO_NIGHTMODE);
            info.RadioVolumeMode = ReadBool(B_RADIO_VOLUMEMODE);
            info.ScreenBrightness = ReadInt(I_SCREEN_BRIGHTNESS);
        }
        catch (Exception ex) { Plugin.Logger.Msg($"CollectEnvironmentInfo: {ex.Message}"); }
        return info;
    }

    // ─── Low-level array readers ──────────────────────────────────────────────
    //
    // All data is read directly from the typed source objects' data[0, index]
    // 2D arrays — these are populated live by the simulation engine regardless
    // of whether the Pyscreen instrument panel display is currently active.
    //
    // The flat floatValues/intValues/boolValues arrays on PyscreenDataSource are
    // only updated when Pyscreen.Update() calls TransferData() — i.e., only when
    // the in-cab screen is rendered.  Reading from the source objects directly is
    // the correct and always-live approach.
    //
    // IMPORTANT — why we do NOT use obj.data (the IL2CPP interop property):
    //   PyscreenIOClassBase<T> is a generic base class.  IL2CPP interop
    //   generator cannot reliably produce a typed property accessor for a generic
    //   base-class field (T[,] data), so the generated property returns null at
    //   runtime even when the native field is non-null.
    //   Instead we read the array pointer directly from native memory using the
    //   field's confirmed byte offset within the object (DataFieldOffset = 40).
    //
    // PyscreenIOClassBase<T> native object layout (64-bit, from il2cpp.h):
    //   offset  0: klass pointer   (8 bytes)
    //   offset  8: monitor pointer (8 bytes)
    //   offset 16: prefix ptr      (8 bytes)   ← first instance field
    //   offset 24: postfix ptr     (8 bytes)
    //   offset 32: instances int32 (4 bytes)
    //   offset 36: frontName bool  (1 byte) + 3 bytes padding
    //   offset 40: data array ptr  (8 bytes)   ← DataFieldOffset
    //   offset 48: dataType ptr    (8 bytes)
    //
    // IL2CPP array object layout (64-bit):
    //   offset  0: klass pointer   (8 bytes)
    //   offset  8: monitor pointer (8 bytes)
    //   offset 16: bounds pointer  (8 bytes)   — Il2CppArrayBounds*, non-null for rank≥2
    //   offset 24: max_length      (8 bytes)   — total element count (instances × fields)
    //   offset 32: element data    (sizeof(T) × max_length bytes, row-major)
    //
    // For data[0, fieldIndex] the flat index is simply fieldIndex (instance = 0).
    // Byte offset of element = ArrayDataOffset + sizeof(T) × fieldIndex.

    /// <summary>
    /// Confirmed byte offset of <c>T[,] data</c> within <c>PyscreenIOClassBase&lt;T&gt;</c>.
    /// Kept fixed to avoid native pointer probing during scene/scenario startup.
    /// <para>
    /// Expected values: 40 (if MSVC applies EBO to the empty aligned base class) or
    /// 48 (if EBO is suppressed, adding 8 bytes for <c>PyscreenIOWrapper_Fields</c>).
    /// </para>
    /// </summary>
    private const int DataFieldOffset = 40;
    private static int _dataFieldOffset = DataFieldOffset;

    /// <summary>Byte offset of the element data block in any IL2CPP array object (64-bit).</summary>
    private const int ArrayDataOffset = 32;
    /// <summary>Byte offset of the max_length field in any IL2CPP array object (64-bit).</summary>
    private const int ArrayMaxLenOffset = 24;

    /// <summary>
    /// Returns true when <paramref name="ptr"/> looks like a plausible IL2CPP heap pointer:
    /// above the low-address guard region, 8-byte aligned, and below the kernel-space boundary.
    /// This is a fast heuristic — it does NOT guarantee the memory is accessible.
    /// Suitable for both array pointers and IL2CPP object pointers.
    /// </summary>
    internal static bool IsPlausibleArrayPointer(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return false;
        var addr = ptr.ToInt64();
        // IL2CPP objects live in user-space (< 0x8000_0000_0000_0000) and are
        // always 8-byte aligned. Addresses below 64 KB belong to the null-guard region.
        return addr > 0x10000L
            && (addr & 7) == 0
            && addr < unchecked((long)0x8000_0000_0000_0000L);
    }

    /// <summary>
    /// Reads the native pointer to <c>T[,] data</c> directly from the IL2CPP object at
    /// <see cref="_dataFieldOffset"/>, bypassing the IL2CPP interop property accessor which
    /// returns null for fields defined on generic base classes.
    /// Returns <see cref="IntPtr.Zero"/> if the offset has not been detected yet or the
    /// resulting pointer fails the heap plausibility check.
    /// </summary>
    private static IntPtr GetDataArrayPtr(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? obj)
    {
        if (obj == null || _dataFieldOffset < 0) return IntPtr.Zero;
        var objPtr = IL2CPP.Il2CppObjectBaseToPtr(obj);
        if (objPtr == IntPtr.Zero) return IntPtr.Zero;
        // Validate the object pointer before dereferencing it.  A garbage non-null
        // pointer (e.g. from an uninitialized or partially freed IL2CPP object) that
        // passes a null check but points to unmapped memory would cause an
        // uncatchable AccessViolationException on the Marshal.ReadIntPtr below.
        if (!IsPlausibleArrayPointer(objPtr)) return IntPtr.Zero;
        var arrPtr = Marshal.ReadIntPtr(objPtr, _dataFieldOffset);
        return IsPlausibleArrayPointer(arrPtr) ? arrPtr : IntPtr.Zero;
    }

    private static double ReadFloat(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedGf);
            if (arr == IntPtr.Zero) return 0;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return 0;
            return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(arr, ArrayDataOffset + 8 * index));
        }
        catch { }
        return 0;
    }

    private static int ReadInt(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedGi);
            if (arr == IntPtr.Zero) return 0;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return 0;
            return Marshal.ReadInt32(arr, ArrayDataOffset + 4 * index);
        }
        catch { }
        return 0;
    }

    private static bool ReadBool(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedGb);
            if (arr == IntPtr.Zero) return false;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return false;
            return Marshal.ReadByte(arr, ArrayDataOffset + index) != 0;
        }
        catch { }
        return false;
    }

    private static double ReadPressure(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedPn);
            if (arr == IntPtr.Zero) return 0;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return 0;
            return BitConverter.Int64BitsToDouble(Marshal.ReadInt64(arr, ArrayDataOffset + 8 * index));
        }
        catch { }
        return 0;
    }

    private static bool ReadBrake(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedBr);
            if (arr == IntPtr.Zero) return false;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return false;
            return Marshal.ReadByte(arr, ArrayDataOffset + index) != 0;
        }
        catch { }
        return false;
    }

    private static bool ReadEmu(int index)
    {
        try
        {
            var arr = GetDataArrayPtr(_cachedEm);
            if (arr == IntPtr.Zero) return false;
            var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            if (index >= maxLen) return false;
            return Marshal.ReadByte(arr, ArrayDataOffset + index) != 0;
        }
        catch { }
        return false;
    }

    // ─── Write support ────────────────────────────────────────────────────────
    //
    // Write methods are safe to call from any thread.  They enqueue a closure that
    // is executed on the Unity main thread by DrainPendingMainThreadOps(), which runs
    // inside TelemetryMonitor.Postfix.  This guarantees all Marshal.Write* calls
    // happen on the same thread as Marshal.Read* and IL2CPP's own TransferData().
    //
    // WARNING: Writing directly to Pyscreen arrays changes dashboard DISPLAY only,
    // not the actual simulation state. To modify the simulation you need to access
    // the locomotive controller or apply a Harmony patch.
    //
    // This write API is provided as a framework for experimentation.

    /// <summary>
    /// Enqueues a validated write command for application on the Unity main thread.
    /// Network handlers must call this instead of touching native objects directly.
    /// </summary>
    public static void EnqueueWriteCommand(QueuedWriteCommand command)
    {
        _pendingWrites.Enqueue(command);
        Plugin.Logger.Msg($"[GameBridge] Command queued: {command.Id} {command.Target}.{command.Field}");
    }

    /// <summary>
    /// Returns an applied/failed command result produced by the main thread, if any.
    /// Safe to call from network threads.
    /// </summary>
    public static bool TryDequeueCommandResult(out CommandResult result) =>
        _completedWrites.TryDequeue(out result!);

    /// <summary>
    /// Enqueues a float write to the telemetry data source (display only).
    /// Executes on the Unity main thread at the next telemetry tick.
    /// </summary>
    public static void WriteFloat(int index, double value)
    {
        EnqueueWriteCommand(new QueuedWriteCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Target = "float",
            Field = $"index_{index}",
            ValueKind = "float",
            Index = index,
            FloatValue = value
        });
    }

    /// <summary>
    /// Enqueues an int write to the telemetry data source (display only).
    /// Executes on the Unity main thread at the next telemetry tick.
    /// </summary>
    public static void WriteInt(int index, int value)
    {
        EnqueueWriteCommand(new QueuedWriteCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Target = "int",
            Field = $"index_{index}",
            ValueKind = "int",
            Index = index,
            IntValue = value
        });
    }

    /// <summary>
    /// Enqueues a bool write to the telemetry data source (display only).
    /// Executes on the Unity main thread at the next telemetry tick.
    /// </summary>
    public static void WriteBool(int index, bool value)
    {
        EnqueueWriteCommand(new QueuedWriteCommand
        {
            Id = Guid.NewGuid().ToString("N"),
            Target = "bool",
            Field = $"index_{index}",
            ValueKind = "bool",
            Index = index,
            BoolValue = value
        });
    }

    private static CommandResult ApplyWriteCommand(QueuedWriteCommand command)
    {
        try
        {
            if (_cachedDataSource == null)
                return CommandResult.Fail(command, "NO_ACTIVE_SOURCE", "No active data source at apply time");

            var applied = command.ValueKind switch
            {
                "float" => TryWriteFloat(command.Index, command.FloatValue, out var floatError)
                    ? CommandResult.AppliedOk(command)
                    : CommandResult.Fail(command, "APPLY_FAILED", floatError),
                "int" => TryWriteInt(command.Index, command.IntValue, out var intError)
                    ? CommandResult.AppliedOk(command)
                    : CommandResult.Fail(command, "APPLY_FAILED", intError),
                "bool" => TryWriteBool(command.Index, command.BoolValue, out var boolError)
                    ? CommandResult.AppliedOk(command)
                    : CommandResult.Fail(command, "APPLY_FAILED", boolError),
                _ => CommandResult.Fail(command, "UNSUPPORTED_TARGET", $"Unsupported value kind: {command.ValueKind}")
            };

            return applied;
        }
        catch (Exception ex)
        {
            return CommandResult.Fail(command, "APPLY_EXCEPTION", ex.Message);
        }
    }

    private static bool TryWriteFloat(int index, double value, out string error)
    {
        error = "";
        var arr = GetDataArrayPtr(_cachedGf);
        if (arr == IntPtr.Zero)
        {
            error = "Float array unavailable";
            return false;
        }
        var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
        if (index < 0 || index >= maxLen)
        {
            error = $"Float index {index} out of range";
            return false;
        }
        Marshal.WriteInt64(arr, ArrayDataOffset + 8 * index, BitConverter.DoubleToInt64Bits(value));
        return true;
    }

    private static bool TryWriteInt(int index, int value, out string error)
    {
        error = "";
        var arr = GetDataArrayPtr(_cachedGi);
        if (arr == IntPtr.Zero)
        {
            error = "Int array unavailable";
            return false;
        }
        var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
        if (index < 0 || index >= maxLen)
        {
            error = $"Int index {index} out of range";
            return false;
        }
        Marshal.WriteInt32(arr, ArrayDataOffset + 4 * index, value);
        return true;
    }

    private static bool TryWriteBool(int index, bool value, out string error)
    {
        error = "";
        var arr = GetDataArrayPtr(_cachedGb);
        if (arr == IntPtr.Zero)
        {
            error = "Bool array unavailable";
            return false;
        }
        var maxLen = (long)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
        if (index < 0 || index >= maxLen)
        {
            error = $"Bool index {index} out of range";
            return false;
        }
        Marshal.WriteByte(arr, ArrayDataOffset + index, (byte)(value ? 1 : 0));
        return true;
    }

    // ─── Diagnostics ──────────────────────────────────────────────────────────

    /// <summary>
    /// Describes the native-memory state of one sub-cache object for the debug endpoint.
    /// </summary>
    /// <param name="obj">The cached IL2CPP object (may be null).</param>
    /// <param name="elemSize">sizeof(T): 8=double, 4=int, 1=bool.</param>
    private static object DescribeArrayCache(
        Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? obj, int elemSize)
    {
        if (obj == null)
            return new { objectFound = false, dataFound = false };

        var objPtr = IL2CPP.Il2CppObjectBaseToPtr(obj);
        if (objPtr == IntPtr.Zero)
            return new { objectFound = true, objPtrZero = true, dataFound = false };

        if (!IsPlausibleArrayPointer(objPtr))
            return new { objectFound = true, objPtrNotPlausible = true, dataFound = false };

        // Local copy so the guard check and the Marshal.ReadIntPtr call use the
        // same value (defensive against future refactors; both callers today are
        // main-thread only, so there is no live race on _dataFieldOffset).
        var offset = _dataFieldOffset;
        var arr = offset >= 0
            ? Marshal.ReadIntPtr(objPtr, offset)
            : IntPtr.Zero;
        if (arr == IntPtr.Zero || !IsPlausibleArrayPointer(arr))
            return new { objectFound = true, dataFound = false };

        try
        {
            var maxLen = (int)Marshal.ReadInt64(arr, ArrayMaxLenOffset);
            var count = Math.Min(maxLen, 8);
            var sample = new object[count];
            for (int i = 0; i < count; i++)
            {
                sample[i] = elemSize == 8
                    ? (object)BitConverter.Int64BitsToDouble(
                          Marshal.ReadInt64(arr, ArrayDataOffset + 8 * i))
                    : elemSize == 4
                        ? (object)Marshal.ReadInt32(arr, ArrayDataOffset + 4 * i)
                        : (object)(Marshal.ReadByte(arr, ArrayDataOffset + i) != 0);
            }
            return new { objectFound = true, dataFound = true, maxLength = maxLen, sample };
        }
        catch (Exception ex)
        {
            return new { objectFound = true, dataFound = true, error = ex.Message };
        }
    }

    /// <summary>
    /// Returns a diagnostic snapshot of the sub-cache state and raw array data.
    /// Used by WebSocket debug requests to verify native field offsets and data presence.
    /// <b>Must be called on the Unity main thread</b> — it invokes
    /// <see cref="DescribeArrayCache"/> which calls <c>Marshal.ReadIntPtr</c>.
    /// Network callers must route through <see cref="RequestDebugSnapshot"/> /
    /// <see cref="GetDebugSnapshot"/> so the read is deferred to the main thread
    /// via <c>DrainPendingMainThreadOps</c>.
    /// </summary>
    public static object CollectDebugInfo()
    {
        // NOTE: must NOT be called directly from a network background thread.
        // DescribeArrayCache calls Marshal.ReadIntPtr which is main-thread only.
        // See DrainPendingMainThreadOps for the main-thread dispatch path.
        return new
        {
            dataSourceFound = _cachedDataSource != null,
            dataFieldOffset = _dataFieldOffset,
            arrayDataOffset = ArrayDataOffset,
            arrayMaxLenOfs = ArrayMaxLenOffset,
            generalFloat = DescribeArrayCache(_cachedGf, 8),
            generalInt = DescribeArrayCache(_cachedGi, 4),
            generalBool = DescribeArrayCache(_cachedGb, 1),
            eimppn = DescribeArrayCache(_cachedPn, 8),
            brakes = DescribeArrayCache(_cachedBr, 1),
            emu = DescribeArrayCache(_cachedEm, 1),
            nextStationPanel = _cachedNsp != null,
        };
    }

    // ─── Cache management ─────────────────────────────────────────────────────

    /// <summary>
    /// Drop all cached game-object references.
    /// <b>Must be called on the Unity main thread</b> (e.g. from
    /// <c>OnSceneWasUnloaded</c> or from <see cref="DrainPendingMainThreadOps"/>).
    /// Network callers should use <see cref="RequestInvalidate"/> instead.
    /// </summary>
    public static void InvalidateCache()
    {
        _cachedDataSource = null;
        _cachedDataSourcePtr = IntPtr.Zero;
        _cachedGf = null;
        _cachedGi = null;
        _cachedGb = null;
        _cachedPn = null;
        _cachedBr = null;
        _cachedEm = null;
        _cachedNsp = null;
        _dataFieldOffset = DataFieldOffset;
    }

    // ─── Network-thread request helpers ──────────────────────────────────────

    /// <summary>
    /// Schedules a cache invalidation to run on the Unity main thread at the
    /// next telemetry tick.  Safe to call from any thread.
    /// </summary>
    public static void RequestInvalidate() => _pendingInvalidate = true;

    /// <summary>
    /// Requests a debug snapshot to be built on the Unity main thread at the
    /// next telemetry tick.  Poll <see cref="GetDebugSnapshot"/> for the result.
    /// Safe to call from any thread.
    /// </summary>
    public static void RequestDebugSnapshot()
    {
        _latestDebugSnapshot = null; // clear stale result before requesting a fresh one
        _debugRequested = true;
    }

    /// <summary>
    /// Returns the last debug snapshot built by the main thread, or <c>null</c>
    /// if no snapshot is available yet (e.g. still waiting for a tick).
    /// Safe to call from any thread.
    /// </summary>
    public static object? GetDebugSnapshot() => _latestDebugSnapshot;

    /// <summary>
    /// Drains all pending main-thread operations (writes, invalidate, debug snapshot).
    /// Must be called on the Unity main thread.  Called by
    /// <see cref="TelemetryMonitor"/> Postfix each tick.
    /// </summary>
    public static void DrainPendingMainThreadOps()
    {
        // 1. Cache invalidation (e.g. from WebSocket invalidate).
        if (_pendingInvalidate)
        {
            _pendingInvalidate = false;
            InvalidateCache();
            Plugin.Logger.Msg("[GameBridge] Cache invalidated via API request");
        }

        // 2. Queued writes (from WebSocket).
        while (_pendingWrites.TryDequeue(out var command))
        {
            var result = ApplyWriteCommand(command);
            _completedWrites.Enqueue(result);
            if (result.Ok)
                Plugin.Logger.Msg($"[GameBridge] Command applied: {command.Id} {command.Target}.{command.Field}");
            else
                Plugin.Logger.Warning(
                    $"[GameBridge] Command failed: {command.Id} {command.Target}.{command.Field} " +
                    $"{result.Code}: {result.Message}");
        }

        // 3. Debug snapshot request (from WebSocket debug).
        if (_debugRequested && _cachedDataSource != null)
        {
            _debugRequested = false;
            _latestDebugSnapshot = CollectDebugInfo();
        }
    }
}
