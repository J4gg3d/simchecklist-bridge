# MSFSBridge

Deutsch | **[English](README.md)**

Bridge-Server, der Microsoft Flight Simulator 2024 mit [SimChecklist](https://simchecklist.app) verbindet.

## Was macht die Bridge?

Die Bridge läuft **lokal auf deinem PC** und:

1. **Liest Flugdaten aus MSFS 2024** via SimConnect (die offizielle Microsoft API)
2. **Sendet diese Daten per WebSocket** an die SimChecklist-Website

### Übertragene Daten

| Kategorie | Daten |
|-----------|-------|
| **Position** | Latitude, Longitude, Altitude, Ground Altitude |
| **Geschwindigkeit** | Ground Speed, Vertical Speed, Airspeed |
| **Attitude** | Pitch, Bank, Heading, Angle of Attack |
| **G-Kräfte** | Vertical G, Lateral G, Longitudinal G |
| **Status** | On Ground, Gear Position, Flaps, Engines Running |
| **Systeme** | Lichter, Elektrik, APU, Anti-Ice, Transponder |
| **ATC** | Callsign, Airline, Flugnummer |
| **GPS/Flugplan** | Waypoints, Ziel, ETE |

### Was wird NICHT übertragen?

- Keine persönlichen Daten von deinem PC
- Keine Dateien außerhalb von MSFS
- Keine Screenshots oder Bildschirminhalte
- Keine Tastatureingaben

## Architektur

```
┌─────────────────┐     SimConnect     ┌─────────────────┐
│  MSFS 2024      │◄──────────────────►│  Bridge         │
│  (Simulator)    │                    │  (Diese App)    │
└─────────────────┘                    └────────┬────────┘
                                                │
                                                │ WebSocket (Port 8080)
                                                │
                                                ▼
                                       ┌─────────────────┐
                                       │  SimChecklist   │
                                       │  (Browser)      │
                                       └─────────────────┘
```

## Features

- **Auto-Connect**: Verbindet sich automatisch mit MSFS wenn es läuft
- **Auto-Retry**: Versucht alle 5 Sekunden erneut, wenn MSFS nicht läuft
- **Auto-Update**: Prüft beim Start auf neue Versionen und aktualisiert auf Knopfdruck
- **Landing Detection**: Erkennt Landungen automatisch und bewertet sie (1-5 Sterne)
- **Glidepath Recording**: Zeichnet die letzten 60 Sekunden des Anflugs auf
- **Flight Logging**: Speichert Flüge automatisch wenn du bei SimFlyCorp eingeloggt bist
- **Demo Mode**: Simulierte Daten wenn MSFS nicht läuft (zum Testen)

## Download

Lade die neueste Version von [Releases](https://github.com/J4gg3d/simchecklist-bridge/releases) herunter.

## Schnellstart

1. Lade `MSFSBridge-vX.X.X.zip` herunter und entpacke es
2. Starte `MSFSBridge.exe`
3. Öffne https://simchecklist.app im Browser
4. Die Website verbindet sich automatisch mit der Bridge

## Konfiguration

Erstelle eine `.env` Datei im gleichen Ordner wie `MSFSBridge.exe` für optionale Einstellungen:

```env
# Benutzerdefinierte Ports (wenn Standard-Ports blockiert sind)
WEBSOCKET_PORT=8090
HTTP_PORT=8091
```

### Port-Konfiguration

Die Standard-Ports sind:
- **WebSocket**: 8080
- **HTTP** (für Tablets): 8081

Wenn Port 8080 blockiert ist (häufig bei Hyper-V oder Docker), setze einen benutzerdefinierten Port in `.env` und konfiguriere den gleichen Port in den Website-Einstellungen.

## Tablet-Zugriff

### Option A: Über simchecklist.app (Empfohlen)
1. Öffne https://simchecklist.app auf deinem PC
2. Starte die Bridge
3. Website verbindet sich automatisch

### Option B: Lokales Netzwerk (für Tablets)
1. Starte die Bridge auf deinem PC
2. Notiere die URL aus der Konsole (z.B. `http://192.168.1.100:8081`)
3. Öffne diese URL auf deinem Tablet
4. PC und Tablet müssen im gleichen WLAN sein

**Hinweis**: Der HTTP-Server für Tablets benötigt einen `www` Ordner mit Website-Dateien. Ohne diesen nutze einfach https://simchecklist.app direkt.

## Befehle

Während die Bridge läuft:

| Taste | Aktion |
|-------|--------|
| `C` | Mit MSFS verbinden (manuell) |
| `D` | Von MSFS trennen |
| `R` | Auto-Retry aktivieren |
| `S` | Status anzeigen |
| `Q` | Beenden |

## Landing Rating System

| Rating | Sinkrate | Sterne |
|--------|----------|--------|
| Perfect | < 100 ft/min | ★★★★★ |
| Good | 100-200 ft/min | ★★★★☆ |
| Acceptable | 200-300 ft/min | ★★★☆☆ |
| Hard | 300-500 ft/min | ★★☆☆☆ |
| Very Hard | > 500 ft/min | ★☆☆☆☆ |

## Firewall

Wenn die Bridge nicht verbinden kann, musst du sie ggf. durch die Windows-Firewall erlauben:

1. Öffne die Windows-Firewall-Einstellungen
2. Klicke auf "Eine App durch die Firewall zulassen"
3. Füge `MSFSBridge.exe` hinzu
4. Aktiviere für Private Netzwerke

## Voraussetzungen

- Windows 10/11
- Microsoft Flight Simulator 2024
- .NET 8.0 Runtime (in Windows 11 enthalten)

## Selbst kompilieren

```bash
# Repository klonen
git clone https://github.com/J4gg3d/simchecklist-bridge.git
cd simchecklist-bridge

# Bauen
dotnet build

# Starten
dotnet run
```

SimConnect DLLs sind im `libs/` Ordner enthalten.

## Problembehandlung

### Bridge verbindet nicht mit MSFS
1. Stelle sicher, dass MSFS 2024 läuft (nicht nur der Launcher)
2. Starte die Bridge neu
3. Prüfe ob .NET 8 Runtime installiert ist

### "Port bereits in Verwendung"
Eine andere Anwendung nutzt Port 8080. Entweder:
- Schließe die andere Anwendung
- Setze einen benutzerdefinierten Port in `.env`

### Website zeigt "Nicht verbunden"
1. Stelle sicher, dass die Bridge läuft
2. Prüfe ob Port 8080 in der Firewall erlaubt ist
3. Bei benutzerdefiniertem Port: Stelle sicher, dass er in den Website-Einstellungen übereinstimmt

## Datenschutz

- Alle Daten bleiben in deinem lokalen Netzwerk
- Die Bridge sendet keine Daten an externe Server (außer optional an SimFlyCorp wenn aktiviert)
- Keine Tracking-Cookies, keine Analytics
- Der Quellcode ist vollständig einsehbar

## Lizenz

MIT License - siehe [LICENSE](LICENSE)

## Links

- [SimChecklist.app](https://simchecklist.app) - Die Hauptanwendung
- [SimFlyCorp](https://simchecklist.app/simflycorp/info) - Piloten-Karriere-System
- [Discord](https://discord.gg/4YzfNCcU) - Community & Support
