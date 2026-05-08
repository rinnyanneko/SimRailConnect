# SimRailConnect WebSocket API

Intended URL: `ws://localhost:5556/ws`

This contract is retained for plugin development. The core plugin starts the API and publishes an inactive baseline snapshot until a telemetry provider publishes live data.

## Safe Mode

This managed-only core build does not include native telemetry or write support.

Supported messages:

- `ping`
- `subscribe`
- `unsubscribe`
- `getSnapshot`

Disabled native-dependent messages:

- `command`
- `debug`
- `invalidate`

Disabled messages return:

```json
{
  "type": "error",
  "id": "request-id",
  "code": "NATIVE_TELEMETRY_DISABLED",
  "message": "Native telemetry is not included in this managed-only plugin build"
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
  "timestampUnixMs": 1714300000100
}
```

The core build returns an inactive `data` snapshot until a separate telemetry provider calls `TelemetryState.PublishSnapshot`.

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
