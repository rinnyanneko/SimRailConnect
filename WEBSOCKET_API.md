# SimRailConnect WebSocket API

Default URL: `ws://localhost:5556/ws`

WebSocket is the only network API. The previous HTTP REST listener has been removed.

## Native Telemetry Safe Mode

`EnableTelemetryPatch` defaults to `false` and is currently ignored by this managed-only plugin build. The WebSocket server does not reference `GameBridge`, Harmony telemetry, Unity, IL2CPP wrappers, native pointers, or `Marshal`.

While safe mode is active:

- `subscribe`, `unsubscribe`, `ping`, and `getSnapshot` still work.
- `getSnapshot` returns the last managed snapshot, which is normally inactive/empty until native telemetry has been enabled.
- `command`, `debug`, and `invalidate` return:

```json
{
  "type": "error",
  "id": "cmd-001",
  "code": "NATIVE_TELEMETRY_DISABLED",
  "message": "Native telemetry is disabled by EnableTelemetryPatch=false"
}
```

Native telemetry will need to return as a separate optional assembly after the SimRail/MelonLoader IL2CPP support-module crash is isolated.

## Threading Model

Read path:
Unity main thread reads IL2CPP/native data in `GameBridge`, builds `TelemetryState.CurrentSnapshot`, and the WebSocket background sender serializes that managed snapshot to clients.

Write path:
WebSocket `command` messages are parsed and validated on a network thread, queued as managed commands, applied from the next `Pyscreen.Update()` telemetry tick on the Unity main thread, then returned as `commandResult`.

WebSocket code must not touch Unity objects, IL2CPP wrappers, native pointers, or `Marshal`.

## Subscribe

```json
{
  "type": "subscribe",
  "id": "sub-001",
  "channels": ["train", "brakes", "doors", "safety"],
  "rateHz": 10
}
```

Ack:

```json
{
  "type": "ack",
  "id": "sub-001",
  "ok": true
}
```

State push:

```json
{
  "type": "state",
  "seq": 12345,
  "timestampUnixMs": 1714300000000,
  "channel": "train",
  "data": {
    "velocity": 87.4,
    "velocityInt": 87,
    "acceleration": 0.12,
    "direction": 1
  }
}
```

Channels:

`train`, `brakes`, `electrical`, `power`, `safety`, `doors`, `controls`, `station`, `environment`, `lights`, `radio`, `signals`, `status`, `telemetry`, `full`.

`signals` currently carries available cab safety indicators. Current external signal/aspect is not exposed yet because it needs a separate safe main-thread cache design.

## Snapshot Query

```json
{
  "type": "getSnapshot",
  "id": "snap-001"
}
```

Response:

```json
{
  "type": "snapshot",
  "id": "snap-001",
  "ok": true,
  "seq": 12346,
  "timestampUnixMs": 1714300000100,
  "data": {}
}
```

## Invalidate Cache

Schedules cached native references to be dropped on the next Unity main-thread telemetry tick.

```json
{
  "type": "invalidate",
  "id": "inv-001"
}
```

Response:

```json
{
  "type": "ack",
  "id": "inv-001",
  "ok": true,
  "queued": true,
  "message": "Cache invalidation scheduled for the next main-thread tick"
}
```

## Debug Diagnostics

Requests native cache diagnostics. The WebSocket handler only queues the request; `GameBridge` builds the debug snapshot on the Unity main thread.

```json
{
  "type": "debug",
  "id": "debug-001"
}
```

Response:

```json
{
  "type": "debug",
  "id": "debug-001",
  "ok": true,
  "timestampUnixMs": 1714300000000,
  "data": {
    "dataSourceFound": true,
    "dataFieldOffset": 40,
    "arrayDataOffset": 32
  }
}
```

## Commands

Action-style command:

```json
{
  "type": "command",
  "id": "cmd-001",
  "target": "brakes",
  "action": "set_brake",
  "value": 4
}
```

Compatibility field-style command:

```json
{
  "type": "command",
  "id": "cmd-002",
  "target": "generalBool",
  "field": "shp",
  "value": true
}
```

Queued ack:

```json
{
  "type": "ack",
  "id": "cmd-001",
  "ok": true,
  "queued": true
}
```

Applied result:

```json
{
  "type": "commandResult",
  "id": "cmd-001",
  "ok": true,
  "queued": true,
  "applied": true,
  "target": "float",
  "field": "pneumatic_brake_status",
  "value": 4
}
```

Error:

```json
{
  "type": "error",
  "id": "cmd-001",
  "code": "VALUE_OUT_OF_RANGE",
  "message": "Value must be between 0 and 10"
}
```

Supported action commands:

| Target | Action | Value |
|---|---|---|
| `train` | `set_throttle` | integer `-100..100` |
| `train` | `set_reverser`, `set_direction` | integer `-1..1` |
| `brakes` | `set_brake`, `set_train_brake` | number `0..10` |
| `controls` | `set_speed_control` | number `0..250` |
| `controls` | `set_speed_control_power` | number `0..1` |
| `controls` | `set_speed_control_active`, `set_sanding` | boolean |
| `safety` | `set_shp`, `set_ca`, `set_alarm` | boolean |
| `lights` | `set_front`, `set_rear` | integer `0..10` |
| `lights` | `set_compartments` | boolean |
| `radio` | `set_channel` | integer `0..99` |
| `radio` | `set_active` | boolean |
| `radio` | `set_volume` | number `0..1` |

Writes are conservative and whitelist-only. They write Pyscreen dashboard/display arrays, so they may affect visible cab/dashboard values rather than physical simulation state.

## Ping/Pong

```json
{
  "type": "ping",
  "id": "ping-001"
}
```

```json
{
  "type": "pong",
  "id": "ping-001",
  "timestampUnixMs": 1714300000000
}
```

## Security and Limits

Defaults:

| Setting | Default |
|---|---|
| `WebSocketPort` | `5556` |
| `WebSocketMaxClients` | `3` |
| `WebSocketDefaultRateHz` | `10` |
| `WebSocketMaxRateHz` | `20` |
| `WebSocketPayloadLimitBytes` | `16384` |
| `WebSocketCommandRateLimitPerSecond` | `5` |
| `WebSocketReadOnly` | `false` |
| `EnableTelemetryPatch` | `false` |
| `ApiToken` | empty |

The WebSocket listener binds to `localhost` only. If `ApiToken` is set, clients must pass either `?token=<token>` in the URL or `Authorization: Bearer <token>`.

## Debug Examples

Using `websocat`:

```sh
websocat ws://localhost:5556/ws
```

Using `wscat`:

```sh
wscat -c ws://localhost:5556/ws
```

Subscribe after connecting:

```json
{"type":"subscribe","id":"sub-001","channels":["train","brakes"],"rateHz":10}
```

Request a full snapshot:

```json
{"type":"getSnapshot","id":"snap-001"}
```
