<!-- SPDX-License-Identifier: GPL-3.0-or-later -->

# SimRailConnect WebSocket API

Default URL: `ws://localhost:5556/ws`

The server publishes read-only telemetry snapshots collected from SimRail Pyscreen data on the Unity main thread. WebSocket handlers run on background threads and only read the latest snapshot.

Command messages are also handled safely: WebSocket handlers validate and queue them, then the Unity main thread applies them during the telemetry tick.

## Supported Messages

- `ping`
- `subscribe`
- `unsubscribe`
- `getSnapshot`
- `command`
- `invalidate`

## Request Ids

Most client requests may include an optional string `id`. The server echoes this value in the response so clients can match responses to requests. SimRailConnect does not interpret `id` as a train id, game object id, command id, or session id.

If `id` is omitted, responses include `"id": null`. To correlate responses to requests, always provide an `id`.

Disabled messages:

- `debug`

Disabled messages return:

```json
{
  "type": "error",
  "id": "request-id",
  "code": "NATIVE_TELEMETRY_DISABLED",
  "message": "Native diagnostics are disabled in this build"
}
```

## Hello

After connection, the server sends:

```json
{
  "type": "hello",
  "clientId": "client-id",
  "url": "ws://localhost:5556/ws"
}
```

## Ping

Request:

```json
{
  "type": "ping",
  "id": "ping-001"
}
```

Response:

```json
{
  "type": "pong",
  "id": "ping-001",
  "timestampUnixMs": 1714300000000
}
```

## Subscribe

Request:

```json
{
  "type": "subscribe",
  "id": "sub-001",
  "channels": ["train", "brakes", "doors", "safety"],
  "rateHz": 10
}
```

Response:

```json
{
  "type": "ack",
  "id": "sub-001",
  "ok": true
}
```

Known channels:

`train`, `brakes`, `electrical`, `power`, `safety`, `doors`, `controls`, `station`, `environment`, `lights`, `radio`, `signals`, `status`, `telemetry`, `full`.

State push shape:

```json
{
  "type": "state",
  "seq": 12345,
  "timestampUnixMs": 1714300000000,
  "channel": "train",
  "data": {}
}
```

## Unsubscribe

Request:

```json
{
  "type": "unsubscribe",
  "id": "unsub-001"
}
```

Response:

```json
{
  "type": "ack",
  "id": "unsub-001",
  "ok": true
}
```

## Snapshot

Request:

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
  "data": {
    "timestamp": "2024-04-28T11:06:40Z",
    "isActive": true,
    "status": "OK",
    "train": {},
    "brakes": {},
    "electrical": {},
    "safety": {},
    "doors": {},
    "controls": {},
    "station": {},
    "environment": {}
  }
}
```

Before a train Pyscreen source exists, `data.isActive` is `false` and `data.status` describes what the collector is waiting for.

## Command

Commands are queued. An `ack` means the request passed validation and entered the queue; it does not mean SimRail has already consumed the value.

Supported write targets:

- `eimpcBool`
- `eimpcInt`
- `eimpcFloat`

Supported named driver commands:

- `emergencyBrake`
- `noPowerAndBrake`
- `setPower`
- `setBrake`
- `setLocalBrake`
- `setThirdBrake`
- `setEdBrake`
- `setDirection`
- `setSpeedTarget`
- `securityAcknowledge`
- `setSanding`
- `horn`
- `radioStop`
- `etcsAck`
- `setSpringBrake`
- `setVcb`
- `setBattery`
- `setConverter`
- `setCompressor`
- `setFrontPantograph`
- `setRearPantograph`

Named driver command example:

```json
{
  "type": "command",
  "id": "cmd-power-001",
  "command": "setPower",
  "value": 0.35
}
```

Emergency brake example:

```json
{
  "type": "command",
  "id": "cmd-eb-001",
  "command": "emergencyBrake",
  "value": true
}
```

VCB/main switch example:

```json
{
  "type": "command",
  "id": "cmd-vcb-001",
  "command": "setVcb",
  "value": true
}
```

Request using a field name:

```json
{
  "type": "command",
  "id": "cmd-001",
  "target": "eimpcBool",
  "field": "batt",
  "value": true
}
```

Request using a raw field index:

```json
{
  "type": "command",
  "id": "cmd-002",
  "target": "eimpcFloat",
  "index": 10,
  "value": 1.0
}
```

Queued response:

```json
{
  "type": "ack",
  "id": "cmd-001",
  "ok": true,
  "status": "queued",
  "command": "eimpcBool",
  "field": "batt",
  "instance": 0,
  "queuedCommands": 1
}
```

If the command queue is full, the server rejects the request instead of enqueueing it. Clients should retry with backoff.

```json
{
  "type": "error",
  "id": "cmd-001",
  "ok": false,
  "error": "COMMAND_QUEUE_FULL",
  "code": "COMMAND_QUEUE_FULL",
  "message": "Command queue is full",
  "currentQueueSize": 128
}
```

The queue-size field is optional and may also be named `queuedCommands`.

Known fields:

| Target | Fields |
|---|---|
| `eimpcBool` | `ms`, `heat`, `batt`, `conv`, `comp`, `comp_shutoff` |
| `eimpcInt` | `motor_isactive` |
| `eimpcFloat` | `fr`, `ihv`, `uhv`, `frt`, `frb`, `cv`, `ci`, `motor_fan_status`, `tcu_status`, `motor_temp`, `pantograph_front_status`, `pantograph_rear_status` |

## Invalidate

Invalidation is queued and applied on the Unity main thread.

```json
{
  "type": "invalidate",
  "id": "inv-001"
}
```

Successful invalidation returns:

```json
{
  "type": "ack",
  "id": "inv-001",
  "ok": true,
  "status": "queued",
  "queuedCommands": 1
}
```

If the command queue is full, invalidation fails with `COMMAND_QUEUE_FULL`; clients should retry with backoff.

```json
{
  "type": "error",
  "id": "inv-001",
  "ok": false,
  "error": "COMMAND_QUEUE_FULL",
  "code": "COMMAND_QUEUE_FULL",
  "message": "Command queue is full",
  "currentQueueSize": 128
}
```

## Auth

If `ApiToken` is configured, clients must provide either:

- `ws://localhost:5556/ws?token=<token>`
- `Authorization: Bearer <token>`

## Limits

| Setting | Default |
|---|---|
| `WebSocketPort` | `5556` |
| `WebSocketMaxClients` | `3` |
| `WebSocketDefaultRateHz` | `10` |
| `WebSocketMaxRateHz` | `20` |
| `WebSocketPayloadLimitBytes` | `16384` |
| `ApiToken` | empty |
