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
using System.Collections.Generic;

namespace SimRailConnect;

/// <summary>
/// Complete telemetry snapshot containing all subsystems.
/// </summary>
public class TelemetrySnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; }
    public TrainInfo? Train { get; set; }
    public BrakeInfo? Brakes { get; set; }
    public ElectricalInfo? Electrical { get; set; }
    public SafetyInfo? Safety { get; set; }
    public DoorInfo? Doors { get; set; }
    public ControlInfo? Controls { get; set; }
    public StationInfo? Station { get; set; }
    public EnvironmentInfo? Environment { get; set; }
}

/// <summary>
/// Basic train movement data.
/// </summary>
public class TrainInfo
{
    /// <summary>Current speed in km/h.</summary>
    public double Velocity { get; set; }
    /// <summary>Current speed (integer version).</summary>
    public int VelocityInt { get; set; }
    /// <summary>Approximate acceleration in m/s², derived from successive telemetry snapshots.</summary>
    public double Acceleration { get; set; }
    /// <summary>Target/new speed in km/h.</summary>
    public double NewSpeed { get; set; }
    /// <summary>Total distance driven in km.</summary>
    public double DistanceDriven { get; set; }
    /// <summary>Distance counter in km.</summary>
    public double DistanceCounter { get; set; }
    /// <summary>Train length in metres.</summary>
    public double TrainLength { get; set; }
    /// <summary>Direction: -1=Reverse, 0=Neutral, 1=Forward.</summary>
    public int Direction { get; set; }
    /// <summary>Cabin direction.</summary>
    public int CabinDirection { get; set; }
    /// <summary>Current cab number.</summary>
    public int Cab { get; set; }
    /// <summary>Unit number.</summary>
    public int UnitNo { get; set; }
    /// <summary>Car number.</summary>
    public int CarNo { get; set; }
}

/// <summary>
/// Brake system pressures and states.
/// </summary>
public class BrakeInfo
{
    /// <summary>Brake cylinder pressure (bar).</summary>
    public double BC { get; set; }
    /// <summary>Brake pipe pressure (bar).</summary>
    public double BP { get; set; }
    /// <summary>Supply/reservoir pressure (bar).</summary>
    public double SP { get; set; }
    /// <summary>Control pressure (bar).</summary>
    public double CP { get; set; }
    /// <summary>Pneumatic brake status value.</summary>
    public double PneumaticBrakeStatus { get; set; }
    /// <summary>Spring brake active.</summary>
    public bool SpringActive { get; set; }
    /// <summary>Spring brake shutoff.</summary>
    public bool SpringShutoff { get; set; }
    /// <summary>ED (electrodynamic) brake active.</summary>
    public int EdBrakeActive { get; set; }
    /// <summary>Brake delay flag.</summary>
    public int BrakeDelayFlag { get; set; }
}

/// <summary>
/// Electrical and traction data.
/// </summary>
public class ElectricalInfo
{
    /// <summary>Overhead line voltage (V).</summary>
    public double Voltage { get; set; }
    /// <summary>Traction force (kN).</summary>
    public double TractionForce { get; set; }
    /// <summary>Traction percentage (%).</summary>
    public double TractionPercent { get; set; }
    /// <summary>Motor frequency (Hz).</summary>
    public double MotorFrequency { get; set; }
    /// <summary>Power consumption (kW).</summary>
    public double PowerConsumption { get; set; }
    /// <summary>Current/voltage ratio.</summary>
    public double CurrentVoltageRatio { get; set; }
    /// <summary>Pantograph pressure (bar).</summary>
    public double PantographPressure { get; set; }
    /// <summary>Low-voltage status value reported by the Pyscreen source.</summary>
    public double LowVoltageStatus { get; set; }
    /// <summary>Pantograph compressor status value reported by the Pyscreen source.</summary>
    public double PantographCompressorStatus { get; set; }
    /// <summary>Converter on.</summary>
    public bool Converter { get; set; }
    /// <summary>AC power status.</summary>
    public bool AcStatus { get; set; }
    /// <summary>Battery on.</summary>
    public bool Battery { get; set; }
    /// <summary>Diesel combustion engine active.</summary>
    public int CombustionEngineActive { get; set; }
    /// <summary>Diesel engine RPM.</summary>
    public double CombustionEngineRPM { get; set; }
    /// <summary>Coolant temperature (°C).</summary>
    public double CoolantTemperature { get; set; }
    /// <summary>Diesel mode flag.</summary>
    public bool DieselMode { get; set; }
}

/// <summary>
/// Safety systems: SHP, CA, ETCS, etc.
/// </summary>
public class SafetyInfo
{
    /// <summary>SHP (Samoczynne Hamowanie Pociągu) active.</summary>
    public bool SHP { get; set; }
    /// <summary>CA (Czuwak Aktywny) active.</summary>
    public bool CA { get; set; }
    /// <summary>Alarm active.</summary>
    public bool AlarmActive { get; set; }
    /// <summary>Fire detection system active.</summary>
    public bool FireDetectionActive { get; set; }
    /// <summary>ABS status.</summary>
    public int AbsStatus { get; set; }
    /// <summary>SAG status.</summary>
    public int SagStatus { get; set; }
}

/// <summary>
/// Door states (for EMU/passenger trains).
/// </summary>
public class DoorInfo
{
    /// <summary>Overall door status.</summary>
    public bool Doors { get; set; }
    /// <summary>Raw "doors_no" state from the EMU Pyscreen source.</summary>
    public bool DoorsNo { get; set; }
    /// <summary>Left doors open.</summary>
    public bool DoorsLeft { get; set; }
    /// <summary>Right doors open.</summary>
    public bool DoorsRight { get; set; }
    /// <summary>Left doorstep deployed.</summary>
    public bool DoorstepLeft { get; set; }
    /// <summary>Right doorstep deployed.</summary>
    public bool DoorstepRight { get; set; }
    /// <summary>Wheel slip detected.</summary>
    public bool Slip { get; set; }
    /// <summary>Brake engagement (EMU).</summary>
    public bool BrakesEngaged { get; set; }
}

/// <summary>
/// Driver control positions.
/// </summary>
public class ControlInfo
{
    /// <summary>Main controller position (throttle notch).</summary>
    public int MainCtrlPos { get; set; }
    /// <summary>Actual main controller position.</summary>
    public int MainCtrlActualPos { get; set; }
    /// <summary>Speed control setpoint.</summary>
    public double SpeedCtrl { get; set; }
    /// <summary>Speed control power.</summary>
    public double SpeedCtrlPower { get; set; }
    /// <summary>Speed control active.</summary>
    public bool SpeedCtrlActive { get; set; }
    /// <summary>Speed control standby.</summary>
    public bool SpeedCtrlStandby { get; set; }
    /// <summary>Speed controller status.</summary>
    public int SpeedCtrlStatus { get; set; }
    /// <summary>Sanding active.</summary>
    public bool Sanding { get; set; }
    /// <summary>Solo drive active.</summary>
    public int SoloDriveActive { get; set; }
    /// <summary>Silent mode active.</summary>
    public int SilentModeActive { get; set; }
    /// <summary>HVAC active state.</summary>
    public int HvacActive { get; set; }
    /// <summary>Front lights setting.</summary>
    public int LightsFront { get; set; }
    /// <summary>Rear lights setting.</summary>
    public int LightsRear { get; set; }
    /// <summary>Compartment lights on.</summary>
    public bool LightsCompartments { get; set; }
}

/// <summary>
/// Next station and timetable info.
/// </summary>
public class StationInfo
{
    /// <summary>Next station name.</summary>
    public string? NextStation { get; set; }
    /// <summary>Distance to next station.</summary>
    public string? Distance { get; set; }
    /// <summary>Number of stations in timetable.</summary>
    public int StationCount { get; set; }
}

/// <summary>
/// Time and environment data.
/// </summary>
public class EnvironmentInfo
{
    /// <summary>Game hours (0-23).</summary>
    public int Hours { get; set; }
    /// <summary>Game minutes (0-59).</summary>
    public int Minutes { get; set; }
    /// <summary>Game seconds (0-59).</summary>
    public int Seconds { get; set; }
    /// <summary>Game day.</summary>
    public int Day { get; set; }
    /// <summary>Game month.</summary>
    public int Month { get; set; }
    /// <summary>Game year.</summary>
    public int Year { get; set; }
    /// <summary>Day of week.</summary>
    public int DayOfWeek { get; set; }
    /// <summary>Cabin light level.</summary>
    public double LightLevel { get; set; }
    /// <summary>Radio active.</summary>
    public bool RadioActive { get; set; }
    /// <summary>Radio channel.</summary>
    public int RadioChannel { get; set; }
    /// <summary>Radio volume.</summary>
    public double RadioVolume { get; set; }
    /// <summary>Radio noise flag.</summary>
    public bool RadioNoise { get; set; }
    /// <summary>Radio night-mode flag.</summary>
    public bool RadioNightMode { get; set; }
    /// <summary>Radio volume-adjust mode flag.</summary>
    public bool RadioVolumeMode { get; set; }
    /// <summary>Screen brightness.</summary>
    public int ScreenBrightness { get; set; }
}

/// <summary>
/// Control command sent by external programs to modify game state.
/// </summary>
public class ControlCommand
{
    /// <summary>
    /// Target subsystem: "generalFloat", "generalInt", "generalBool",
    /// "eimppn", "brakes", "emu".
    /// </summary>
    public string Target { get; set; } = "";

    /// <summary>
    /// Field name within the target subsystem (e.g. "velocity", "mainctrl_pos").
    /// </summary>
    public string Field { get; set; } = "";

    /// <summary>
    /// Value to set (will be parsed to appropriate type).
    /// </summary>
    public object? Value { get; set; }
}

/// <summary>
/// Validated write command queued by WebSocket network handlers and applied
/// later by the Unity main thread.
/// </summary>
public class QueuedWriteCommand
{
    public string Id { get; set; } = "";
    public string? ClientId { get; set; }
    public string Target { get; set; } = "";
    public string Field { get; set; } = "";
    public string ValueKind { get; set; } = "";
    public int Index { get; set; }
    public double FloatValue { get; set; }
    public int IntValue { get; set; }
    public bool BoolValue { get; set; }
    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;

    public object Value => ValueKind switch
    {
        "float" => FloatValue,
        "int" => IntValue,
        "bool" => BoolValue,
        _ => ""
    };
}

/// <summary>
/// Result produced by the Unity main thread after a queued write has been
/// applied or rejected at apply time.
/// </summary>
public class CommandResult
{
    public string Type { get; set; } = "commandResult";
    public string Id { get; set; } = "";
    public string? ClientId { get; set; }
    public bool Ok { get; set; }
    public bool Queued { get; set; } = true;
    public bool Applied { get; set; }
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Target { get; set; }
    public string? Field { get; set; }
    public object? Value { get; set; }
    public long TimestampUnixMs { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static CommandResult AppliedOk(QueuedWriteCommand command) => new()
    {
        Id = command.Id,
        ClientId = command.ClientId,
        Ok = true,
        Applied = true,
        Target = command.Target,
        Field = command.Field,
        Value = command.Value,
        Message = "Command applied on Unity main thread"
    };

    public static CommandResult Fail(QueuedWriteCommand command, string code, string message) => new()
    {
        Id = command.Id,
        ClientId = command.ClientId,
        Ok = false,
        Applied = false,
        Code = code,
        Message = message,
        Target = command.Target,
        Field = command.Field,
        Value = command.Value
    };
}

/// <summary>
/// Response wrapper for API calls.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
}
