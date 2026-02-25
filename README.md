<div align="center">
<img width="1200" height="475" alt="GHBanner" src="https://github.com/user-attachments/assets/0aa67016-6eaf-458a-adb2-6e31a0763ed6" />
</div>

# Okey Rummy — Multiplayer

A real-time multiplayer Okey Rummy game built with **Godot 4.5 (C#/.NET)** and **self-hosted Supabase** for accounts, friends, and presence.

## Prerequisites

- [Godot 4.5 Mono](https://godotengine.org/download) (C# support)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (for self-hosted Supabase)

## Quick Start

### 1. Start Supabase (Backend)

```bash
cd supabase
cp .env.example .env
# Edit .env — set POSTGRES_PASSWORD, JWT_SECRET, and IDP credentials
docker compose up -d
```

Services will be available at:
| Service | URL |
|---------|-----|
| API Gateway | http://localhost:8000 |
| Auth | http://localhost:8000/auth/v1/ |
| REST API | http://localhost:8000/rest/v1/ |
| Realtime | http://localhost:8000/realtime/v1/ |
| Database | localhost:5432 |

### 2. Configure the Game Client

Edit `godot/Core/Networking/AccountManager.cs`:
```csharp
private const string SUPABASE_URL = "http://your-server:8000";
private const string SUPABASE_ANON_KEY = "your-anon-key";
```

### 3. Build & Run

```bash
cd godot
dotnet build
# Open in Godot Editor, or run directly:
/path/to/Godot --path .
```

## Architecture

```
┌─────────────┐     ┌──────────────────────┐
│ Godot Client│────▶│ Game Server (ENet)    │
│  LoginScreen│     │  NetworkManager       │
│  LobbyUI    │     │  MatchManager         │
│  MainEngine │     └──────────────────────┘
└──────┬──────┘
       │ HTTP/REST
┌──────▼──────────────────────────┐
│ Self-Hosted Supabase            │
│  ├─ Auth (GoTrue) — OAuth/Guest│
│  ├─ PostgREST — Profiles/Friends│
│  ├─ Realtime — Presence         │
│  └─ PostgreSQL — All data       │
└─────────────────────────────────┘
```

## Features

- **Authentication**: Google, Facebook, Discord, Twitch OAuth + Guest (device ID)
- **Friends**: Search, send/accept/decline requests, block/unblock
- **Presence**: Online/In-Game/Away/Offline status with 30s heartbeat
- **Privacy**: Control who sees your status, profile, and friend requests
- **Multiplayer**: Real-time 4-player Okey with drag-and-drop, animations, bot AI
- **Bot Replacement**: Disconnected players auto-replaced by bot after 1 missed turn

## Identity Providers Setup

Enable IDPs by setting credentials in `supabase/.env`:

| Provider | Env Vars |
|----------|----------|
| Google | `GOOGLE_ENABLED=true`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` |
| Facebook | `FACEBOOK_ENABLED=true`, `FACEBOOK_CLIENT_ID`, `FACEBOOK_CLIENT_SECRET` |
| Discord | `DISCORD_ENABLED=true`, `DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET` |
| Twitch | `TWITCH_ENABLED=true`, `TWITCH_CLIENT_ID`, `TWITCH_CLIENT_SECRET` |

## Testing Multiplayer

```bash
# Headless server + 3 GUI clients
mkdir -p logs
/path/to/Godot --headless --path godot -- --server --test-mode > logs/server.log 2>&1 &
/path/to/Godot --path godot -- --no-server &
/path/to/Godot --path godot -- --no-server &
/path/to/Godot --path godot -- --no-server &
```
