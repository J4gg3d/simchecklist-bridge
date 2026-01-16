namespace MSFSBridge.Models;

/// <summary>
/// Datenmodell für SimConnect-Daten die an die Webseite gesendet werden
/// </summary>
public class SimData
{
    public double SimRate { get; set; } = 1.0;
    public bool Paused { get; set; } = false;
    public bool Connected { get; set; } = false;

    // Remote Session
    public string? SessionCode { get; set; }

    // Flugdaten
    public double? Altitude { get; set; }
    public double? GroundSpeed { get; set; }
    public double? Heading { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? VerticalSpeed { get; set; }  // ft/min
    public double? GForce { get; set; }
    public string? AircraftTitle { get; set; }

    // Flugphase
    public bool? OnGround { get; set; }
    public bool? EnginesRunning { get; set; }
    public int? FlapsPosition { get; set; }
    public bool? GearDown { get; set; }

    // Parkbremse
    public bool? ParkingBrake { get; set; }

    // Lichter
    public bool? LightNav { get; set; }
    public bool? LightBeacon { get; set; }
    public bool? LightLanding { get; set; }
    public bool? LightTaxi { get; set; }
    public bool? LightStrobe { get; set; }
    public bool? LightRecognition { get; set; }
    public bool? LightWing { get; set; }
    public bool? LightLogo { get; set; }
    public bool? LightPanel { get; set; }

    // Elektrisch
    public bool? Battery1 { get; set; }
    public bool? Battery2 { get; set; }
    public bool? ExternalPower { get; set; }
    public bool? AvionicsMaster { get; set; }

    // APU
    public bool? ApuMaster { get; set; }
    public bool? ApuRunning { get; set; }
    public double? ApuPctRpm { get; set; }

    // Triebwerke
    public bool? EngineMaster1 { get; set; }
    public bool? EngineMaster2 { get; set; }
    public double? Engine1N1 { get; set; }
    public double? Engine1N2 { get; set; }
    public double? Engine2N1 { get; set; }
    public double? Engine2N2 { get; set; }
    public double? Throttle1 { get; set; }
    public double? Throttle2 { get; set; }

    // Flugsteuerung
    public bool? SpoilersArmed { get; set; }
    public double? SpoilersPosition { get; set; }
    public bool? AutopilotMaster { get; set; }
    public bool? AutothrottleArmed { get; set; }

    // Kabine
    public bool? SeatbeltSign { get; set; }
    public bool? NoSmokingSign { get; set; }

    // Transponder
    public int? TransponderState { get; set; }

    // Anti-Ice
    public bool? AntiIceEng1 { get; set; }
    public bool? AntiIceEng2 { get; set; }
    public bool? AntiIceStructural { get; set; }
    public bool? PitotHeat { get; set; }

    // Treibstoffpumpen
    public bool? FuelPump1 { get; set; }
    public bool? FuelPump2 { get; set; }

    // Hydraulik
    public bool? HydraulicPump1 { get; set; }
    public bool? HydraulicPump2 { get; set; }

    // GPS Flugplan
    public bool? GpsIsActiveFlightPlan { get; set; }
    public int? GpsFlightPlanWpCount { get; set; }
    public int? GpsFlightPlanWpIndex { get; set; }
    public double? GpsFlightPlanTotalDistance { get; set; } // in NM
    public double? GpsWpDistance { get; set; } // in NM
    public double? GpsWpEte { get; set; } // in Sekunden
    public double? GpsEte { get; set; } // in Sekunden
    public double? GpsEta { get; set; } // in Sekunden

    // ATC / Flug-Identifikation
    public string? AtcId { get; set; }
    public string? AtcAirline { get; set; }
    public string? AtcFlightNumber { get; set; }

    // GPS Waypoints
    public string? GpsWpNextId { get; set; }
    public string? GpsWpPrevId { get; set; }
    public string? GpsApproachAirportId { get; set; } // Zielflughafen
}
