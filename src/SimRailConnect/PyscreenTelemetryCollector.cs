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
#if SIMRAIL_IL2CPP
using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityObject = UnityEngine.Object;

namespace SimRailConnect;

internal sealed class PyscreenTelemetryCollector
{
    private const long DiscoveryRetryMs = 1000;
    private const int MaxConsecutiveFailures = 5;
    private const int MaxCommandsPerTick = 16;

    private static readonly Dictionary<string, int> EimpcBoolFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ms"] = 0,
        ["heat"] = 1,
        ["batt"] = 2,
        ["battery"] = 2,
        ["conv"] = 3,
        ["converter"] = 3,
        ["comp"] = 4,
        ["compressor"] = 4,
        ["comp_shutoff"] = 5,
        ["compressor_shutoff"] = 5
    };

    private static readonly Dictionary<string, int> EimpcIntFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["motor_isactive"] = 0,
        ["motorIsActive"] = 0
    };

    private static readonly Dictionary<string, int> EimpcFloatFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fr"] = 0,
        ["ihv"] = 1,
        ["uhv"] = 2,
        ["frt"] = 3,
        ["frb"] = 4,
        ["cv"] = 5,
        ["ci"] = 6,
        ["motor_fan_status"] = 7,
        ["motorFanStatus"] = 7,
        ["tcu_status"] = 8,
        ["tcuStatus"] = 8,
        ["motor_temp"] = 9,
        ["motorTemp"] = 9,
        ["pantograph_front_status"] = 10,
        ["pantographFrontStatus"] = 10,
        ["pantograph_rear_status"] = 11,
        ["pantographRearStatus"] = 11
    };

    private VehiclePyscreenDataSource? _source;
    private VehicleControllerBase? _controller;
    private TrainsetInfo? _trainset;
    private long _nextPollAt;
    private long _nextDiscoveryAt;
    private double _lastVelocityKmh;
    private long _lastVelocityTick;
    private int _consecutiveFailures;

    public bool IsEnabled { get; set; }

    public void Update()
    {
        if (!IsEnabled)
            return;

        var now = Environment.TickCount64;
        if (now < _nextPollAt)
            return;

        _nextPollAt = now + Math.Max(50, TelemetryState.UpdateIntervalMs);

        try
        {
            var source = GetSource(now);
            if (source == null)
            {
                TelemetryState.PublishSnapshot(TelemetrySnapshot.CreateInactive("Waiting for VehiclePyscreenDataSource."));
                return;
            }

            if (DrainCommands(source))
                return;

            TelemetryState.PublishSnapshot(CreateSnapshot(source, now));
            _consecutiveFailures = 0;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            Plugin.Logger.Warning($"Pyscreen telemetry read failed ({_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}");

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                Invalidate("too many telemetry read failures");
                TelemetryState.PublishSnapshot(TelemetrySnapshot.CreateInactive("Pyscreen telemetry source disabled after repeated read failures."));
            }
        }
    }

    public void Invalidate(string reason)
    {
        _source = null;
        _controller = null;
        _trainset = null;
        _nextDiscoveryAt = 0;
        _lastVelocityTick = 0;
        _consecutiveFailures = 0;
        Plugin.Logger.Msg($"Telemetry cache invalidated: {reason}");
    }

    private VehiclePyscreenDataSource? GetSource(long now)
    {
        if (IsUsable(_source))
            return _source;

        _source = null;
        if (now < _nextDiscoveryAt)
            return null;

        _nextDiscoveryAt = now + DiscoveryRetryMs;
        Plugin.Logger.Msg("Telemetry discovery started: scanning Pyscreen sources.");

        var screens = UnityObject.FindObjectsOfType<Pyscreen>();
        for (var i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            if (!IsUsable(screen))
                continue;

            var candidate = screen.Source?.TryCast<VehiclePyscreenDataSource>();
            if (!IsUsable(candidate))
                continue;

            _source = candidate;
            _controller = ResolveController(candidate!);
            _trainset = ResolveTrainset(candidate!, _controller);
            Plugin.Logger.Msg("Telemetry discovery completed: VehiclePyscreenDataSource found.");
            return _source;
        }

        Plugin.Logger.Msg("Telemetry discovery completed: no VehiclePyscreenDataSource found.");
        return null;
    }

    private TelemetrySnapshot CreateSnapshot(VehiclePyscreenDataSource source, long now)
    {
        var generalFloat = GetArray<double>(source.generalFloat?.data);
        var pressureFloat = GetArray<double>(source.eimppn?.data);
        var generalInt = GetArray<int>(source.generalInt?.data);
        var generalBool = GetArray<bool>(source.generalBool?.data);
        var brakeBool = GetArray<bool>(source.brakes?.data);
        var emuBool = GetArray<bool>(source.emu?.data);

        var velocity = Read(generalFloat, FloatIndex.Velocity);
        var acceleration = CalculateAcceleration(velocity, now);

        return new TelemetrySnapshot
        {
            IsActive = true,
            Status = "OK",
            Train = new TrainInfo
            {
                Velocity = velocity,
                VelocityInt = (int)Math.Round(velocity),
                Acceleration = acceleration,
                NewSpeed = Read(generalFloat, FloatIndex.NewSpeed),
                DistanceDriven = Read(generalFloat, FloatIndex.DistanceDriven),
                DistanceCounter = Read(generalFloat, FloatIndex.DistanceCounter),
                TrainLength = Read(generalFloat, FloatIndex.TrainLength),
                Direction = Read(generalInt, IntIndex.Direction),
                CabinDirection = Read(generalInt, IntIndex.CabinDirection),
                Cab = Read(generalInt, IntIndex.Cab),
                UnitNo = Read(generalInt, IntIndex.UnitNo),
                CarNo = Read(generalInt, IntIndex.CarNo)
            },
            Brakes = new BrakeInfo
            {
                BC = Read(pressureFloat, PressureIndex.BrakeCylinder),
                BP = Read(pressureFloat, PressureIndex.BrakePipe),
                SP = Read(pressureFloat, PressureIndex.Supply),
                CP = Read(pressureFloat, PressureIndex.Control),
                PneumaticBrakeStatus = Read(generalFloat, FloatIndex.PneumaticBrakeStatus),
                SpringActive = Read(brakeBool, BrakeBoolIndex.SpringActive),
                SpringShutoff = Read(brakeBool, BrakeBoolIndex.SpringShutoff),
                EdBrakeActive = Read(generalInt, IntIndex.EdBrake),
                BrakeDelayFlag = Read(generalInt, IntIndex.BrakeDelay)
            },
            Electrical = new ElectricalInfo
            {
                Voltage = Read(generalFloat, FloatIndex.Voltage),
                TractionForce = Read(generalFloat, FloatIndex.TractionForce),
                TractionPercent = Read(generalFloat, FloatIndex.TractionPercent),
                MotorFrequency = Read(generalFloat, FloatIndex.MotorFrequency),
                PowerConsumption = Read(generalFloat, FloatIndex.PowerConsumption),
                CurrentVoltageRatio = Read(generalFloat, FloatIndex.CurrentVoltageRatio),
                PantographPressure = Read(generalFloat, FloatIndex.PantographPressure),
                LowVoltageStatus = Read(generalFloat, FloatIndex.LowVoltageStatus),
                PantographCompressorStatus = Read(generalFloat, FloatIndex.PantographCompressorStatus),
                Converter = Read(generalBool, BoolIndex.Converter),
                AcStatus = Read(generalBool, BoolIndex.AcStatus),
                Battery = Read(generalBool, BoolIndex.Battery),
                CombustionEngineActive = Read(generalInt, IntIndex.CombustionActive),
                CombustionEngineRPM = Read(generalFloat, FloatIndex.CombustionRpm),
                CoolantTemperature = Read(generalFloat, FloatIndex.CombustionCoolant),
                DieselMode = Read(generalBool, BoolIndex.DieselMode)
            },
            Safety = new SafetyInfo
            {
                SHP = Read(generalBool, BoolIndex.Shp),
                CA = Read(generalBool, BoolIndex.Ca),
                AlarmActive = Read(generalBool, BoolIndex.AlarmActive),
                FireDetectionActive = Read(generalBool, BoolIndex.FireDetection),
                AbsStatus = Read(generalInt, IntIndex.AbsStatus),
                SagStatus = Read(generalInt, IntIndex.SagStatus)
            },
            Doors = new DoorInfo
            {
                Doors = Read(emuBool, EmuBoolIndex.Doors),
                DoorsNo = Read(emuBool, EmuBoolIndex.DoorsNo),
                DoorsLeft = Read(emuBool, EmuBoolIndex.DoorsLeft),
                DoorsRight = Read(emuBool, EmuBoolIndex.DoorsRight),
                DoorstepLeft = Read(emuBool, EmuBoolIndex.DoorstepLeft),
                DoorstepRight = Read(emuBool, EmuBoolIndex.DoorstepRight),
                Slip = Read(emuBool, EmuBoolIndex.Slip),
                BrakesEngaged = Read(emuBool, EmuBoolIndex.Brakes)
            },
            Controls = new ControlInfo
            {
                MainCtrlPos = Read(generalInt, IntIndex.MainCtrlPos),
                MainCtrlActualPos = Read(generalInt, IntIndex.MainCtrlActual),
                SpeedCtrl = Read(generalFloat, FloatIndex.SpeedCtrl),
                SpeedCtrlPower = Read(generalFloat, FloatIndex.SpeedCtrlPower),
                SpeedCtrlActive = Read(generalBool, BoolIndex.SpeedCtrlActive),
                SpeedCtrlStandby = Read(generalBool, BoolIndex.SpeedCtrlStandby),
                SpeedCtrlStatus = Read(generalInt, IntIndex.SpeedCtrlStatus),
                Sanding = Read(generalBool, BoolIndex.Sanding),
                SoloDriveActive = Read(generalInt, IntIndex.SoloDrive),
                SilentModeActive = Read(generalInt, IntIndex.SilentMode),
                HvacActive = Read(generalInt, IntIndex.HvacActive),
                LightsFront = Read(generalInt, IntIndex.LightsFront),
                LightsRear = Read(generalInt, IntIndex.LightsRear),
                LightsCompartments = Read(generalBool, BoolIndex.LightsCompartments)
            },
            Station = new StationInfo
            {
                StationCount = Read(generalInt, IntIndex.TrainStationCount)
            },
            Environment = new EnvironmentInfo
            {
                Hours = Read(generalInt, IntIndex.Hours),
                Minutes = Read(generalInt, IntIndex.Minutes),
                Seconds = Read(generalInt, IntIndex.Seconds),
                Day = Read(generalInt, IntIndex.Day),
                Month = Read(generalInt, IntIndex.Month),
                Year = Read(generalInt, IntIndex.Year),
                DayOfWeek = Read(generalInt, IntIndex.DayOfWeek),
                LightLevel = Read(generalFloat, FloatIndex.LightLevel),
                RadioActive = Read(generalBool, BoolIndex.RadioActive),
                RadioChannel = Read(generalInt, IntIndex.RadioChannel),
                RadioVolume = Read(generalFloat, FloatIndex.RadioVolume),
                RadioNoise = Read(generalBool, BoolIndex.RadioNoise),
                RadioNightMode = Read(generalBool, BoolIndex.RadioNightMode),
                RadioVolumeMode = Read(generalBool, BoolIndex.RadioVolumeMode),
                ScreenBrightness = Read(generalInt, IntIndex.ScreenBrightness)
            }
        };
    }

    private bool DrainCommands(VehiclePyscreenDataSource source)
    {
        var processed = 0;
        while (processed < MaxCommandsPerTick && TelemetryCommandQueue.TryDequeue(out var command))
        {
            if (command == null)
                continue;

            processed++;
            try
            {
                if (command.Kind == TelemetryCommandKind.InvalidateTelemetry)
                {
                    Invalidate(command.Reason);
                    return true;
                }

                if (command.Kind == TelemetryCommandKind.DriverControl)
                    ApplyDriverControl(source, command);
                else
                    ApplyPyscreenWrite(source, command);
            }
            catch (Exception ex)
            {
                Plugin.Logger.Warning($"Command {command.Id} failed: {ex.Message}");
            }
        }

        return false;
    }

    private static void ApplyPyscreenWrite(VehiclePyscreenDataSource source, TelemetryCommand command)
    {
        switch (command.Target)
        {
            case "eimpcBool":
                Write(
                    GetArray<bool>(source.eimpcBool?.data),
                    ResolveIndex(command, EimpcBoolFields),
                    6,
                    command.Instance,
                    command.BoolValue);
                break;

            case "eimpcInt":
                Write(
                    GetArray<int>(source.eimpcInt?.data),
                    ResolveIndex(command, EimpcIntFields),
                    1,
                    command.Instance,
                    (int)Math.Round(command.NumberValue));
                break;

            case "eimpcFloat":
                Write(
                    GetArray<double>(source.eimpcFloat?.data),
                    ResolveIndex(command, EimpcFloatFields),
                    12,
                    command.Instance,
                    command.NumberValue);
                break;

            default:
                throw new InvalidOperationException($"Unsupported command target: {command.Target}");
        }

        Plugin.Logger.Msg(
            $"Command {command.Id} applied: {command.Target}[instance={command.Instance}, field={command.Field ?? command.Index?.ToString() ?? "?"}]");
    }

    private void ApplyDriverControl(VehiclePyscreenDataSource source, TelemetryCommand command)
    {
        var controller = IsUsable(_controller) ? _controller : ResolveController(source);
        var trainset = IsUsable(_trainset) ? _trainset : ResolveTrainset(source, controller);
        _controller = controller;
        _trainset = trainset;

        switch (command.Action)
        {
            case "emergencyBrake":
                ApplyEmergencyBrake(controller, trainset, command.BoolValue);
                WriteInput(controller, GeneralInputIndex.EmergencyBrakeButton, command.BoolValue ? 1f : 0f);
                WriteInput(controller, GeneralInputIndex.EmergencyBrakeButtonFlag, command.BoolValue ? 1f : 0f);
                break;

            case "noPowerAndBrake":
                ApplyNoPowerAndBrake(controller, trainset, true, command.BoolValue);
                break;

            case "setPower":
                var power = Clamp01OrRaw(command.NumberValue, controller?.DrivePercentageHandleMaxValue ?? 1f);
                WriteInput(controller, GeneralInputIndex.DriveHandle, power);
                WriteInput(controller, GeneralInputIndex.DriveHandlePressed, 1f);
                if (controller != null)
                    controller.DrivePercentageHandle = power;
                break;

            case "setBrake":
                WriteInput(controller, GeneralInputIndex.MainBrake, Clamp01(command.NumberValue));
                break;

            case "setLocalBrake":
                WriteInput(controller, GeneralInputIndex.LocalBrake, Clamp01(command.NumberValue));
                break;

            case "setThirdBrake":
                WriteInput(controller, GeneralInputIndex.ThirdBrake, Clamp01(command.NumberValue));
                break;

            case "setEdBrake":
                var edBrake = (int)Math.Round(command.NumberValue);
                if (controller != null)
                    controller.EDBrakeState = edBrake;
                break;

            case "setDirection":
                var direction = (float)Math.Round(command.NumberValue);
                WriteInput(controller, GeneralInputIndex.DirectionRequest, direction);
                if (controller != null)
                    controller.Direction = (int)direction;
                break;

            case "setSpeedTarget":
                WriteInput(controller, GeneralInputIndex.SpeedCtrl, (float)command.NumberValue);
                WriteInput(controller, GeneralInputIndex.SpeedCtrlPressed, 1f);
                break;

            case "securityAcknowledge":
                WriteInput(controller, GeneralInputIndex.SecuritySystemButton, command.BoolValue ? 1f : 0f);
                break;

            case "setSanding":
                WriteInput(controller, GeneralInputIndex.SandingButton, command.BoolValue ? 1f : 0f);
                break;

            case "horn":
                WriteInput(controller, GeneralInputIndex.HornSignal, command.BoolValue ? 1f : 0f);
                break;

            case "radioStop":
                WriteInput(controller, GeneralInputIndex.RadioStop, command.BoolValue ? 1f : 0f);
                WriteInput(controller, GeneralInputIndex.RadioStopRequest, command.BoolValue ? 1f : 0f);
                break;

            case "etcsAck":
                WriteInput(controller, GeneralInputIndex.EtcsAckButton, command.BoolValue ? 1f : 0f);
                break;

            case "setSpringBrake":
                WriteInput(controller, GeneralInputIndex.SpringBrakeRequest, command.BoolValue ? 1f : 0f);
                WriteInput(controller, GeneralInputIndex.SpringBrakeValve, command.BoolValue ? 1f : 0f);
                break;

            case "setVcb":
                WriteInput(controller, GeneralInputIndex.MainSwitch, command.BoolValue ? 1f : 0f);
                ApplyPyscreenWrite(source, ToPyscreenBool(command, "ms"));
                break;

            case "setBattery":
                WriteInput(controller, GeneralInputIndex.BatteryRequest, 0f);
                WriteInput(controller, GeneralInputIndex.BatteryOffRequest, 0f);
                WriteInput(controller, command.BoolValue ? GeneralInputIndex.BatteryRequest : GeneralInputIndex.BatteryOffRequest, 1f);
                ApplyPyscreenWrite(source, ToPyscreenBool(command, "batt"));
                break;

            case "setConverter":
                ApplyPyscreenWrite(source, ToPyscreenBool(command, "conv"));
                break;

            case "setCompressor":
                ApplyPyscreenWrite(source, ToPyscreenBool(command, "comp"));
                break;

            case "setFrontPantograph":
                WriteInput(controller, GeneralInputIndex.PantographFrontRequest, command.BoolValue ? 1f : 0f);
                WriteInput(controller, GeneralInputIndex.PantographSetRequest, 1f);
                WriteInput(controller, GeneralInputIndex.PantographSet, command.BoolValue ? 1f : 0f);
                ApplyPyscreenWrite(source, ToPyscreenFloat(command, "pantograph_front_status"));
                break;

            case "setRearPantograph":
                WriteInput(controller, GeneralInputIndex.PantographBackRequest, command.BoolValue ? 1f : 0f);
                WriteInput(controller, GeneralInputIndex.PantographSetRequest, 1f);
                WriteInput(controller, GeneralInputIndex.PantographSet, command.BoolValue ? 1f : 0f);
                ApplyPyscreenWrite(source, ToPyscreenFloat(command, "pantograph_rear_status"));
                break;

            default:
                throw new InvalidOperationException($"Unsupported driver command: {command.Action}");
        }

        Plugin.Logger.Msg($"Command {command.Id} applied: {command.Action}");
    }

    private static void ApplyEmergencyBrake(VehicleControllerBase? controller, TrainsetInfo? trainset, bool active)
    {
        if (!active)
        {
            if (IsUsable(trainset))
            {
                trainset!.SetNoPowerAndBrake(false, false, false);
                return;
            }

            if (IsUsable(controller))
            {
                controller!.SetNoPowerAndBrake(false, false);
                return;
            }

            return;
        }

        if (IsUsable(trainset))
        {
            trainset!.SetNoPowerAndBrake(true, true, false);
            return;
        }

        if (IsUsable(controller))
        {
            controller!.SetNoPowerAndBrake(true, true);
            return;
        }

        throw new InvalidOperationException("No active TrainsetInfo or VehicleControllerBase found");
    }

    private static void ApplyNoPowerAndBrake(VehicleControllerBase? controller, TrainsetInfo? trainset, bool noPower, bool brake)
    {
        if (IsUsable(trainset))
        {
            trainset!.SetNoPowerAndBrake(noPower, brake, false);
            return;
        }

        if (IsUsable(controller))
        {
            controller!.SetNoPowerAndBrake(noPower, brake);
            return;
        }

        throw new InvalidOperationException("No active TrainsetInfo or VehicleControllerBase found");
    }

    private static void WriteInput(VehicleControllerBase? controller, int index, float value)
    {
        if (!IsUsable(controller))
            throw new InvalidOperationException("No active VehicleControllerBase found");

        var data = controller!.Input_General?.data;
        if (data == null || index < 0 || index >= data.Length)
            throw new InvalidOperationException($"Input_General index {index} is unavailable");

        data[index] = value;
    }

    private static VehicleControllerBase? ResolveController(VehiclePyscreenDataSource source)
    {
        return IsUsable(source)
            ? source.GetComponentInParent<VehicleControllerBase>()
            : null;
    }

    private static TrainsetInfo? ResolveTrainset(VehiclePyscreenDataSource source, VehicleControllerBase? controller)
    {
        if (IsUsable(controller))
            return controller!.GetComponentInParent<TrainsetInfo>();

        return IsUsable(source)
            ? source.GetComponentInParent<TrainsetInfo>()
            : null;
    }

    private static TelemetryCommand ToPyscreenBool(TelemetryCommand command, string field) => new()
    {
        Id = command.Id,
        Kind = TelemetryCommandKind.PyscreenWrite,
        Target = "eimpcBool",
        Field = field,
        Instance = command.Instance,
        BoolValue = command.BoolValue
    };

    private static TelemetryCommand ToPyscreenFloat(TelemetryCommand command, string field) => new()
    {
        Id = command.Id,
        Kind = TelemetryCommandKind.PyscreenWrite,
        Target = "eimpcFloat",
        Field = field,
        Instance = command.Instance,
        NumberValue = command.BoolValue ? 1.0 : 0.0
    };

    private double CalculateAcceleration(double velocityKmh, long now)
    {
        if (_lastVelocityTick == 0)
        {
            _lastVelocityTick = now;
            _lastVelocityKmh = velocityKmh;
            return 0;
        }

        var deltaSeconds = Math.Max(0.001, (now - _lastVelocityTick) / 1000.0);
        var acceleration = ((velocityKmh - _lastVelocityKmh) / 3.6) / deltaSeconds;
        _lastVelocityTick = now;
        _lastVelocityKmh = velocityKmh;
        return acceleration;
    }

    private static bool IsUsable(UnityObject? value)
    {
        return value != null && value.Pointer != IntPtr.Zero && !value.WasCollected;
    }

    private static bool IsUsable(Il2CppObjectBase? value)
    {
        return value != null && value.Pointer != IntPtr.Zero && !value.WasCollected;
    }

    private static double Read(Il2CppStructArray<double>? values, int index) =>
        values != null && index >= 0 && index < values.Length ? values[index] : 0;

    private static int Read(Il2CppStructArray<int>? values, int index) =>
        values != null && index >= 0 && index < values.Length ? values[index] : 0;

    private static bool Read(Il2CppStructArray<bool>? values, int index) =>
        values != null && index >= 0 && index < values.Length && values[index];

    private static void Write<T>(Il2CppStructArray<T>? values, int fieldIndex, int fieldCount, int instance, T value)
        where T : unmanaged
    {
        if (values == null)
            throw new InvalidOperationException("Target command array is not available");

        var index = fieldIndex + Math.Max(0, instance) * fieldCount;
        if (index < 0 || index >= values.Length)
            throw new IndexOutOfRangeException($"Command index {index} is outside target array length {values.Length}");

        values[index] = value;
    }

    private static int ResolveIndex(TelemetryCommand command, IReadOnlyDictionary<string, int> fields)
    {
        if (command.Index.HasValue)
            return command.Index.Value;

        if (!string.IsNullOrWhiteSpace(command.Field) && fields.TryGetValue(command.Field, out var index))
            return index;

        throw new InvalidOperationException($"Unknown field '{command.Field}' for {command.Target}");
    }

    private static Il2CppStructArray<T>? GetArray<T>(Il2CppObjectBase? data)
        where T : unmanaged
    {
        if (!IsUsable(data))
            return null;

        return new Il2CppStructArray<T>(data!.Pointer);
    }

    private static float Clamp01(double value) =>
        (float)Math.Max(0.0, Math.Min(1.0, value));

    private static float Clamp01OrRaw(double value, float maxRaw)
    {
        var max = Math.Max(1f, maxRaw);
        if (value >= 0.0 && value <= 1.0)
            return (float)(value * max);

        return (float)Math.Max(0.0, Math.Min(max, value));
    }

    private static class GeneralInputIndex
    {
        public const int DriveHandle = 0;
        public const int DriveHandlePressed = 1;
        public const int SpeedCtrl = 3;
        public const int SpeedCtrlPressed = 4;
        public const int MainBrake = 5;
        public const int LocalBrake = 6;
        public const int ThirdBrake = 7;
        public const int EmergencyBrakeButton = 8;
        public const int SecuritySystemButton = 9;
        public const int SpringBrakeRequest = 10;
        public const int SpringBrakeValve = 11;
        public const int MainSwitch = 12;
        public const int DirectionRequest = 13;
        public const int BatteryRequest = 14;
        public const int BatteryOffRequest = 15;
        public const int PantographFrontRequest = 16;
        public const int PantographBackRequest = 17;
        public const int PantographSetRequest = 18;
        public const int PantographSet = 19;
        public const int HornSignal = 21;
        public const int SandingButton = 31;
        public const int RadioStop = 33;
        public const int RadioStopRequest = 34;
        public const int EmergencyBrakeButtonFlag = 51;
        public const int EtcsAckButton = 84;
    }

    private static class FloatIndex
    {
        public const int LightLevel = 0;
        public const int RadioVolume = 1;
        public const int Velocity = 2;
        public const int MotorFrequency = 3;
        public const int PowerConsumption = 4;
        public const int SpeedCtrl = 5;
        public const int SpeedCtrlPower = 6;
        public const int Voltage = 7;
        public const int NewSpeed = 8;
        public const int PantographPressure = 9;
        public const int DistanceCounter = 10;
        public const int TrainLength = 11;
        public const int DistanceDriven = 12;
        public const int PneumaticBrakeStatus = 13;
        public const int LowVoltageStatus = 14;
        public const int PantographCompressorStatus = 15;
        public const int TractionForce = 16;
        public const int CombustionRpm = 17;
        public const int CombustionCoolant = 18;
        public const int CurrentVoltageRatio = 19;
        public const int TractionPercent = 20;
    }

    private static class BoolIndex
    {
        public const int Shp = 0;
        public const int Ca = 1;
        public const int Battery = 2;
        public const int Converter = 3;
        public const int Sanding = 4;
        public const int SpeedCtrlActive = 5;
        public const int SpeedCtrlStandby = 6;
        public const int RadioActive = 7;
        public const int LightsCompartments = 9;
        public const int FireDetection = 10;
        public const int AlarmActive = 11;
        public const int AcStatus = 12;
        public const int DieselMode = 13;
        public const int RadioNoise = 14;
        public const int RadioNightMode = 15;
        public const int RadioVolumeMode = 16;
    }

    private static class IntIndex
    {
        public const int Seconds = 0;
        public const int Minutes = 1;
        public const int Hours = 2;
        public const int RadioChannel = 3;
        public const int TrainStationCount = 4;
        public const int UnitNo = 5;
        public const int CarNo = 6;
        public const int Cab = 7;
        public const int LightsFront = 8;
        public const int LightsRear = 9;
        public const int Direction = 10;
        public const int MainCtrlPos = 11;
        public const int MainCtrlActual = 12;
        public const int Day = 26;
        public const int Month = 27;
        public const int Year = 28;
        public const int AbsStatus = 29;
        public const int SoloDrive = 30;
        public const int SilentMode = 31;
        public const int HvacActive = 33;
        public const int EdBrake = 34;
        public const int BrakeDelay = 35;
        public const int SpeedCtrlStatus = 37;
        public const int CabinDirection = 38;
        public const int SagStatus = 39;
        public const int CombustionActive = 40;
        public const int DayOfWeek = 41;
        public const int ScreenBrightness = 42;
    }

    private static class PressureIndex
    {
        public const int BrakeCylinder = 0;
        public const int BrakePipe = 1;
        public const int Supply = 2;
        public const int Control = 3;
    }

    private static class BrakeBoolIndex
    {
        public const int SpringActive = 0;
        public const int SpringShutoff = 1;
    }

    private static class EmuBoolIndex
    {
        public const int Doors = 0;
        public const int DoorsNo = 1;
        public const int DoorsLeft = 2;
        public const int DoorsRight = 3;
        public const int DoorstepLeft = 4;
        public const int DoorstepRight = 5;
        public const int Slip = 6;
        public const int Brakes = 7;
    }
}
#endif
