using Microsoft.FlightSimulator.SimConnect;
using MSFSBridge.Models;
using System.Runtime.InteropServices;

namespace MSFSBridge;

/// <summary>
/// Manager für die SimConnect-Verbindung zu MSFS
/// </summary>
public class SimConnectManager : IDisposable
{
    private SimConnect? _simConnect;
    private bool _isConnected = false;
    private bool _disposed = false;
    private Thread? _messageThread;
    private bool _running = false;
    private bool _isPaused = false;  // Wird durch System-Event gesetzt

    // Landing Detection
    private bool _wasOnGround = true;
    private double _lastVerticalSpeed = 0;
    private double _lastGForce = 1.0;
    private double _lastGroundSpeed = 0;
    private DateTime _lastLandingTime = DateTime.MinValue;

    // Landing Attitude Data (erfasst kurz vor Touchdown)
    private double _lastPitch = 0;
    private double _lastBank = 0;
    private double _lastAoA = 0;
    private double _lastSideslip = 0;
    private double _lastHeadingMagnetic = 0;
    private double _lastAccelX = 0;
    private double _lastAccelZ = 0;
    private SimData? _lastSimData;  // Letzte SimData für FlightTracker

    // Approach Tracking für Gleitpfad-Visualisierung (letzte 60 Sekunden)
    private const int APPROACH_BUFFER_SIZE = 60;
    private readonly Queue<ApproachRawData> _approachBuffer = new();
    private double _touchdownLat = 0;
    private double _touchdownLon = 0;

    // Hilfsklasse für Rohdaten im Buffer
    private class ApproachRawData
    {
        public DateTime Timestamp { get; set; }
        public double AltitudeAgl { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double VerticalSpeed { get; set; }
        public double GroundSpeed { get; set; }
    }

    // Flight Tracker (sammelt Flugdaten für Logbuch)
    private readonly FlightTracker _flightTracker = new();

    // Route vom Frontend (Fallback wenn GPS-Daten leer sind)
    private string? _frontendOrigin;
    private string? _frontendDestination;

    // Flight Validation (verhindert falsche Landungen beim Mission-Start)
    private DateTime _takeoffTime = DateTime.MinValue;  // Wann abgehoben
    private double _maxAltitudeAGL = 0;  // Maximale erreichte Höhe
    private double _maxGForce = 0;  // Maximale G-Force während des Flugs
    private double _takeoffGroundSpeed = 0;  // Ground Speed beim Abheben
    private bool _validTakeoff = false;  // War es ein echter Takeoff?
    private bool _firstDataReceived = false;  // Verhindert falschen Takeoff bei Sim-Start
    private const double MIN_FLIGHT_SECONDS = 180;  // Mindestens 3 Minuten in der Luft
    private const double MIN_ALTITUDE_AGL = 100;  // Mindestens 100 ft AGL erreicht
    private const double MIN_TAKEOFF_SPEED = 40;  // Mindestens 40 kts beim Abheben
    private const double MAX_TAKEOFF_SPEED = 250;  // Maximal 250 kts beim Abheben (darüber = in-air spawn)
    private const double MIN_GFORCE = 0.5;  // Mindestens 0.5 G während des Flugs (Plausibilität)
    private const double MIN_DISTANCE_NM = 5;  // Mindestens 5 NM geflogen

    // Windows Message für SimConnect
    private const int WM_USER_SIMCONNECT = 0x0402;

    // Event IDs
    private enum EVENTS
    {
        PAUSE_STATE,
        PAUSE_EX1
    }

    // Data Definition IDs
    private enum DEFINITIONS
    {
        SimData
    }

    // Request IDs
    private enum REQUESTS
    {
        SimData
    }

    // Struct für SimConnect-Daten (muss exakt zu AddToDataDefinition passen)
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct SimDataStruct
    {
        public double SimulationRate;
        public double SimOnGround;
        public double Altitude;
        public double GroundSpeed;
        public double Heading;
        public double Latitude;
        public double Longitude;
        public double VerticalSpeed;  // ft/min
        public double GForce;
        public double AltitudeAgl;  // Höhe über Grund für Approach-Tracking

        // Attitude für Landing-Analyse
        public double PlanePitchDegrees;     // Positiv = Nase oben
        public double PlaneBankDegrees;      // Positiv = rechter Flügel unten
        public double AngleOfAttack;         // Anstellwinkel
        public double SideSlip;              // Schiebewinkel
        public double PlaneHeadingMagnetic;  // Magnetischer Kurs
        public double AccelerationBodyX;     // Seitliche G-Kraft
        public double AccelerationBodyZ;     // Längs-G-Kraft

        public double EngCombustion1;
        public double FlapsPosition;
        public double GearPosition;

        // Parkbremse
        public double ParkingBrake;

        // Lichter
        public double LightNav;
        public double LightBeacon;
        public double LightLanding;
        public double LightTaxi;
        public double LightStrobe;
        public double LightRecognition;
        public double LightWing;
        public double LightLogo;
        public double LightPanel;

        // Elektrisch
        public double Battery1;
        public double Battery2;
        public double ExternalPower;
        public double AvionicsMaster;

        // APU
        public double ApuSwitch;
        public double ApuPctRpm;

        // Triebwerke
        public double EngCombustion2;
        public double Engine1N1;
        public double Engine1N2;
        public double Engine2N1;
        public double Engine2N2;
        public double Throttle1;
        public double Throttle2;

        // Flugsteuerung
        public double SpoilersArmed;
        public double SpoilersPosition;
        public double AutopilotMaster;
        public double AutothrottleArmed;

        // Kabine
        public double SeatbeltSign;
        public double NoSmokingSign;

        // Transponder
        public double TransponderState;

        // Anti-Ice
        public double AntiIceEng1;
        public double AntiIceEng2;
        public double AntiIceStructural;
        public double PitotHeat;

        // Treibstoffpumpen
        public double FuelPump1;
        public double FuelPump2;

        // Hydraulik - deaktiviert, existiert nicht mit Index in MSFS 2024
        // public double HydraulicPump1;
        // public double HydraulicPump2;

        // GPS Flugplan (numerische Werte)
        public double GpsIsActiveFlightPlan;
        public double GpsFlightPlanWpCount;
        public double GpsFlightPlanWpIndex;
        public double GpsWpDistance;  // in meters
        public double GpsWpEte;       // in seconds
        public double GpsEte;         // in seconds

        // Strings am Ende (müssen am Ende stehen wegen Marshalling)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string AircraftTitle;

        // ATC-SimVars für Flugnummer
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string AtcId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string AtcAirline;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string AtcFlightNumber;

        // GPS Waypoint Strings
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GpsWpNextId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string GpsWpPrevId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)]
        public string GpsApproachAirportId;
    }

    // Events
    public event Action<SimData>? OnDataReceived;
    public event Action<LandingInfo>? OnLandingDetected;
    public event Action<FlightLog>? OnFlightCompleted;  // Neues Event für Logbuch
    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    /// <summary>
    /// Setzt die Session-ID für Flight-Logging (anonyme Flüge)
    /// </summary>
    public void SetSessionCode(string? sessionCode)
    {
        _flightTracker.SetSessionCode(sessionCode);
    }

    /// <summary>
    /// Setzt die User-ID für Flight-Logging (eingeloggte User)
    /// </summary>
    public void SetUserId(string? userId)
    {
        _flightTracker.SetUserId(userId);
    }

    /// <summary>
    /// Setzt die Route vom Frontend (Fallback wenn GPS-Daten leer sind)
    /// </summary>
    public void SetRoute(string? origin, string? destination)
    {
        _frontendOrigin = origin?.ToUpperInvariant();
        _frontendDestination = destination?.ToUpperInvariant();
        Console.WriteLine($"[FLIGHT] Route gesetzt: {_frontendOrigin ?? "?"} → {_frontendDestination ?? "?"}");

        // Route auch an FlightTracker weitergeben (falls bereits tracking)
        if (_flightTracker.IsTracking)
        {
            _flightTracker.SetRoute(_frontendOrigin, _frontendDestination);
        }
    }

    public bool IsConnected => _isConnected;

    /// <summary>
    /// Prüft ob ein String gültige Zeichen enthält (keine Sonderzeichen/Müll)
    /// </summary>
    private static bool IsValidString(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        // Prüfen ob der String nur druckbare ASCII-Zeichen enthält
        foreach (char c in str)
        {
            if (c < 32 || c > 126) return false;
        }
        return true;
    }

    /// <summary>
    /// Prüft ob ein String ein gültiger ICAO-Code ist (4 Buchstaben)
    /// </summary>
    private static bool IsValidIcao(string? str)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        str = str.Trim();
        if (str.Length < 3 || str.Length > 4) return false;

        // Echte ICAO-Codes haben 4 Buchstaben (EDDF, LFSB, KJFK)
        // Codes mit Ziffern (DE02, ED07) sind lokale Kennzeichen, keine echten ICAOs
        foreach (char c in str)
        {
            if (!char.IsLetter(c)) return false;
        }
        return true;
    }

    /// <summary>
    /// Verbindung zum Simulator herstellen
    /// </summary>
    public bool Connect()
    {
        if (_isConnected)
        {
            OnStatusChanged?.Invoke("Bereits verbunden");
            return true;
        }

        try
        {
            OnStatusChanged?.Invoke("Verbinde mit MSFS...");

            // SimConnect-Verbindung erstellen
            _simConnect = new SimConnect("MSFS Checklist Bridge", IntPtr.Zero, WM_USER_SIMCONNECT, null, 0);

            // Event Handler registrieren
            _simConnect.OnRecvOpen += SimConnect_OnRecvOpen;
            _simConnect.OnRecvQuit += SimConnect_OnRecvQuit;
            _simConnect.OnRecvException += SimConnect_OnRecvException;
            _simConnect.OnRecvSimobjectData += SimConnect_OnRecvSimobjectData;
            _simConnect.OnRecvEvent += SimConnect_OnRecvEvent;

            // System-Events für Pause abonnieren
            _simConnect.SubscribeToSystemEvent(EVENTS.PAUSE_STATE, "Pause");
            _simConnect.SubscribeToSystemEvent(EVENTS.PAUSE_EX1, "Pause_EX1");

            // MINIMALE Daten-Definition für MSFS 2024 Kompatibilität
            // Viele SimVars aus MSFS 2020 sind in MSFS 2024 obsolete!
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "SIMULATION RATE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "G FORCE", "GForce", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE ALT ABOVE GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Attitude für Landing-Analyse (Reihenfolge muss zu SimDataStruct passen!)
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "INCIDENCE ALPHA", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "INCIDENCE BETA", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PLANE HEADING DEGREES MAGNETIC", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ACCELERATION BODY X", "G Force", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ACCELERATION BODY Z", "G Force", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG COMBUSTION:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "FLAPS HANDLE PERCENT", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GEAR TOTAL PCT EXTENDED", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);  // Ersetzt GEAR HANDLE POSITION

            // Parkbremse
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "BRAKE PARKING INDICATOR", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Lichter
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT NAV ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT BEACON ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT LANDING ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT TAXI ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT STROBE ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT RECOGNITION ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT WING ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT LOGO ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "LIGHT PANEL ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Elektrisch - vereinfacht für MSFS 2024
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ELECTRICAL MASTER BATTERY:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ELECTRICAL MASTER BATTERY:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "EXTERNAL POWER ON:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "CIRCUIT AVIONICS ON", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);  // Ersetzt AVIONICS MASTER SWITCH

            // APU
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "APU SWITCH", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "APU PCT RPM", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Triebwerke
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG COMBUSTION:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG N1 RPM:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG N2 RPM:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG N1 RPM:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG N2 RPM:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG THROTTLE LEVER POSITION:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG THROTTLE LEVER POSITION:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Flugsteuerung
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "SPOILERS ARMED", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "SPOILERS HANDLE POSITION", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "AUTOPILOT MASTER", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "AUTOPILOT THROTTLE ARM", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Kabine - deaktiviert, möglicherweise nicht in MSFS 2024
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "CABIN SEATBELTS ALERT SWITCH", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "CABIN NO SMOKING ALERT SWITCH", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Transponder
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "TRANSPONDER STATE:1", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Anti-Ice
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG ANTI ICE:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ENG ANTI ICE:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "STRUCTURAL DEICE SWITCH", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "PITOT HEAT", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Treibstoffpumpen
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG FUEL PUMP SWITCH:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GENERAL ENG FUEL PUMP SWITCH:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Hydraulik - deaktiviert, HYDRAULIC SWITCH existiert nicht mit Index in MSFS 2024
            // _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "HYDRAULIC SWITCH:1", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            // _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "HYDRAULIC SWITCH:2", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // GPS Flugplan (numerische Werte)
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS IS ACTIVE FLIGHT PLAN", "bool", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS FLIGHT PLAN WP COUNT", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS FLIGHT PLAN WP INDEX", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS WP DISTANCE", "meters", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS WP ETE", "seconds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS ETE", "seconds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Strings am Ende
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // ATC-SimVars für Flugnummer
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ATC ID", null, SIMCONNECT_DATATYPE.STRING64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ATC AIRLINE", null, SIMCONNECT_DATATYPE.STRING64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "ATC FLIGHT NUMBER", null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // GPS Waypoint Strings
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS WP NEXT ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS WP PREV ID", null, SIMCONNECT_DATATYPE.STRING32, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DEFINITIONS.SimData, "GPS APPROACH AIRPORT ID", null, SIMCONNECT_DATATYPE.STRING8, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            // Struct registrieren
            _simConnect.RegisterDataDefineStruct<SimDataStruct>(DEFINITIONS.SimData);

            // Message-Thread starten
            _running = true;
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "SimConnect Message Loop"
            };
            _messageThread.Start();

            // Periodische Datenabfrage starten (SECOND statt SIM_FRAME, ohne CHANGED Flag für kontinuierliche Updates)
            _simConnect.RequestDataOnSimObject(REQUESTS.SimData, DEFINITIONS.SimData, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

            _isConnected = true;
            OnStatusChanged?.Invoke("Verbunden mit MSFS");
            return true;
        }
        catch (COMException ex)
        {
            OnError?.Invoke($"SimConnect-Fehler: {ex.Message}");
            OnError?.Invoke("Ist MSFS gestartet?");
            Disconnect();
            return false;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Verbindungsfehler: {ex.Message}");
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Verbindung trennen
    /// </summary>
    public void Disconnect()
    {
        _running = false;
        _isConnected = false;
        _firstDataReceived = false;  // Reset für nächsten Connect
        _validTakeoff = false;
        _wasOnGround = true;

        if (_simConnect != null)
        {
            try
            {
                _simConnect.Dispose();
            }
            catch { }
            _simConnect = null;
        }

        _messageThread?.Join(1000);
        _messageThread = null;

        OnStatusChanged?.Invoke("Verbindung getrennt");
    }

    /// <summary>
    /// Message-Loop für SimConnect
    /// </summary>
    private void MessageLoop()
    {
        while (_running && _simConnect != null)
        {
            try
            {
                _simConnect.ReceiveMessage();
            }
            catch (Exception)
            {
                // Verbindung verloren
                if (_running)
                {
                    _isConnected = false;
                    OnError?.Invoke("Verbindung zu MSFS verloren");
                    _running = false;
                }
            }

            Thread.Sleep(10);
        }
    }

    #region SimConnect Event Handlers

    private void SimConnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
    {
        OnStatusChanged?.Invoke($"Verbunden: {data.szApplicationName}");
    }

    private void SimConnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
    {
        OnStatusChanged?.Invoke("MSFS wurde beendet");
        _isConnected = false;
        _running = false;
    }

    private void SimConnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        OnError?.Invoke($"SimConnect Exception: {data.dwException}");
    }

    private void SimConnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
    {
        switch ((EVENTS)data.uEventID)
        {
            case EVENTS.PAUSE_STATE:
            case EVENTS.PAUSE_EX1:
                bool wasPaused = _isPaused;
                _isPaused = data.dwData != 0;
                if (wasPaused != _isPaused)
                {
                    Console.WriteLine($"[SIM] {(_isPaused ? "Pausiert" : "Fortgesetzt")}");
                }
                break;
        }
    }

    private void SimConnect_OnRecvSimobjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
    {
        if (data.dwRequestID == (uint)REQUESTS.SimData)
        {
            try
            {
                var simData = (SimDataStruct)data.dwData[0];

                bool currentOnGround = simData.SimOnGround > 0;
                double currentVerticalSpeed = Math.Round(simData.VerticalSpeed, 0);
                double currentGForce = Math.Round(simData.GForce, 2);
                double currentGroundSpeed = Math.Round(simData.GroundSpeed, 0);
                double currentAltitude = simData.Altitude;

                // Erstes Datenpaket: Nur Status setzen, keine Transition auslösen
                if (!_firstDataReceived)
                {
                    _firstDataReceived = true;
                    _wasOnGround = currentOnGround;
                    if (!currentOnGround)
                    {
                        Console.WriteLine($"[FLIGHT] Started in-air (GS: {currentGroundSpeed:F0} kts, Alt: {currentAltitude:F0} ft) - ignoring until next ground contact");
                    }
                    else
                    {
                        Console.WriteLine($"[FLIGHT] Started on ground - ready for takeoff detection");
                    }
                }

                // Takeoff Detection: War am Boden, jetzt in der Luft
                if (_wasOnGround && !currentOnGround && _firstDataReceived)
                {
                    _takeoffTime = DateTime.Now;
                    _maxAltitudeAGL = 0;
                    _maxGForce = currentGForce;
                    _takeoffGroundSpeed = currentGroundSpeed;

                    // Prüfe ob es ein plausibler Takeoff ist (nicht Mission-Start oder in-air spawn)
                    bool speedValid = currentGroundSpeed >= MIN_TAKEOFF_SPEED && currentGroundSpeed <= MAX_TAKEOFF_SPEED;
                    _validTakeoff = speedValid;

                    if (_validTakeoff)
                    {
                        Console.WriteLine($"[FLIGHT] Takeoff detected - GS: {currentGroundSpeed:F0} kts");
                        // FlightTracker starten wenn wir lastSimData haben
                        if (_lastSimData != null)
                        {
                            // Origin: GPS-Daten > Frontend-Route > Nearest Airport
                            string? originAirport = null;
                            if (IsValidIcao(simData.GpsWpPrevId))
                            {
                                originAirport = simData.GpsWpPrevId?.Trim();
                            }
                            else if (!string.IsNullOrEmpty(_frontendOrigin))
                            {
                                originAirport = _frontendOrigin;
                            }
                            else
                            {
                                originAirport = AirportDatabase.FindNearestAirport(simData.Latitude, simData.Longitude);
                            }
                            _flightTracker.StartTracking(_lastSimData, originAirport);
                            Console.WriteLine($"[FLIGHT-LOG] Tracking started from {originAirport ?? "unknown"}");
                        }
                        else
                        {
                            Console.WriteLine("[FLIGHT-LOG] Cannot start tracking - no previous sim data");
                        }
                    }
                    else if (currentGroundSpeed > MAX_TAKEOFF_SPEED)
                    {
                        Console.WriteLine($"[FLIGHT] In-air spawn detected - GS: {currentGroundSpeed:F0} kts (> {MAX_TAKEOFF_SPEED} kts) - ignoring");
                    }
                    else
                    {
                        Console.WriteLine($"[FLIGHT] Possible mission start detected - GS: {currentGroundSpeed:F0} kts (< {MIN_TAKEOFF_SPEED} kts) - monitoring...");
                    }
                }

                // Track max values while airborne
                if (!currentOnGround)
                {
                    if (currentAltitude > _maxAltitudeAGL)
                    {
                        _maxAltitudeAGL = currentAltitude;
                    }
                    if (currentGForce > _maxGForce)
                    {
                        _maxGForce = currentGForce;
                    }
                    // Ein echter Flug entwickelt irgendwann ausreichend Geschwindigkeit
                    if (!_validTakeoff && currentGroundSpeed >= MIN_TAKEOFF_SPEED)
                    {
                        _validTakeoff = true;
                        Console.WriteLine($"[FLIGHT] Valid flight confirmed - GS: {currentGroundSpeed:F0} kts");
                        // Jetzt FlightTracker starten (nachträglich validierter Takeoff)
                        if (!_flightTracker.IsTracking && _lastSimData != null)
                        {
                            // Origin: GPS-Daten > Frontend-Route > Nearest Airport
                            string? originAirport = null;
                            if (IsValidIcao(simData.GpsWpPrevId))
                            {
                                originAirport = simData.GpsWpPrevId?.Trim();
                            }
                            else if (!string.IsNullOrEmpty(_frontendOrigin))
                            {
                                originAirport = _frontendOrigin;
                            }
                            else
                            {
                                originAirport = AirportDatabase.FindNearestAirport(simData.Latitude, simData.Longitude);
                            }
                            _flightTracker.StartTracking(_lastSimData, originAirport);
                        }
                    }

                    // FlightTracker mit aktuellen Daten updaten
                    if (_flightTracker.IsTracking && _lastSimData != null)
                    {
                        _flightTracker.Update(_lastSimData);
                    }

                    // Approach-Tracking: Sammle Daten unter 3000 ft AGL für Gleitpfad-Visualisierung
                    double currentAltitudeAgl = simData.AltitudeAgl;
                    if (currentAltitudeAgl < 3000 && currentAltitudeAgl > 0)
                    {
                        _approachBuffer.Enqueue(new ApproachRawData
                        {
                            Timestamp = DateTime.Now,
                            AltitudeAgl = currentAltitudeAgl,
                            Latitude = simData.Latitude,
                            Longitude = simData.Longitude,
                            VerticalSpeed = currentVerticalSpeed,
                            GroundSpeed = currentGroundSpeed
                        });

                        // Buffer auf 60 Einträge begrenzen (FIFO)
                        while (_approachBuffer.Count > APPROACH_BUFFER_SIZE)
                        {
                            _approachBuffer.Dequeue();
                        }
                    }
                }

                // Landing Detection: War in der Luft, jetzt am Boden
                if (!_wasOnGround && currentOnGround)
                {
                    // Berechne Flugzeit
                    double flightSeconds = (DateTime.Now - _takeoffTime).TotalSeconds;
                    double flightDistance = _flightTracker.TotalDistanceNm;

                    // Validiere: War es ein echter Flug?
                    bool validFlight = _validTakeoff &&
                                       flightSeconds >= MIN_FLIGHT_SECONDS &&
                                       _maxAltitudeAGL >= MIN_ALTITUDE_AGL &&
                                       _maxGForce >= MIN_GFORCE &&
                                       flightDistance >= MIN_DISTANCE_NM;

                    // Verhindere mehrfache Landungen in kurzer Zeit (z.B. Bouncing)
                    bool notBouncing = (DateTime.Now - _lastLandingTime).TotalSeconds > 5;

                    if (validFlight && notBouncing)
                    {
                        var (rating, score) = LandingInfo.CalculateRating(_lastVerticalSpeed);

                        // Destination: GPS-Daten > Frontend-Route > Nearest Airport
                        string? landingAirport = null;
                        if (IsValidIcao(simData.GpsApproachAirportId))
                        {
                            landingAirport = simData.GpsApproachAirportId?.Trim().ToUpper();
                        }
                        else if (!string.IsNullOrEmpty(_frontendDestination))
                        {
                            landingAirport = _frontendDestination;
                        }
                        else
                        {
                            landingAirport = AirportDatabase.FindNearestAirport(simData.Latitude, simData.Longitude);
                        }

                        var landingInfo = new LandingInfo
                        {
                            Timestamp = DateTime.Now,
                            VerticalSpeed = _lastVerticalSpeed,
                            GForce = _lastGForce,
                            GroundSpeed = _lastGroundSpeed,
                            Rating = rating,
                            RatingScore = score,
                            AircraftTitle = simData.AircraftTitle,
                            Airport = landingAirport,

                            // Attitude bei Touchdown
                            Pitch = Math.Round(_lastPitch, 1),
                            Bank = Math.Round(_lastBank, 1),
                            AngleOfAttack = Math.Round(_lastAoA, 1),
                            Sideslip = Math.Round(_lastSideslip, 1),
                            HeadingMagnetic = Math.Round(_lastHeadingMagnetic, 0),

                            // G-Kräfte für Vektoren
                            LateralG = Math.Round(_lastAccelX, 2),
                            LongitudinalG = Math.Round(_lastAccelZ, 2),

                            // Flight Summary (für Rank-Progress)
                            Origin = _flightTracker.OriginAirport,
                            Destination = landingAirport,
                            FlightDurationSeconds = _flightTracker.FlightDurationSeconds,
                            DistanceNm = Math.Round(_flightTracker.TotalDistanceNm, 1)
                        };

                        // Approach-Daten verarbeiten (Gleitpfad-Visualisierung)
                        if (_approachBuffer.Count > 0)
                        {
                            _touchdownLat = simData.Latitude;
                            _touchdownLon = simData.Longitude;
                            var touchdownTime = DateTime.Now;

                            landingInfo.ApproachData = _approachBuffer
                                .Select(raw => new ApproachDataPoint
                                {
                                    SecondsBeforeTouchdown = Math.Round((raw.Timestamp - touchdownTime).TotalSeconds, 1),
                                    AltitudeAgl = Math.Round(raw.AltitudeAgl, 0),
                                    DistanceToTouchdown = Math.Round(CalculateDistanceNm(raw.Latitude, raw.Longitude, _touchdownLat, _touchdownLon), 2),
                                    VerticalSpeed = Math.Round(raw.VerticalSpeed, 0),
                                    GroundSpeed = Math.Round(raw.GroundSpeed, 0)
                                })
                                .OrderBy(p => p.SecondsBeforeTouchdown)  // Älteste zuerst
                                .ToList();

                            Console.WriteLine($"[LANDING] Approach data: {landingInfo.ApproachData.Count} points");
                            _approachBuffer.Clear();
                        }

                        Console.WriteLine($"[LANDING] {rating} ({score}/5) - VS: {_lastVerticalSpeed:F0} ft/min, G: {_lastGForce:F2}, Pitch: {_lastPitch:F1}°, Bank: {_lastBank:F1}°");
                        OnLandingDetected?.Invoke(landingInfo);
                        _lastLandingTime = DateTime.Now;

                        // FlightTracker beenden und Flug speichern
                        if (_flightTracker.IsTracking)
                        {
                            // landingAirport wurde oben bereits mit Fallback-Logik bestimmt
                            var flightLog = _flightTracker.StopTracking(landingInfo, landingAirport);
                            if (flightLog != null)
                            {
                                Console.WriteLine($"[FLIGHT-LOG] Flight completed: {flightLog.Origin ?? "?"} -> {flightLog.Destination ?? "?"}, {flightLog.DistanceNm:F1} NM");
                                OnFlightCompleted?.Invoke(flightLog);
                            }
                            else
                            {
                                Console.WriteLine("[FLIGHT-LOG] FlightTracker returned null");
                            }
                        }
                        else
                        {
                            Console.WriteLine("[FLIGHT-LOG] FlightTracker was not tracking - flight not logged");
                        }
                    }
                    else if (!validFlight)
                    {
                        // Detaillierte Fehlermeldung
                        var reasons = new List<string>();
                        if (!_validTakeoff) reasons.Add($"no valid takeoff (GS at liftoff: {_takeoffGroundSpeed:F0} kts)");
                        if (flightSeconds < MIN_FLIGHT_SECONDS) reasons.Add($"too short ({flightSeconds:F0}s < {MIN_FLIGHT_SECONDS}s)");
                        if (_maxAltitudeAGL < MIN_ALTITUDE_AGL) reasons.Add($"too low ({_maxAltitudeAGL:F0}ft < {MIN_ALTITUDE_AGL}ft)");
                        if (_maxGForce < MIN_GFORCE) reasons.Add($"G-force unrealistic ({_maxGForce:F2} < {MIN_GFORCE})");
                        if (flightDistance < MIN_DISTANCE_NM) reasons.Add($"too short distance ({flightDistance:F1}NM < {MIN_DISTANCE_NM}NM)");

                        Console.WriteLine($"[FLIGHT] Ignored - {string.Join(", ", reasons)}");

                        // FlightTracker abbrechen
                        if (_flightTracker.IsTracking)
                        {
                            _flightTracker.CancelTracking();
                        }
                    }

                    // Reset flight tracking
                    _maxAltitudeAGL = 0;
                    _maxGForce = 0;
                    _validTakeoff = false;
                }

                // State für nächsten Durchlauf speichern
                _wasOnGround = currentOnGround;
                _lastVerticalSpeed = currentVerticalSpeed;
                _lastGForce = currentGForce;
                _lastGroundSpeed = currentGroundSpeed;

                // Attitude-Daten für Landing-Analyse speichern
                _lastPitch = simData.PlanePitchDegrees;
                _lastBank = simData.PlaneBankDegrees;
                _lastAoA = simData.AngleOfAttack;
                _lastSideslip = simData.SideSlip;
                _lastHeadingMagnetic = simData.PlaneHeadingMagnetic;
                _lastAccelX = simData.AccelerationBodyX;
                _lastAccelZ = simData.AccelerationBodyZ;

                var result = new SimData
                {
                    Connected = true,
                    SimRate = Math.Round(simData.SimulationRate, 1),
                    Paused = _isPaused,
                    OnGround = currentOnGround,
                    Altitude = Math.Round(simData.Altitude, 0),
                    GroundSpeed = currentGroundSpeed,
                    Heading = Math.Round(simData.Heading, 0),
                    Latitude = simData.Latitude,
                    Longitude = simData.Longitude,
                    VerticalSpeed = currentVerticalSpeed,
                    GForce = currentGForce,
                    EnginesRunning = simData.EngCombustion1 > 0 || simData.EngCombustion2 > 0,
                    FlapsPosition = (int)Math.Round(simData.FlapsPosition),
                    GearDown = simData.GearPosition > 0,
                    AircraftTitle = simData.AircraftTitle,

                    // Parkbremse
                    ParkingBrake = simData.ParkingBrake > 0,

                    // Lichter
                    LightNav = simData.LightNav > 0,
                    LightBeacon = simData.LightBeacon > 0,
                    LightLanding = simData.LightLanding > 0,
                    LightTaxi = simData.LightTaxi > 0,
                    LightStrobe = simData.LightStrobe > 0,
                    LightRecognition = simData.LightRecognition > 0,
                    LightWing = simData.LightWing > 0,
                    LightLogo = simData.LightLogo > 0,
                    LightPanel = simData.LightPanel > 0,

                    // Elektrisch
                    Battery1 = simData.Battery1 > 0,
                    Battery2 = simData.Battery2 > 0,
                    ExternalPower = simData.ExternalPower > 0,
                    AvionicsMaster = simData.AvionicsMaster > 0,

                    // APU
                    ApuMaster = simData.ApuSwitch > 0,
                    ApuRunning = simData.ApuPctRpm > 90,
                    ApuPctRpm = Math.Round(simData.ApuPctRpm, 0),

                    // Triebwerke
                    EngineMaster1 = simData.EngCombustion1 > 0,
                    EngineMaster2 = simData.EngCombustion2 > 0,
                    Engine1N1 = Math.Round(simData.Engine1N1, 1),
                    Engine1N2 = Math.Round(simData.Engine1N2, 1),
                    Engine2N1 = Math.Round(simData.Engine2N1, 1),
                    Engine2N2 = Math.Round(simData.Engine2N2, 1),
                    Throttle1 = Math.Round(simData.Throttle1, 0),
                    Throttle2 = Math.Round(simData.Throttle2, 0),

                    // Flugsteuerung
                    SpoilersArmed = simData.SpoilersArmed > 0,
                    SpoilersPosition = Math.Round(simData.SpoilersPosition, 0),
                    AutopilotMaster = simData.AutopilotMaster > 0,
                    AutothrottleArmed = simData.AutothrottleArmed > 0,

                    // Kabine
                    SeatbeltSign = simData.SeatbeltSign > 0,
                    NoSmokingSign = simData.NoSmokingSign > 0,

                    // Transponder (0=Off, 1=Standby, 2=Test, 3=On, 4=Alt)
                    TransponderState = (int)simData.TransponderState,

                    // Anti-Ice
                    AntiIceEng1 = simData.AntiIceEng1 > 0,
                    AntiIceEng2 = simData.AntiIceEng2 > 0,
                    AntiIceStructural = simData.AntiIceStructural > 0,
                    PitotHeat = simData.PitotHeat > 0,

                    // Treibstoffpumpen
                    FuelPump1 = simData.FuelPump1 > 0,
                    FuelPump2 = simData.FuelPump2 > 0,

                    // Hydraulik - deaktiviert
                    // HydraulicPump1 = simData.HydraulicPump1 > 0,
                    // HydraulicPump2 = simData.HydraulicPump2 > 0,

                    // ATC-Daten für Flugnummer
                    AtcId = IsValidString(simData.AtcId) ? simData.AtcId?.Trim() : null,
                    AtcAirline = IsValidString(simData.AtcAirline) ? simData.AtcAirline?.Trim() : null,
                    AtcFlightNumber = IsValidString(simData.AtcFlightNumber) ? simData.AtcFlightNumber?.Trim() : null,

                    // GPS Flugplan
                    GpsIsActiveFlightPlan = simData.GpsIsActiveFlightPlan > 0,
                    GpsFlightPlanWpCount = (int)simData.GpsFlightPlanWpCount,
                    GpsFlightPlanWpIndex = (int)simData.GpsFlightPlanWpIndex,
                    GpsWpDistance = Math.Round(simData.GpsWpDistance / 1852.0, 1),  // Meter zu NM
                    GpsWpEte = Math.Round(simData.GpsWpEte, 0),
                    GpsEte = Math.Round(simData.GpsEte, 0),

                    // GPS Waypoints
                    GpsWpNextId = IsValidString(simData.GpsWpNextId) ? simData.GpsWpNextId?.Trim() : null,
                    GpsWpPrevId = IsValidString(simData.GpsWpPrevId) ? simData.GpsWpPrevId?.Trim() : null,
                    GpsApproachAirportId = IsValidIcao(simData.GpsApproachAirportId) ? simData.GpsApproachAirportId?.Trim().ToUpper() : null
                };

                // SimData für FlightTracker speichern
                _lastSimData = result;

                OnDataReceived?.Invoke(result);
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Daten-Parsing-Fehler: {ex.Message}");
            }
        }
    }

    #endregion

    /// <summary>
    /// Berechnet die Distanz zwischen zwei Koordinaten in Nautischen Meilen (Haversine-Formel)
    /// </summary>
    private static double CalculateDistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusNm = 3440.065;  // Erdradius in Nautischen Meilen

        double lat1Rad = lat1 * Math.PI / 180.0;
        double lat2Rad = lat2 * Math.PI / 180.0;
        double deltaLat = (lat2 - lat1) * Math.PI / 180.0;
        double deltaLon = (lon2 - lon1) * Math.PI / 180.0;

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2) +
                   Math.Cos(lat1Rad) * Math.Cos(lat2Rad) *
                   Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusNm * c;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        GC.SuppressFinalize(this);
    }
}
