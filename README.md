# SimChecklist Bridge

Eine lokale Bridge-Anwendung, die Microsoft Flight Simulator 2024 mit [SimChecklist.app](https://simchecklist.app) verbindet.

## Was macht die Bridge?

Die Bridge läuft **lokal auf deinem PC** und:

1. **Liest Flugdaten aus MSFS 2024** via SimConnect (die offizielle Microsoft API)
2. **Sendet diese Daten per WebSocket** an die SimChecklist-Website

### Übertragene Daten

Die Bridge liest folgende Daten aus dem Simulator:

| Kategorie | Daten |
|-----------|-------|
| **Position** | Latitude, Longitude, Altitude, Ground Altitude |
| **Geschwindigkeit** | Ground Speed, Vertical Speed, Airspeed |
| **Attitude** | Pitch, Bank, Heading, Angle of Attack |
| **G-Kräfte** | Vertical G, Lateral G, Longitudinal G |
| **Status** | On Ground, Gear Position, Flaps, Engines Running |
| **Systeme** | Lights, Electrical, APU, Anti-Ice, Transponder |
| **ATC** | Callsign, Airline, Flight Number |
| **GPS/Flugplan** | Waypoints, Destination, ETE |

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

## Auto-Update

Die Bridge prüft beim Start automatisch ob eine neue Version verfügbar ist:

```
╔══════════════════════════════════════════════════════════════╗
║                    UPDATE VERFÜGBAR!                         ║
╚══════════════════════════════════════════════════════════════╝
  Aktuelle Version: 1.5.0
  Neue Version:     v1.6.0

  [J] Jetzt aktualisieren
  [N] Später (Bridge normal starten)
  [I] Im Browser öffnen
```

Bei Auswahl von **[J]** wird das Update automatisch heruntergeladen, installiert und die Bridge neu gestartet.

## Installation

### Voraussetzungen

- Windows 10/11
- [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (nicht SDK, nur Runtime)
- Microsoft Flight Simulator 2024

### Option A: Fertige Release nutzen

1. Lade die neueste Release von der [Releases-Seite](https://github.com/J4gg3d/simchecklist-bridge/releases) herunter
2. Entpacke die ZIP-Datei
3. Starte `MSFSBridge.exe` oder `start-bridge.bat`

### Option B: Selbst kompilieren

```bash
# Repository klonen
git clone https://github.com/J4gg3d/simchecklist-bridge.git
cd simchecklist-bridge

# Bauen
dotnet build

# Starten
dotnet run
```

Die SimConnect DLLs sind bereits im `libs/` Ordner enthalten.

## Konfiguration

Die Bridge funktioniert ohne Konfiguration für die Grundfunktionen.

Für erweiterte Features (Flight-Logging zu SimFlyCorp) erstelle eine `.env` Datei:

```bash
cp .env.example .env
# Dann die Werte in .env anpassen
```

## Port

Die Bridge verwendet **Port 8080** für WebSocket-Verbindungen. Falls dieser Port blockiert ist, musst du ihn in deiner Firewall freigeben.

## WebSocket-Protokoll

### Vom Client zur Bridge

```json
{ "type": "route", "data": { "origin": "EDDF", "destination": "KJFK" } }
{ "type": "getAirport", "data": "EDDF" }
{ "type": "ping" }
```

### Von der Bridge zum Client

```json
// Flugdaten (werden kontinuierlich gesendet)
{
  "altitude": 35000,
  "groundSpeed": 450,
  "heading": 270,
  "latitude": 50.0379,
  "longitude": 8.5622,
  "verticalSpeed": 0,
  "onGround": false,
  // ... weitere Felder
}

// Landing-Event
{
  "type": "landing",
  "landing": {
    "verticalSpeed": -150,
    "gForce": 1.2,
    "rating": "Good",
    "ratingScore": 4
  }
}

// Route-Sync
{ "type": "route", "route": { "origin": "EDDF", "destination": "KJFK" } }

// Flughafen-Koordinaten
{ "type": "airportCoords", "icao": "EDDF", "coords": { "lat": 50.0379, "lon": 8.5622 } }
```

## Landing Rating System

| Rating | Sinkrate | Sterne |
|--------|----------|--------|
| Perfect | < 100 ft/min | ★★★★★ |
| Good | 100-200 ft/min | ★★★★☆ |
| Acceptable | 200-300 ft/min | ★★★☆☆ |
| Hard | 300-500 ft/min | ★★☆☆☆ |
| Very Hard | > 500 ft/min | ★☆☆☆☆ |

## Troubleshooting

### Bridge verbindet nicht mit MSFS

1. Stelle sicher, dass MSFS 2024 läuft (nicht nur der Launcher)
2. Starte die Bridge neu
3. Prüfe ob .NET 8 Runtime installiert ist

### Website zeigt "Nicht verbunden"

1. Die Bridge muss laufen
2. Prüfe ob Port 8080 in der Firewall freigegeben ist
3. Bei HTTPS-Websites: Die Bridge läuft auf localhost, was von Browsern als sicher behandelt wird

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
- [Discord](https://discord.gg/Kn5eXb9D) - Community & Support
