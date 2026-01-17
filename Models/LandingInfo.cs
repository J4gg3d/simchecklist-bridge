namespace MSFSBridge.Models;

/// <summary>
/// Datenmodell für Landing-Informationen
/// </summary>
public class LandingInfo
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // Touchdown-Daten
    public double VerticalSpeed { get; set; }  // ft/min (negativ = sinken)
    public double GForce { get; set; }
    public double GroundSpeed { get; set; }  // knots

    // Rating
    public string Rating { get; set; } = "Unknown";
    public int RatingScore { get; set; }  // 1-5 (5 = Perfect)

    // Flugzeug & Ort
    public string? AircraftTitle { get; set; }
    public string? Airport { get; set; }  // ICAO wenn verfügbar

    // Attitude bei Touchdown
    public double Pitch { get; set; }           // Grad, positiv = Nase oben
    public double Bank { get; set; }            // Grad, positiv = rechter Flügel unten
    public double AngleOfAttack { get; set; }   // Anstellwinkel in Grad
    public double Sideslip { get; set; }        // Schiebewinkel in Grad
    public double HeadingMagnetic { get; set; } // Magnetischer Kurs

    // G-Kräfte für Vektoren
    public double LateralG { get; set; }        // Seitliche G-Kraft
    public double LongitudinalG { get; set; }   // Längs-G-Kraft (Verzögerung)

    // Approach-Daten für Gleitpfad-Visualisierung (letzte 60 Sekunden vor Touchdown)
    public List<ApproachDataPoint>? ApproachData { get; set; }

    // Flight Summary (für Rank-Progress nach Landing)
    public string? Origin { get; set; }           // ICAO Startflughafen
    public string? Destination { get; set; }      // ICAO Zielflughafen
    public int FlightDurationSeconds { get; set; } // Flugdauer in Sekunden
    public double DistanceNm { get; set; }        // Geflogene Distanz in NM

    /// <summary>
    /// Berechnet das Rating basierend auf der Vertical Speed
    /// </summary>
    public static (string Rating, int Score) CalculateRating(double verticalSpeedFpm)
    {
        // Vertical Speed ist negativ beim Sinken, wir nehmen den Absolutwert
        double absVs = Math.Abs(verticalSpeedFpm);

        if (absVs < 100)
            return ("Perfect", 5);
        else if (absVs < 200)
            return ("Good", 4);
        else if (absVs < 300)
            return ("Acceptable", 3);
        else if (absVs < 500)
            return ("Hard", 2);
        else
            return ("Very Hard", 1);
    }
}
