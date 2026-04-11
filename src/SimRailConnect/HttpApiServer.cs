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
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace SimRailConnect;

/// <summary>
/// Lightweight HTTP REST API server using HttpListener.
/// Exposes telemetry data and control endpoints for external programs.
/// </summary>
public class HttpApiServer
{
    private readonly int _port;
    private readonly bool _enableWrite;
    private HttpListener? _listener;
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>The actual HTTP prefix that the listener successfully bound to.</summary>
    public string BoundPrefix { get; private set; } = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions JsonPrettyOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public HttpApiServer(int port, bool enableWrite)
    {
        _port = port;
        _enableWrite = enableWrite;
    }

    public void Start()
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
        try
        {
            _listener.Start();
            // '+' binds to all network interfaces
            BoundPrefix = $"http://+:{_port}/ (all interfaces)";
        }
        catch (HttpListenerException)
        {
            // Fall back to loopback only (does not require admin rights)
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            _listener.Start();
            BoundPrefix = $"http://localhost:{_port}/";
        }

        _running = true;
        _thread = new Thread(ServerLoop)
        {
            IsBackground = true,
            Name = "SimRailConnect_HttpApi"
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    private void ServerLoop()
    {
        while (_running && _listener != null && _listener.IsListening)
        {
            try
            {
                var ctx = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
            }
            catch (HttpListenerException) when (!_running) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Plugin.Logger.Warning($"HTTP server error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath?.ToLowerInvariant() ?? "/";
            var method = ctx.Request.HttpMethod;
            var pretty = ctx.Request.QueryString["pretty"] == "true";
            var options = pretty ? JsonPrettyOptions : JsonOptions;

            // CORS headers
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                ctx.Response.Close();
                return;
            }

            object? responseBody = path switch
            {
                "/" or "/api" => HandleIndex(),
                "/api/telemetry" => HandleTelemetry(),
                "/api/train" => HandleTrain(),
                "/api/brakes" => HandleBrakes(),
                "/api/electrical" => HandleElectrical(),
                "/api/safety" => HandleSafety(),
                "/api/doors" => HandleDoors(),
                "/api/controls" => HandleControls(),
                "/api/station" => HandleStation(),
                "/api/environment" => HandleEnvironment(),
                "/api/write" when method == "POST" => HandleWrite(ctx.Request),
                "/api/write" => ApiResponse<string>.Fail("Use POST method"),
                "/api/invalidate" => HandleInvalidateCache(),
                "/api/debug" => HandleDebug(),
                _ => null
            };

            if (responseBody == null)
            {
                ctx.Response.StatusCode = 404;
                responseBody = ApiResponse<string>.Fail($"Unknown endpoint: {path}");
            }

            var json = JsonSerializer.Serialize(responseBody, responseBody.GetType(), options);
            var buffer = Encoding.UTF8.GetBytes(json);

            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = buffer.Length;
            ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                var errJson = JsonSerializer.Serialize(
                    ApiResponse<string>.Fail(ex.Message), JsonOptions);
                var errBuf = Encoding.UTF8.GetBytes(errJson);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.OutputStream.Write(errBuf, 0, errBuf.Length);
            }
            catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    // ───────────────────────────── Endpoint handlers ─────────────────────────

    private object HandleIndex()
    {
        return new
        {
            name = "SimRailConnect API",
            version = Plugin.PluginVersion,
            endpoints = new Dictionary<string, string>
            {
                ["GET /api/telemetry"] = "Complete telemetry snapshot",
                ["GET /api/train"] = "Train movement data (speed, distance, direction)",
                ["GET /api/brakes"] = "Brake pressures (BC, BP, SP, CP)",
                ["GET /api/electrical"] = "Electrical/traction data (voltage, power, RPM)",
                ["GET /api/safety"] = "Safety systems (SHP, CA, alarms)",
                ["GET /api/doors"] = "Door and doorstep states",
                ["GET /api/controls"] = "Driver control positions",
                ["GET /api/station"] = "Next station information",
                ["GET /api/environment"] = "Time, weather, radio",
                ["POST /api/write"] = "Write value to telemetry (JSON body: {target, field, value})",
                ["GET /api/invalidate"] = "Invalidate cached game object references",
                ["GET /api/debug"] = "Native cache diagnostics (data offsets, array lengths, raw samples)"
            },
            hint = "Add ?pretty=true to any endpoint for formatted JSON"
        };
    }

    private object HandleTelemetry()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot == null)
            return ApiResponse<string>.Fail("No telemetry data available. Is a train active?");
        return ApiResponse<TelemetrySnapshot>.Ok(snapshot);
    }

    private object HandleTrain()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Train == null)
            return ApiResponse<string>.Fail("No train data available");
        return ApiResponse<TrainInfo>.Ok(snapshot.Train);
    }

    private object HandleBrakes()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Brakes == null)
            return ApiResponse<string>.Fail("No brake data available");
        return ApiResponse<BrakeInfo>.Ok(snapshot.Brakes);
    }

    private object HandleElectrical()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Electrical == null)
            return ApiResponse<string>.Fail("No electrical data available");
        return ApiResponse<ElectricalInfo>.Ok(snapshot.Electrical);
    }

    private object HandleSafety()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Safety == null)
            return ApiResponse<string>.Fail("No safety data available");
        return ApiResponse<SafetyInfo>.Ok(snapshot.Safety);
    }

    private object HandleDoors()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Doors == null)
            return ApiResponse<string>.Fail("No door data available");
        return ApiResponse<DoorInfo>.Ok(snapshot.Doors);
    }

    private object HandleControls()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Controls == null)
            return ApiResponse<string>.Fail("No control data available");
        return ApiResponse<ControlInfo>.Ok(snapshot.Controls);
    }

    private object HandleStation()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Station == null)
            return ApiResponse<string>.Fail("No station data available");
        return ApiResponse<StationInfo>.Ok(snapshot.Station);
    }

    private object HandleEnvironment()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.Environment == null)
            return ApiResponse<string>.Fail("No environment data available");
        return ApiResponse<EnvironmentInfo>.Ok(snapshot.Environment);
    }

    private object HandleWrite(HttpListenerRequest request)
    {
        if (!_enableWrite)
            return ApiResponse<string>.Fail("Write API is disabled. Enable in config.");

        try
        {
            var encoding = request.ContentEncoding ?? Encoding.UTF8;
            using var reader = new StreamReader(request.InputStream, encoding);
            var body = reader.ReadToEnd();
            var cmd = JsonSerializer.Deserialize<ControlCommand>(body, JsonOptions);
            if (cmd == null)
                return ApiResponse<string>.Fail("Invalid JSON body");

            return ExecuteWrite(cmd);
        }
        catch (JsonException ex)
        {
            return ApiResponse<string>.Fail($"JSON parse error: {ex.Message}");
        }
    }

    private object ExecuteWrite(ControlCommand cmd)
    {
        // Fail fast if no data source is active — writes would be silently dropped.
        if (TelemetryState.CurrentSnapshot?.IsActive != true)
            return ApiResponse<string>.Fail("No active data source — board a train first.");

        // Map field names to indices
        var (target, index) = ResolveField(cmd.Target, cmd.Field);
        if (index < 0)
            return ApiResponse<string>.Fail(
                $"Unknown field: {cmd.Target}.{cmd.Field}. " +
                $"Use GET / to see available field names.");

        switch (target)
        {
            case "float":
                if (cmd.Value is not JsonElement fElem || !fElem.TryGetDouble(out var dVal))
                    return ApiResponse<string>.Fail("Value must be a number for float fields");
                GameBridge.WriteFloat(index, dVal);
                break;
            case "int":
                if (cmd.Value is not JsonElement iElem || !iElem.TryGetInt32(out var iVal))
                    return ApiResponse<string>.Fail("Value must be an integer for int fields");
                GameBridge.WriteInt(index, iVal);
                break;
            case "bool":
                if (cmd.Value is not JsonElement bElem ||
                    bElem.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return ApiResponse<string>.Fail("Value must be true/false for bool fields");
                GameBridge.WriteBool(index, bElem.GetBoolean());
                break;
            default:
                return ApiResponse<string>.Fail($"Unsupported target type: {target}");
        }

        return ApiResponse<string>.Ok(
            $"Write queued: {cmd.Target}.{cmd.Field} = {cmd.Value} (applies on next main-thread tick)");
    }

    private static (string type, int index) ResolveField(string target, string field)
    {
        var key = $"{target}.{field}".ToLowerInvariant();
        return key switch
        {
            // GeneralFloat fields
            "generalfloat.velocity" or "float.velocity" => ("float", 2),
            "generalfloat.new_speed" or "float.new_speed" => ("float", 8),
            "generalfloat.voltage" or "float.voltage" => ("float", 7),
            "generalfloat.speedctrl" or "float.speedctrl" => ("float", 5),
            "generalfloat.speedctrlpower" or "float.speedctrlpower" => ("float", 6),
            "generalfloat.pantpress" or "float.pantpress" => ("float", 9),
            "generalfloat.distance_counter" or "float.distance_counter" => ("float", 10),
            "generalfloat.train_length" or "float.train_length" => ("float", 11),
            "generalfloat.distance_driven" or "float.distance_driven" => ("float", 12),
            "generalfloat.pneumatic_brake_status" or "float.pneumatic_brake_status" => ("float", 13),
            "generalfloat.eimp_t_tractionforce" or "float.tractionforce" => ("float", 16),
            "generalfloat.eimp_t_tractionpercent" or "float.tractionpercent" => ("float", 20),
            "generalfloat.eimp_t_fd" or "float.motor_freq" => ("float", 19),
            "generalfloat.eimp_t_pd" or "float.power" => ("float", 3),
            "generalfloat.combustion_engine_rpm" or "float.rpm" => ("float", 17),
            "generalfloat.combustion_coolant_temperature" or "float.coolant" => ("float", 18),
            "generalfloat.light_level" or "float.light_level" => ("float", 0),
            "generalfloat.radio_volume" or "float.radio_volume" => ("float", 1),

            // GeneralInt fields
            "generalint.direction" or "int.direction" => ("int", 10),
            "generalint.mainctrl_pos" or "int.mainctrl_pos" or "int.throttle" => ("int", 11),
            "generalint.main_ctrl_actual_pos" or "int.mainctrl_actual" => ("int", 12),
            "generalint.radio_channel" or "int.radio_channel" => ("int", 3),
            "generalint.lights_train_front" or "int.lights_front" => ("int", 8),
            "generalint.lights_train_rear" or "int.lights_rear" => ("int", 9),
            "generalint.cab" or "int.cab" => ("int", 7),
            "generalint.screen_brightness" or "int.brightness" => ("int", 42),
            "generalint.brake_delay_flag" or "int.brake_delay" => ("int", 35),

            // GeneralBool fields
            "generalbool.shp" or "bool.shp" => ("bool", 0),
            "generalbool.ca" or "bool.ca" => ("bool", 1),
            "generalbool.battery" or "bool.battery" => ("bool", 2),
            "generalbool.converter" or "bool.converter" => ("bool", 3),
            "generalbool.sanding" or "bool.sanding" => ("bool", 4),
            "generalbool.speedctrlactive" or "bool.speedctrl" => ("bool", 5),
            "generalbool.radio_active" or "bool.radio" => ("bool", 7),
            "generalbool.lights_compartments" or "bool.lights" => ("bool", 9),
            "generalbool.alarm_active" or "bool.alarm" => ("bool", 11),
            "generalbool.diesel_mode" or "bool.diesel" => ("bool", 13),

            _ => ("", -1)
        };
    }

    private object HandleInvalidateCache()
    {
        GameBridge.RequestInvalidate();
        return ApiResponse<string>.Ok("Cache invalidation scheduled for the next main-thread tick.");
    }

    /// <summary>
    /// Returns a native-memory diagnostic snapshot of each sub-cache object:
    /// whether each PyscreenIOClassBase&lt;T&gt; sub-object was found, whether its
    /// data array pointer is non-null, its element count, and a few raw samples.
    /// <para>
    /// The snapshot is built on the Unity main thread (inside
    /// <c>TelemetryMonitor.Postfix</c>) and retrieved here without any direct
    /// native-memory access from the HTTP thread.
    /// </para>
    /// </summary>
    private object HandleDebug()
    {
        var snapshot = TelemetryState.CurrentSnapshot;
        if (snapshot?.IsActive != true)
            return ApiResponse<string>.Fail(
                "No active telemetry yet (IsActive=false). " +
                "Board a train, wait one tick, then try again.");

        GameBridge.RequestDebugSnapshot();

        // Poll for the main thread to service the request (up to 600 ms).
        var deadline = DateTime.UtcNow.AddMilliseconds(600);
        while (DateTime.UtcNow < deadline)
        {
            var result = GameBridge.GetDebugSnapshot();
            if (result != null)
                return ApiResponse<object>.Ok(result);
            System.Threading.Thread.Sleep(10);
        }

        return ApiResponse<string>.Fail(
            "Debug snapshot timed out — is Pyscreen.Update() still ticking? " +
            "Try boarding a train and retrying.");
    }
}
