# Quasar Web Bridge

A bridge server that enables web-based client sessions for the Quasar Relay system.

## Overview

This server allows Quasar servers to create web sessions, which can be accessed through a browser to establish remote connections via the Quasar relay infrastructure.

## Features

- Create web sessions with one-time use capabilities
- Web-based client interface that connects to Quasar relay servers
- Automatic session cleanup and expiration
- Docker containerization for easy deployment
- Configurable relay server endpoints

## Installation

### Prerequisites

- Docker and Docker Compose
- Node.js (for local development)

### Deployment with Docker

1. Make sure Docker is installed on your system
2. Ensure the `quasar-network` Docker network exists (or modify the docker-compose.yml to use your network)
3. Deploy using Docker Compose:

```bash
docker-compose up -d
```

### Local Development

1. Install dependencies:

```bash
npm install
```

2. Start the server:

```bash
npm start
```

## Configuration

Configuration is managed through the `config.json` file:

```json
{
  "port": 3000,
  "relayServer": "ws://quasar-relay-1:8080", 
  "sessionExpiryMinutes": 30,
  "logging": {
    "level": "info",
    "directory": "./logs"
  }
}
```

- `port`: HTTP server port
- `relayServer`: WebSocket URL of the Quasar relay server
- `sessionExpiryMinutes`: Session validity period in minutes
- `logging`: Log configuration settings

## API Endpoints

### Create Session

**POST** `/session/create`

Creates a new web client session.

**Request Body:**
```json
{
  "serverId": "unique-server-id",
  "sessionName": "Optional Session Name",
  "expiresInMinutes": 30,
  "oneTimeSession": true
}
```

**Response:**
```json
{
  "sessionKey": "generated-session-key",
  "sessionName": "Session Name",
  "serverId": "unique-server-id",
  "expiresAt": 1717268142363,
  "sessionUrl": "http://localhost:3000/client/connect?sessionKey=generated-session-key"
}
```

### Client Connect Page

**GET** `/client/connect?sessionKey=<session-key>`

HTML page that allows users to connect to a relay session using the provided session key.

### Validate Session

**GET** `/api/session/:sessionKey`

Validates a session key and returns session information.

**Response:**
```json
{
  "valid": true,
  "sessionKey": "session-key",
  "sessionName": "Session Name",
  "serverId": "server-id",
  "relayServer": "ws://quasar-relay-1:8080"
}
```

### Health Check

**GET** `/health`

Simple health check endpoint.

## Integration with SilentRemote

To integrate with SilentRemote:

1. Update the SilentRemote.Server's RelaySignalingService.cs to create web sessions via the bridge server:

```csharp
// In RegisterWebSessionAsync method
var httpClient = new HttpClient();
var request = new HttpRequestMessage(HttpMethod.Post, "http://quasar-web-bridge:3000/session/create");
request.Content = new StringContent(JsonConvert.SerializeObject(new
{
    serverId,
    sessionName,
    expiresInMinutes,
    oneTimeSession
}), Encoding.UTF8, "application/json");

var response = await httpClient.SendAsync(request);
if (response.IsSuccessStatusCode)
{
    var content = await response.Content.ReadAsStringAsync();
    var session = JsonConvert.DeserializeObject<WebSessionInfo>(content);
    
    return new WebSessionInfo
    {
        SessionKey = session.SessionKey,
        SessionName = session.SessionName,
        ServerId = session.ServerId,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddMinutes(expiresInMinutes),
        RelayUrl = session.RelayUrl, // This will be the relay WebSocket URL
        OneTimeSession = oneTimeSession
    };
}
```

2. Ensure the network configuration allows SilentRemote.Server to communicate with the quasar-web-bridge container.

## Security Considerations

- The bridge server does not implement authentication for session creation
- Consider implementing authentication for the `/session/create` endpoint in production
- Use HTTPS in production for secure communication
- Consider network isolation to prevent unauthorized access to the bridge server
