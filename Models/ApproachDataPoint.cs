namespace MSFSBridge.Models;

/// <summary>
/// Ein Datenpunkt während des Anflugs für die Gleitpfad-Visualisierung
/// </summary>
public class ApproachDataPoint
{
    /// <summary>
    /// Sekunden vor dem Touchdown (0 = Touchdown, negative Werte = Zeit davor)
    /// </summary>
    public double SecondsBeforeTouchdown { get; set; }

    /// <summary>
    /// Höhe über Grund in Fuß (Radio Altimeter)
    /// </summary>
    public double AltitudeAgl { get; set; }

    /// <summary>
    /// Distanz zum Touchdown-Punkt in Nautischen Meilen
    /// </summary>
    public double DistanceToTouchdown { get; set; }

    /// <summary>
    /// Vertical Speed in ft/min
    /// </summary>
    public double VerticalSpeed { get; set; }

    /// <summary>
    /// Ground Speed in Knoten
    /// </summary>
    public double GroundSpeed { get; set; }
}
