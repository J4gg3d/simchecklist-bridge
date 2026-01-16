namespace MSFSBridge;

/// <summary>
/// Statische Flughafen-Datenbank für Fallback bei fehlendem GPS-Approach
/// </summary>
public static class AirportDatabase
{
    private const double EarthRadiusNm = 3440.065; // Erdradius in Nautischen Meilen

    /// <summary>
    /// Statisches Dictionary mit ICAO-Codes und Koordinaten
    /// Synchronisiert mit src/utils/geoUtils.js
    /// </summary>
    private static readonly Dictionary<string, (double Lat, double Lon)> Airports = new()
    {
        // Deutschland
        ["EDDF"] = (50.0379, 8.5622),    // Frankfurt
        ["EDDM"] = (48.3538, 11.7861),   // München
        ["EDDB"] = (52.3667, 13.5033),   // Berlin Brandenburg
        ["EDDL"] = (51.2895, 6.7668),    // Düsseldorf
        ["EDDH"] = (53.6304, 9.9882),    // Hamburg
        ["EDDK"] = (50.8659, 7.1427),    // Köln/Bonn
        ["EDDS"] = (48.6899, 9.2220),    // Stuttgart
        ["EDDW"] = (53.0475, 8.7867),    // Bremen
        ["EDDN"] = (49.4987, 11.0669),   // Nürnberg
        ["EDDV"] = (52.4611, 9.6850),    // Hannover

        // Europa
        ["EGLL"] = (51.4700, -0.4543),   // London Heathrow
        ["EHAM"] = (52.3086, 4.7639),    // Amsterdam Schiphol
        ["LFPG"] = (49.0097, 2.5479),    // Paris CDG
        ["LEMD"] = (40.4719, -3.5626),   // Madrid
        ["LIRF"] = (41.8003, 12.2389),   // Rom Fiumicino
        ["LSZH"] = (47.4647, 8.5492),    // Zürich
        ["LOWW"] = (48.1103, 16.5697),   // Wien
        ["EBBR"] = (50.9014, 4.4844),    // Brüssel
        ["EKCH"] = (55.6180, 12.6560),   // Kopenhagen
        ["ENGM"] = (60.1939, 11.1004),   // Oslo
        ["ESSA"] = (59.6519, 17.9186),   // Stockholm Arlanda
        ["EFHK"] = (60.3172, 24.9633),   // Helsinki
        ["LPPT"] = (38.7813, -9.1359),   // Lissabon
        ["LEBL"] = (41.2971, 2.0785),    // Barcelona
        ["EIDW"] = (53.4213, -6.2701),   // Dublin
        ["EGKK"] = (51.1481, -0.1903),   // London Gatwick
        ["EGCC"] = (53.3537, -2.2750),   // Manchester
        ["LFPO"] = (48.7253, 2.3594),    // Paris Orly
        ["LIMC"] = (45.6306, 8.7231),    // Mailand Malpensa
        ["LGAV"] = (37.9364, 23.9445),   // Athen
        ["LTFM"] = (41.2608, 28.7419),   // Istanbul
        ["LFSB"] = (47.5896, 7.5299),    // Basel-Mulhouse EuroAirport
        ["LSZB"] = (46.9141, 7.4971),    // Bern
        ["LSGG"] = (46.2381, 6.1089),    // Genf
        ["LFST"] = (48.5383, 7.6281),    // Straßburg
        ["LFML"] = (43.4393, 5.2214),    // Marseille
        ["LFLL"] = (45.7256, 5.0811),    // Lyon
        ["LFMN"] = (43.6584, 7.2159),    // Nizza
        ["LFBD"] = (44.8283, -0.7156),   // Bordeaux
        ["LFRS"] = (47.1532, -1.6107),   // Nantes
        ["EDNY"] = (47.6713, 9.5115),    // Friedrichshafen
        ["EDSB"] = (48.7794, 8.0805),    // Karlsruhe/Baden-Baden

        // USA
        ["KJFK"] = (40.6413, -73.7781),  // New York JFK
        ["KLAX"] = (33.9416, -118.4085), // Los Angeles
        ["KORD"] = (41.9742, -87.9073),  // Chicago O'Hare
        ["KATL"] = (33.6407, -84.4277),  // Atlanta
        ["KDFW"] = (32.8998, -97.0403),  // Dallas/Fort Worth
        ["KDEN"] = (39.8561, -104.6737), // Denver
        ["KSFO"] = (37.6213, -122.3790), // San Francisco
        ["KLAS"] = (36.0840, -115.1537), // Las Vegas
        ["KMIA"] = (25.7959, -80.2870),  // Miami
        ["KSEA"] = (47.4502, -122.3088), // Seattle
        ["KBOS"] = (42.3656, -71.0096),  // Boston
        ["KEWR"] = (40.6895, -74.1745),  // Newark
        ["KPHX"] = (33.4373, -112.0078), // Phoenix
        ["KMSP"] = (44.8848, -93.2223),  // Minneapolis
        ["KDTW"] = (42.2162, -83.3554),  // Detroit
        ["KIAH"] = (29.9902, -95.3368),  // Houston
        ["KPIT"] = (40.4915, -80.2329),  // Pittsburgh
        ["KCLT"] = (35.2140, -80.9431),  // Charlotte
        ["KMCO"] = (28.4312, -81.3081),  // Orlando
        ["KFLL"] = (26.0726, -80.1527),  // Fort Lauderdale
        ["KSAN"] = (32.7336, -117.1897), // San Diego
        ["KPDX"] = (45.5898, -122.5951), // Portland
        ["KSLC"] = (40.7884, -111.9778), // Salt Lake City
        ["KDCA"] = (38.8521, -77.0377),  // Washington Reagan
        ["KIAD"] = (38.9531, -77.4565),  // Washington Dulles
        ["KBWI"] = (39.1754, -76.6683),  // Baltimore
        ["KTPA"] = (27.9755, -82.5332),  // Tampa
        ["KCLE"] = (41.4117, -81.8498),  // Cleveland
        ["KCMH"] = (39.9980, -82.8919),  // Columbus
        ["KIND"] = (39.7173, -86.2944),  // Indianapolis
        ["KMKE"] = (42.9472, -87.8966),  // Milwaukee
        ["KSTL"] = (38.7487, -90.3700),  // St. Louis
        ["KMCI"] = (39.2976, -94.7139),  // Kansas City
        ["KOMA"] = (41.3032, -95.8941),  // Omaha Eppley Airfield
        ["KAUS"] = (30.1945, -97.6699),  // Austin
        ["KSAT"] = (29.5337, -98.4698),  // San Antonio
        ["KRDU"] = (35.8776, -78.7875),  // Raleigh-Durham
        ["KBNA"] = (36.1263, -86.6774),  // Nashville
        ["KPHL"] = (39.8744, -75.2424),  // Philadelphia

        // Kanada
        ["CYYZ"] = (43.6777, -79.6248),  // Toronto Pearson
        ["CYVR"] = (49.1947, -123.1840), // Vancouver
        ["CYUL"] = (45.4706, -73.7408),  // Montreal
        ["CYQB"] = (46.7911, -71.3933),  // Quebec City

        // Asien
        ["RJTT"] = (35.5494, 139.7798),  // Tokyo Haneda
        ["VHHH"] = (22.3080, 113.9185),  // Hong Kong
        ["WSSS"] = (1.3644, 103.9915),   // Singapore Changi
        ["RKSI"] = (37.4691, 126.4505),  // Seoul Incheon
        ["ZBAA"] = (40.0799, 116.6031),  // Beijing
        ["ZSPD"] = (31.1443, 121.8083),  // Shanghai Pudong
        ["OMDB"] = (25.2528, 55.3644),   // Dubai
        ["VABB"] = (19.0896, 72.8656),   // Mumbai
        ["VIDP"] = (28.5562, 77.1000),   // Delhi
        ["VTBS"] = (13.6900, 100.7501),  // Bangkok

        // Ozeanien
        ["YSSY"] = (-33.9399, 151.1753), // Sydney
        ["YMML"] = (-37.6690, 144.8410), // Melbourne
        ["NZAA"] = (-37.0082, 174.7850), // Auckland

        // Südamerika
        ["SBGR"] = (-23.4356, -46.4731), // São Paulo
        ["SCEL"] = (-33.3930, -70.7858), // Santiago
        ["SAEZ"] = (-34.8222, -58.5358), // Buenos Aires

        // Afrika
        ["FAOR"] = (-26.1392, 28.2460),  // Johannesburg
        ["HECA"] = (30.1219, 31.4056),   // Kairo
        ["GMMN"] = (33.3675, -7.5900),   // Casablanca
        ["GGOV"] = (11.8948, -15.6531),  // Bissau
        ["GOOY"] = (14.7397, -17.4902),  // Dakar
        ["GABS"] = (13.4699, -16.6522),  // Banjul
        ["GULB"] = (11.5886, -13.1386),  // Labé
        ["GUCY"] = (10.3866, -9.2617),   // Conakry
        ["DXXX"] = (6.1657, 1.2546),     // Lomé
        ["DGAA"] = (5.6052, -0.1668),    // Accra
        ["DBBB"] = (6.3573, 2.3844),     // Cotonou
        ["DNMM"] = (6.5774, 3.3212),     // Lagos
        ["FKKD"] = (4.0061, 9.7194),     // Douala
        ["FCBB"] = (-4.2517, 15.2531),   // Brazzaville
        ["FZAA"] = (-4.3858, 15.4446),   // Kinshasa
        ["HKJK"] = (-1.3192, 36.9278),   // Nairobi
        ["HTDA"] = (-6.8781, 39.2026),   // Dar es Salaam
        ["FMEE"] = (-20.4302, 57.6836),  // Mauritius
        ["FMMI"] = (-18.7969, 47.4789),  // Antananarivo
        ["FACT"] = (-33.9649, 18.6017),  // Kapstadt
    };

    /// <summary>
    /// Findet den nächsten Flughafen zur angegebenen GPS-Position
    /// </summary>
    /// <param name="lat">Breitengrad</param>
    /// <param name="lon">Längengrad</param>
    /// <param name="maxDistanceNm">Maximale Suchentfernung in Nautischen Meilen (Standard: 10 NM)</param>
    /// <returns>ICAO-Code des nächsten Flughafens oder null wenn keiner in Reichweite</returns>
    public static string? FindNearestAirport(double lat, double lon, double maxDistanceNm = 10)
    {
        string? nearestIcao = null;
        double nearestDistance = double.MaxValue;

        foreach (var (icao, coords) in Airports)
        {
            var distance = CalculateDistance(lat, lon, coords.Lat, coords.Lon);
            if (distance < nearestDistance && distance <= maxDistanceNm)
            {
                nearestDistance = distance;
                nearestIcao = icao;
            }
        }

        return nearestIcao;
    }

    /// <summary>
    /// Berechnet die Großkreis-Distanz zwischen zwei Punkten (Haversine-Formel)
    /// </summary>
    /// <returns>Distanz in Nautischen Meilen</returns>
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusNm * c;
    }

    private static double ToRad(double deg) => deg * (Math.PI / 180);
}
