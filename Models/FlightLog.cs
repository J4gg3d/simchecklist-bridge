using Newtonsoft.Json;

namespace MSFSBridge.Models;

/// <summary>
/// Repräsentiert einen kompletten Flug für das Logbuch
/// </summary>
public class FlightLog
{
    // Durchschnittliche Reisegeschwindigkeit für Score-Berechnung (kts)
    private const double AVERAGE_CRUISE_SPEED = 400.0;

    [JsonProperty("user_id")]
    public string? UserId { get; set; }

    [JsonProperty("origin")]
    public string? Origin { get; set; }

    [JsonProperty("destination")]
    public string? Destination { get; set; }

    [JsonProperty("aircraft_type")]
    public string? AircraftType { get; set; }

    [JsonProperty("departure_time")]
    public DateTime? DepartureTime { get; set; }

    [JsonProperty("arrival_time")]
    public DateTime ArrivalTime { get; set; } = DateTime.UtcNow;

    [JsonProperty("flight_duration_seconds")]
    public int FlightDurationSeconds { get; set; }

    [JsonProperty("distance_nm")]
    public double DistanceNm { get; set; }

    [JsonProperty("max_altitude_ft")]
    public int MaxAltitudeFt { get; set; }

    [JsonProperty("landing_rating")]
    public int LandingRating { get; set; }

    [JsonProperty("landing_vs")]
    public double LandingVs { get; set; }

    [JsonProperty("landing_gforce")]
    public double LandingGforce { get; set; }

    [JsonProperty("session_code")]
    public string? SessionCode { get; set; }

    [JsonProperty("score")]
    public int Score { get; set; }

    /// <summary>
    /// Berechnet den Flight Score basierend auf Distanz, Dauer und Landing Rating.
    /// - Distanz dominiert, aber in Relation zur erwarteten Flugzeit
    /// - SimRate/Überspringen wird bestraft (zu schnelle Flüge = weniger Punkte)
    /// - Landing Rating als Prestige-Bonus (10-50 Punkte)
    /// </summary>
    public void CalculateScore()
    {
        // Erwartete Flugzeit in Sekunden: Distanz / Durchschnittsgeschwindigkeit * 3600
        double expectedDurationSeconds = (DistanceNm / AVERAGE_CRUISE_SPEED) * 3600.0;

        // Zeit-Faktor: Verhältnis von tatsächlicher zu erwarteter Zeit (max 1.0)
        // Wer schneller fliegt als realistisch möglich, bekommt weniger Punkte
        double timeFactor = 1.0;
        if (expectedDurationSeconds > 0)
        {
            timeFactor = Math.Min(1.0, FlightDurationSeconds / expectedDurationSeconds);
        }

        // Basis-Score: Distanz × Zeit-Faktor
        double baseScore = DistanceNm * timeFactor;

        // Landing Bonus: 10-50 Punkte (Rating 1-5 × 10)
        int landingBonus = LandingRating * 10;

        // Gesamt-Score (gerundet)
        Score = (int)Math.Round(baseScore + landingBonus);
    }
}
