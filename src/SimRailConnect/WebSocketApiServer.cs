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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SimRailConnect;

/// <summary>
/// Local WebSocket telemetry server.
/// WebSocket callbacks are background-thread code, so this class only reads
/// TelemetryState snapshots. Unity and IL2CPP access belongs to the main-thread
/// Pyscreen collector.
/// </summary>
public sealed class WebSocketApiServer
{
    private readonly int _port;
    private readonly int _maxClients;
    private readonly int _defaultRateHz;
    private readonly int _maxRateHz;
    private readonly int _payloadLimitBytes;
    private readonly string _token;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _broadcastTask;
    private long _sequence;

    public string Url => $"ws://localhost:{_port}/ws";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public WebSocketApiServer(
        int port,
        int maxClients,
        int defaultRateHz,
        int maxRateHz,
        int payloadLimitBytes,
        string token)
    {
        _port = port;
        _maxClients = Math.Max(1, maxClients);
        _defaultRateHz = Clamp(defaultRateHz, 1, 60);
        _maxRateHz = Clamp(maxRateHz, 1, 60);
        _payloadLimitBytes = Math.Max(1024, payloadLimitBytes);
        _token = token ?? "";
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _broadcastTask = Task.Run(() => BroadcastLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }

        foreach (var client in _clients.Values)
            client.CloseAsync("server stopping").GetAwaiter().GetResult();

        _clients.Clear();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                if (!ctx.Request.IsWebSocketRequest || ctx.Request.Url?.AbsolutePath != "/ws")
                {
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    continue;
                }

                if (!IsAuthorized(ctx.Request))
                {
                    ctx.Response.StatusCode = 401;
                    ctx.Response.Close();
                    continue;
                }

                if (_clients.Count >= _maxClients)
                {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.Close();
                    continue;
                }

                var wsContext = await ctx.AcceptWebSocketAsync(null).ConfigureAwait(false);
                var client = new ClientConnection(wsContext.WebSocket, _defaultRateHz, _maxRateHz);
                if (!_clients.TryAdd(client.Id, client))
                {
                    await client.CloseAsync("duplicate client id").ConfigureAwait(false);
                    continue;
                }

                Plugin.Logger.Msg($"[WebSocket] open client={client.Id}");
                _ = Task.Run(() => ReceiveLoopAsync(client, token));
                await SendAsync(client, new { type = "hello", clientId = client.Id, url = Url }).ConfigureAwait(false);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Plugin.Logger.Warning($"[WebSocket] accept failure: {ex.Message}");
                try { ctx?.Response.Close(); } catch { }
            }
        }
    }

    private async Task ReceiveLoopAsync(ClientConnection client, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && client.Socket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextAsync(client.Socket, token).ConfigureAwait(false);
                if (text == null) break;

                try
                {
                    using var doc = JsonDocument.Parse(text);
                    await HandleClientMessageAsync(client, doc.RootElement).ConfigureAwait(false);
                }
                catch (JsonException ex)
                {
                    Plugin.Logger.Warning($"[WebSocket] parse failure client={client.Id}: {ex.Message}");
                    await SendErrorAsync(client, null, "MALFORMED_JSON", ex.Message).ConfigureAwait(false);
                }
            }
        }
        catch (WebSocketException ex)
        {
            Plugin.Logger.Warning($"[WebSocket] receive failure client={client.Id}: {ex.Message}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Plugin.Logger.Warning($"[WebSocket] client loop failure client={client.Id}: {ex.Message}");
        }
        finally
        {
            _clients.TryRemove(client.Id, out _);
            await client.CloseAsync("closed").ConfigureAwait(false);
            Plugin.Logger.Msg($"[WebSocket] close client={client.Id}");
        }
    }

    private async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[Math.Min(_payloadLimitBytes, 8192)];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("Only text JSON messages are supported");

            if (stream.Length + result.Count > _payloadLimitBytes)
                throw new InvalidDataException($"Payload exceeds {_payloadLimitBytes} bytes");

            stream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    private async Task HandleClientMessageAsync(ClientConnection client, JsonElement root)
    {
        if (!root.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            await SendErrorAsync(client, null, "MISSING_TYPE", "Message requires a string type").ConfigureAwait(false);
            return;
        }

        var type = typeElement.GetString() ?? "";
        var id = TryGetString(root, "id");

        switch (type)
        {
            case "ping":
                await SendAsync(client, new { type = "pong", id, timestampUnixMs = UnixNow() }).ConfigureAwait(false);
                break;

            case "subscribe":
                HandleSubscribe(client, root);
                Plugin.Logger.Msg($"[WebSocket] subscribe client={client.Id} channels={string.Join(",", client.GetChannels())}");
                await SendAsync(client, new { type = "ack", id, ok = true }).ConfigureAwait(false);
                break;

            case "unsubscribe":
                client.SetChannels(Array.Empty<string>(), 0);
                Plugin.Logger.Msg($"[WebSocket] unsubscribe client={client.Id}");
                await SendAsync(client, new { type = "ack", id, ok = true }).ConfigureAwait(false);
                break;

            case "getSnapshot":
                await SendAsync(client, new
                {
                    type = "snapshot",
                    id,
                    ok = true,
                    seq = Interlocked.Increment(ref _sequence),
                    timestampUnixMs = UnixNow(),
                    data = TelemetryState.CurrentSnapshot
                }).ConfigureAwait(false);
                break;

            case "invalidate":
                await HandleInvalidateAsync(client, id).ConfigureAwait(false);
                break;

            case "debug":
                await HandleDebugAsync(client, id).ConfigureAwait(false);
                break;

            case "command":
                await HandleCommandAsync(client, root, id).ConfigureAwait(false);
                break;

            default:
                await SendErrorAsync(client, id, "UNKNOWN_TYPE", $"Unknown message type: {type}").ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleDebugAsync(ClientConnection client, string? id)
    {
        await SendNativeTelemetryDisabledAsync(client, id).ConfigureAwait(false);
    }

    private async Task HandleInvalidateAsync(ClientConnection client, string? id)
    {
        var command = new TelemetryCommand
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Kind = TelemetryCommandKind.InvalidateTelemetry,
            Reason = "WebSocket invalidate request"
        };

        if (!TelemetryCommandQueue.TryEnqueue(command, out var error))
        {
            await SendCommandQueueFullAsync(client, id, error).ConfigureAwait(false);
            return;
        }

        await SendAsync(client, new
        {
            type = "ack",
            id,
            ok = true,
            status = "queued",
            queuedCommands = TelemetryCommandQueue.Count
        }).ConfigureAwait(false);
    }

    private void HandleSubscribe(ClientConnection client, JsonElement root)
    {
        var channels = new List<string>();
        if (root.TryGetProperty("channels", out var channelArray) &&
            channelArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in channelArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var channel = NormalizeChannel(item.GetString());
                if (IsKnownChannel(channel))
                    channels.Add(channel);
            }
        }

        if (channels.Count == 0)
            channels.Add("train");

        var rateHz = _defaultRateHz;
        if (root.TryGetProperty("rateHz", out var rateElement) &&
            rateElement.TryGetInt32(out var requestedRate))
            rateHz = Clamp(requestedRate, 1, _maxRateHz);

        client.SetChannels(channels, rateHz);
    }

    private async Task HandleCommandAsync(ClientConnection client, JsonElement root, string? id)
    {
        if (!TryParseCommand(root, id, out var command, out var error))
        {
            await SendErrorAsync(client, id, "INVALID_COMMAND", error).ConfigureAwait(false);
            return;
        }

        if (!TelemetryCommandQueue.TryEnqueue(command, out error))
        {
            await SendCommandQueueFullAsync(client, id, error).ConfigureAwait(false);
            return;
        }

        await SendAsync(client, new
        {
            type = "ack",
            id,
            ok = true,
            status = "queued",
            command = string.IsNullOrWhiteSpace(command.Action) ? command.Target : command.Action,
            field = command.Field,
            index = command.Index,
            instance = command.Instance,
            queuedCommands = TelemetryCommandQueue.Count
        }).ConfigureAwait(false);
    }

    private static bool TryParseCommand(
        JsonElement root,
        string? id,
        out TelemetryCommand command,
        out string error)
    {
        command = null!;
        error = "";

        var commandName = NormalizeDriverCommand(TryGetString(root, "command") ?? TryGetString(root, "action"));
        if (!string.IsNullOrWhiteSpace(commandName))
            return TryParseDriverControl(root, id, commandName, out command, out error);

        return TryParsePyscreenWrite(root, id, out command, out error);
    }

    private static bool TryParseDriverControl(
        JsonElement root,
        string? id,
        string action,
        out TelemetryCommand command,
        out string error)
    {
        command = null!;
        error = "";

        var value = 0.0;
        var boolValue = false;
        var requiresNumericValue = DriverCommandRequiresNumericValue(action);
        if (root.TryGetProperty("value", out var valueElement))
        {
            if (valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False)
            {
                if (requiresNumericValue)
                {
                    error = "Driver command value must be numeric";
                    return false;
                }

                boolValue = valueElement.GetBoolean();
            }
            else if (valueElement.TryGetDouble(out var parsedValue))
            {
                value = parsedValue;
                boolValue = Math.Abs(parsedValue) > double.Epsilon;
            }
            else
            {
                error = "Driver command value must be boolean or number";
                return false;
            }
        }
        else
        {
            if (requiresNumericValue)
            {
                error = "Driver command requires numeric value";
                return false;
            }

            boolValue = true;
        }

        command = new TelemetryCommand
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Kind = TelemetryCommandKind.DriverControl,
            Action = action,
            NumberValue = value,
            BoolValue = boolValue
        };
        return true;
    }

    private static bool TryParsePyscreenWrite(
        JsonElement root,
        string? id,
        out TelemetryCommand command,
        out string error)
    {
        command = null!;
        error = "";

        var target = NormalizeCommandTarget(TryGetString(root, "target") ?? TryGetString(root, "group"));
        if (target is not ("eimpcBool" or "eimpcInt" or "eimpcFloat"))
        {
            error = "Command requires a known action, or target must be eimpcBool, eimpcInt, or eimpcFloat";
            return false;
        }

        var field = TryGetString(root, "field");
        int? index = null;
        if (root.TryGetProperty("index", out var indexElement))
        {
            if (!indexElement.TryGetInt32(out var parsedIndex) || parsedIndex < 0)
            {
                error = "Command index must be a non-negative integer";
                return false;
            }

            index = parsedIndex;
        }

        if (string.IsNullOrWhiteSpace(field) && index == null)
        {
            error = "Command requires field or index";
            return false;
        }

        var instance = 0;
        if (root.TryGetProperty("instance", out var instanceElement) && instanceElement.TryGetInt32(out var parsedInstance))
            instance = Math.Max(0, parsedInstance);

        if (!root.TryGetProperty("value", out var valueElement))
        {
            error = "Command requires value";
            return false;
        }

        var numberValue = 0.0;
        var boolValue = false;
        if (target == "eimpcBool")
        {
            if (valueElement.ValueKind == JsonValueKind.True || valueElement.ValueKind == JsonValueKind.False)
                boolValue = valueElement.GetBoolean();
            else if (valueElement.TryGetDouble(out var boolNumber))
                boolValue = Math.Abs(boolNumber) > double.Epsilon;
            else
            {
                error = "eimpcBool value must be boolean or number";
                return false;
            }
        }
        else if (!valueElement.TryGetDouble(out numberValue))
        {
            error = $"{target} value must be numeric";
            return false;
        }

        command = new TelemetryCommand
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Kind = TelemetryCommandKind.PyscreenWrite,
            Target = target,
            Field = string.IsNullOrWhiteSpace(field) ? null : field,
            Index = index,
            Instance = instance,
            NumberValue = numberValue,
            BoolValue = boolValue
        };
        return true;
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = TelemetryState.CurrentSnapshot;
                if (snapshot != null)
                {
                    foreach (var client in _clients.Values.ToArray())
                    {
                        if (!client.ShouldSend()) continue;

                        foreach (var channel in client.GetChannels())
                        {
                            var data = SelectChannel(snapshot, channel);
                            if (data == null) continue;
                            await SendAsync(client, new
                            {
                                type = "state",
                                seq = Interlocked.Increment(ref _sequence),
                                timestampUnixMs = UnixNow(),
                                channel,
                                data
                            }).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.Warning($"[WebSocket] broadcast failure: {ex.Message}");
            }

            try { await Task.Delay(25, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task SendErrorAsync(ClientConnection client, string? id, string code, string message)
    {
        await SendAsync(client, new { type = "error", id, code, message }).ConfigureAwait(false);
    }

    private async Task SendCommandQueueFullAsync(ClientConnection client, string? id, string message)
    {
        await SendAsync(client, new
        {
            type = "error",
            id,
            ok = false,
            error = "COMMAND_QUEUE_FULL",
            code = "COMMAND_QUEUE_FULL",
            message,
            currentQueueSize = TelemetryCommandQueue.Count
        }).ConfigureAwait(false);
    }

    private async Task SendNativeTelemetryDisabledAsync(ClientConnection client, string? id)
    {
        await SendErrorAsync(
            client,
            id,
            "NATIVE_TELEMETRY_DISABLED",
            "Native diagnostics are disabled in this build").ConfigureAwait(false);
    }

    private async Task SendAsync(ClientConnection client, object payload)
    {
        if (client.Socket.State != WebSocketState.Open) return;

        try
        {
            var json = JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await client.SendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    await client.Socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                client.SendLock.Release();
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.Warning($"[WebSocket] send failure client={client.Id}: {ex.Message}");
            _clients.TryRemove(client.Id, out _);
            await client.CloseAsync("send failure").ConfigureAwait(false);
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (string.IsNullOrWhiteSpace(_token)) return true;

        var queryToken = request.QueryString["token"];
        if (string.Equals(queryToken, _token, StringComparison.Ordinal))
            return true;

        var header = request.Headers["Authorization"];
        const string prefix = "Bearer ";
        return header != null &&
            header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(header.Substring(prefix.Length), _token, StringComparison.Ordinal);
    }

    private static object? SelectChannel(TelemetrySnapshot snapshot, string channel) => channel switch
    {
        "telemetry" or "full" => snapshot,
        "train" => snapshot.Train,
        "brakes" => snapshot.Brakes,
        "electrical" or "power" => snapshot.Electrical,
        "safety" => snapshot.Safety,
        "doors" => snapshot.Doors,
        "controls" => snapshot.Controls,
        "station" => snapshot.Station,
        "environment" => snapshot.Environment,
        "lights" => new
        {
            snapshot.Controls?.LightsFront,
            snapshot.Controls?.LightsRear,
            snapshot.Controls?.LightsCompartments,
            snapshot.Environment?.LightLevel,
            snapshot.Environment?.ScreenBrightness
        },
        "radio" => new
        {
            snapshot.Environment?.RadioActive,
            snapshot.Environment?.RadioChannel,
            snapshot.Environment?.RadioVolume,
            snapshot.Environment?.RadioNoise,
            snapshot.Environment?.RadioNightMode,
            snapshot.Environment?.RadioVolumeMode
        },
        "signals" => new
        {
            source = "pyscreen-safety",
            snapshot.Safety?.SHP,
            snapshot.Safety?.CA,
            snapshot.Safety?.AlarmActive,
            note = "Current signal/aspect is not exposed yet; this channel currently carries available cab safety indicators."
        },
        "status" => new
        {
            snapshot.IsActive,
            snapshot.Timestamp,
            snapshot.Status
        },
        _ => null
    };

    private static bool IsKnownChannel(string channel) =>
        channel is "telemetry" or "full" or "train" or "brakes" or "electrical" or "power" or
            "safety" or "doors" or "controls" or "station" or "environment" or "lights" or
            "radio" or "signals" or "status";

    private static string NormalizeChannel(string? channel) =>
        (channel ?? "").Trim().ToLowerInvariant();

    private static string NormalizeCommandTarget(string? value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "setpyscreenbool" or "setpyscreeneimpcbool" or "eimpcbool" => "eimpcBool",
            "setpyscreenint" or "setpyscreeneimpcint" or "eimpcint" => "eimpcInt",
            "setpyscreenfloat" or "setpyscreeneimpcfloat" or "eimpcfloat" => "eimpcFloat",
            var other => other
        };
    }

    private static string NormalizeDriverCommand(string? value)
    {
        return (value ?? "").Trim().ToLowerInvariant() switch
        {
            "emergencybrake" or "emergency_brake" or "eb" => "emergencyBrake",
            "nopowerandbrake" or "no_power_and_brake" => "noPowerAndBrake",
            "setpower" or "power" or "traction" => "setPower",
            "setbrake" or "brake" or "trainbrake" => "setBrake",
            "setlocalbrake" or "localbrake" or "locomotivebrake" => "setLocalBrake",
            "setthirdbrake" or "thirdbrake" or "epbrake" => "setThirdBrake",
            "setedbrake" or "edbrake" => "setEdBrake",
            "setdirection" or "direction" => "setDirection",
            "setspeedtarget" or "speedtarget" or "cruise" or "atospeed" => "setSpeedTarget",
            "alerter" or "security" or "securitysystem" or "shp" or "ca" => "securityAcknowledge",
            "sanding" or "setsanding" => "setSanding",
            "horn" => "horn",
            "radiostop" => "radioStop",
            "etcsack" or "etcs_ack" => "etcsAck",
            "springbrake" or "setspringbrake" => "setSpringBrake",
            "vcb" or "mainswitch" or "setvcb" => "setVcb",
            "battery" or "setbattery" => "setBattery",
            "converter" or "setconverter" => "setConverter",
            "compressor" or "setcompressor" => "setCompressor",
            "pantographfront" or "frontpantograph" or "setfrontpantograph" => "setFrontPantograph",
            "pantographrear" or "rearpantograph" or "setrearpantograph" => "setRearPantograph",
            _ => ""
        };
    }

    private static bool DriverCommandRequiresNumericValue(string action) =>
        action is "setPower" or "setBrake" or "setLocalBrake" or "setThirdBrake" or
            "setEdBrake" or "setDirection" or "setSpeedTarget";

    private static string? TryGetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }

    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    private static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private sealed class ClientConnection
    {
        private readonly object _gate = new();
        private readonly int _maxRateHz;
        private HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
        private int _rateHz;
        private DateTime _nextSendUtc = DateTime.UtcNow;
        public ClientConnection(WebSocket socket, int defaultRateHz, int maxRateHz)
        {
            Socket = socket;
            _rateHz = defaultRateHz;
            _maxRateHz = maxRateHz;
        }

        public string Id { get; } = Guid.NewGuid().ToString("N");
        public WebSocket Socket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);

        public void SetChannels(IEnumerable<string> channels, int rateHz)
        {
            lock (_gate)
            {
                _channels = new HashSet<string>(channels, StringComparer.OrdinalIgnoreCase);
                _rateHz = rateHz <= 0 ? _rateHz : Clamp(rateHz, 1, _maxRateHz);
                _nextSendUtc = DateTime.UtcNow;
            }
        }

        public string[] GetChannels()
        {
            lock (_gate)
                return _channels.ToArray();
        }

        public bool ShouldSend()
        {
            lock (_gate)
            {
                if (_channels.Count == 0) return false;
                var now = DateTime.UtcNow;
                if (now < _nextSendUtc) return false;
                _nextSendUtc = now.AddMilliseconds(1000.0 / Math.Max(1, _rateHz));
                return true;
            }
        }

        public async Task CloseAsync(string reason)
        {
            try
            {
                if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.CloseReceived)
                {
                    await Socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        reason,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch { }
            try { Socket.Dispose(); } catch { }
        }
    }
}
