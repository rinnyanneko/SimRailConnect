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
using System.Text.Json;

namespace SimRailConnect;

/// <summary>
/// Validates public write commands before they are queued for the Unity main
/// thread. This class is intentionally free of Unity/IL2CPP/native access so it
/// is safe to call from WebSocket network threads.
/// </summary>
internal static class ApiCommandRegistry
{
    private sealed class FieldSpec
    {
        public FieldSpec(string valueKind, int index, double min, double max)
        {
            ValueKind = valueKind;
            Index = index;
            Min = min;
            Max = max;
        }

        public string ValueKind { get; }
        public int Index { get; }
        public double Min { get; }
        public double Max { get; }
    }

    private static readonly Dictionary<string, FieldSpec> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        // GeneralFloat fields
        ["generalfloat.velocity"] = new("float", 2, 0, 500),
        ["float.velocity"] = new("float", 2, 0, 500),
        ["generalfloat.new_speed"] = new("float", 8, 0, 500),
        ["float.new_speed"] = new("float", 8, 0, 500),
        ["generalfloat.voltage"] = new("float", 7, 0, 50000),
        ["float.voltage"] = new("float", 7, 0, 50000),
        ["generalfloat.speedctrl"] = new("float", 5, 0, 250),
        ["float.speedctrl"] = new("float", 5, 0, 250),
        ["generalfloat.speedctrlpower"] = new("float", 6, 0, 1),
        ["float.speedctrlpower"] = new("float", 6, 0, 1),
        ["generalfloat.pantpress"] = new("float", 9, 0, 20),
        ["float.pantpress"] = new("float", 9, 0, 20),
        ["generalfloat.distance_counter"] = new("float", 10, 0, 1000000),
        ["float.distance_counter"] = new("float", 10, 0, 1000000),
        ["generalfloat.train_length"] = new("float", 11, 0, 5000),
        ["float.train_length"] = new("float", 11, 0, 5000),
        ["generalfloat.distance_driven"] = new("float", 12, 0, 1000000),
        ["float.distance_driven"] = new("float", 12, 0, 1000000),
        ["generalfloat.pneumatic_brake_status"] = new("float", 13, 0, 10),
        ["float.pneumatic_brake_status"] = new("float", 13, 0, 10),
        ["generalfloat.eimp_t_tractionforce"] = new("float", 16, -1000, 1000),
        ["float.tractionforce"] = new("float", 16, -1000, 1000),
        ["generalfloat.eimp_t_tractionpercent"] = new("float", 20, -100, 100),
        ["float.tractionpercent"] = new("float", 20, -100, 100),
        ["generalfloat.eimp_t_fd"] = new("float", 19, 0, 1000),
        ["float.motor_freq"] = new("float", 19, 0, 1000),
        ["generalfloat.eimp_t_pd"] = new("float", 3, -20000, 20000),
        ["float.power"] = new("float", 3, -20000, 20000),
        ["generalfloat.combustion_engine_rpm"] = new("float", 17, 0, 5000),
        ["float.rpm"] = new("float", 17, 0, 5000),
        ["generalfloat.combustion_coolant_temperature"] = new("float", 18, -50, 200),
        ["float.coolant"] = new("float", 18, -50, 200),
        ["generalfloat.light_level"] = new("float", 0, 0, 1),
        ["float.light_level"] = new("float", 0, 0, 1),
        ["generalfloat.radio_volume"] = new("float", 1, 0, 1),
        ["float.radio_volume"] = new("float", 1, 0, 1),

        // GeneralInt fields
        ["generalint.direction"] = new("int", 10, -1, 1),
        ["int.direction"] = new("int", 10, -1, 1),
        ["generalint.mainctrl_pos"] = new("int", 11, -100, 100),
        ["int.mainctrl_pos"] = new("int", 11, -100, 100),
        ["int.throttle"] = new("int", 11, -100, 100),
        ["generalint.main_ctrl_actual_pos"] = new("int", 12, -100, 100),
        ["int.mainctrl_actual"] = new("int", 12, -100, 100),
        ["generalint.radio_channel"] = new("int", 3, 0, 99),
        ["int.radio_channel"] = new("int", 3, 0, 99),
        ["generalint.lights_train_front"] = new("int", 8, 0, 10),
        ["int.lights_front"] = new("int", 8, 0, 10),
        ["generalint.lights_train_rear"] = new("int", 9, 0, 10),
        ["int.lights_rear"] = new("int", 9, 0, 10),
        ["generalint.cab"] = new("int", 7, 0, 4),
        ["int.cab"] = new("int", 7, 0, 4),
        ["generalint.screen_brightness"] = new("int", 42, 0, 10),
        ["int.brightness"] = new("int", 42, 0, 10),
        ["generalint.brake_delay_flag"] = new("int", 35, 0, 10),
        ["int.brake_delay"] = new("int", 35, 0, 10),

        // GeneralBool fields
        ["generalbool.shp"] = new("bool", 0, 0, 1),
        ["bool.shp"] = new("bool", 0, 0, 1),
        ["generalbool.ca"] = new("bool", 1, 0, 1),
        ["bool.ca"] = new("bool", 1, 0, 1),
        ["generalbool.battery"] = new("bool", 2, 0, 1),
        ["bool.battery"] = new("bool", 2, 0, 1),
        ["generalbool.converter"] = new("bool", 3, 0, 1),
        ["bool.converter"] = new("bool", 3, 0, 1),
        ["generalbool.sanding"] = new("bool", 4, 0, 1),
        ["bool.sanding"] = new("bool", 4, 0, 1),
        ["generalbool.speedctrlactive"] = new("bool", 5, 0, 1),
        ["bool.speedctrl"] = new("bool", 5, 0, 1),
        ["generalbool.radio_active"] = new("bool", 7, 0, 1),
        ["bool.radio"] = new("bool", 7, 0, 1),
        ["generalbool.lights_compartments"] = new("bool", 9, 0, 1),
        ["bool.lights"] = new("bool", 9, 0, 1),
        ["generalbool.alarm_active"] = new("bool", 11, 0, 1),
        ["bool.alarm"] = new("bool", 11, 0, 1),
        ["generalbool.diesel_mode"] = new("bool", 13, 0, 1),
        ["bool.diesel"] = new("bool", 13, 0, 1),
    };

    private static readonly Dictionary<string, (string Target, string Field)> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["train.set_throttle"] = ("int", "throttle"),
        ["train.set_reverser"] = ("int", "direction"),
        ["train.set_direction"] = ("int", "direction"),
        ["brakes.set_brake"] = ("float", "pneumatic_brake_status"),
        ["brakes.set_train_brake"] = ("float", "pneumatic_brake_status"),
        ["controls.set_speed_control"] = ("float", "speedctrl"),
        ["controls.set_speed_control_power"] = ("float", "speedctrlpower"),
        ["controls.set_speed_control_active"] = ("bool", "speedctrl"),
        ["controls.set_sanding"] = ("bool", "sanding"),
        ["safety.set_shp"] = ("bool", "shp"),
        ["safety.set_ca"] = ("bool", "ca"),
        ["safety.set_alarm"] = ("bool", "alarm"),
        ["lights.set_front"] = ("int", "lights_front"),
        ["lights.set_rear"] = ("int", "lights_rear"),
        ["lights.set_compartments"] = ("bool", "lights"),
        ["radio.set_channel"] = ("int", "radio_channel"),
        ["radio.set_active"] = ("bool", "radio"),
        ["radio.set_volume"] = ("float", "radio_volume"),
    };

    public static bool TryCreateFromWebSocket(
        JsonElement root,
        string id,
        string? clientId,
        out QueuedWriteCommand queued,
        out string code,
        out string message)
    {
        queued = new QueuedWriteCommand();
        code = "";
        message = "";

        if (!root.TryGetProperty("target", out var targetElement) ||
            targetElement.ValueKind != JsonValueKind.String)
        {
            code = "MISSING_TARGET";
            message = "Command requires a string target";
            return false;
        }

        var target = targetElement.GetString() ?? "";
        string field;

        if (root.TryGetProperty("field", out var fieldElement) &&
            fieldElement.ValueKind == JsonValueKind.String)
        {
            field = fieldElement.GetString() ?? "";
        }
        else if (root.TryGetProperty("action", out var actionElement) &&
                 actionElement.ValueKind == JsonValueKind.String)
        {
            var key = $"{target}.{actionElement.GetString()}";
            if (!Actions.TryGetValue(key, out var mapped))
            {
                code = "UNKNOWN_ACTION";
                message = $"Unknown command action: {key}";
                return false;
            }

            target = mapped.Target;
            field = mapped.Field;
        }
        else
        {
            code = "MISSING_FIELD";
            message = "Command requires either field or action";
            return false;
        }

        if (!root.TryGetProperty("value", out var value))
        {
            code = "MISSING_VALUE";
            message = "Command requires a value";
            return false;
        }

        return TryCreate(target, field, value, id, clientId, out queued, out code, out message);
    }

    private static bool TryCreate(
        string target,
        string field,
        object? value,
        string id,
        string? clientId,
        out QueuedWriteCommand queued,
        out string code,
        out string message)
    {
        queued = new QueuedWriteCommand();
        code = "";
        message = "";

        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(field))
        {
            code = "INVALID_COMMAND";
            message = "Target and field are required";
            return false;
        }

        if (!Fields.TryGetValue($"{target}.{field}", out var spec))
        {
            code = "UNKNOWN_FIELD";
            message = $"Unknown field: {target}.{field}";
            return false;
        }

        queued = new QueuedWriteCommand
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            ClientId = clientId,
            Target = target,
            Field = field,
            ValueKind = spec.ValueKind,
            Index = spec.Index,
            QueuedAtUtc = DateTime.UtcNow
        };

        switch (spec.ValueKind)
        {
            case "float":
                if (!TryGetDouble(value, out var doubleValue))
                {
                    code = "INVALID_VALUE";
                    message = "Value must be a number";
                    return false;
                }
                if (doubleValue < spec.Min || doubleValue > spec.Max)
                {
                    code = "VALUE_OUT_OF_RANGE";
                    message = $"Value must be between {spec.Min} and {spec.Max}";
                    return false;
                }
                queued.FloatValue = doubleValue;
                return true;

            case "int":
                if (!TryGetInt(value, out var intValue))
                {
                    code = "INVALID_VALUE";
                    message = "Value must be an integer";
                    return false;
                }
                if (intValue < spec.Min || intValue > spec.Max)
                {
                    code = "VALUE_OUT_OF_RANGE";
                    message = $"Value must be between {spec.Min} and {spec.Max}";
                    return false;
                }
                queued.IntValue = intValue;
                return true;

            case "bool":
                if (!TryGetBool(value, out var boolValue))
                {
                    code = "INVALID_VALUE";
                    message = "Value must be true or false";
                    return false;
                }
                queued.BoolValue = boolValue;
                return true;

            default:
                code = "UNSUPPORTED_TARGET";
                message = $"Unsupported target type: {spec.ValueKind}";
                return false;
        }
    }

    private static bool TryGetDouble(object? value, out double result)
    {
        if (value is JsonElement element)
            return element.TryGetDouble(out result);
        if (value is double d)
        {
            result = d;
            return true;
        }
        if (value is float f)
        {
            result = f;
            return true;
        }
        if (value is int i)
        {
            result = i;
            return true;
        }
        result = 0;
        return false;
    }

    private static bool TryGetInt(object? value, out int result)
    {
        if (value is JsonElement element)
            return element.TryGetInt32(out result);
        if (value is int i)
        {
            result = i;
            return true;
        }
        result = 0;
        return false;
    }

    private static bool TryGetBool(object? value, out bool result)
    {
        if (value is JsonElement element &&
            element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            result = element.GetBoolean();
            return true;
        }
        if (value is bool b)
        {
            result = b;
            return true;
        }
        result = false;
        return false;
    }
}
