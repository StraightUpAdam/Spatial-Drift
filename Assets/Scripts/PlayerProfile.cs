using UnityEngine;

/// Per-player cosmetic profile.  The local player's chosen car colour persists
/// across sessions (PlayerPrefs) and, in multiplayer, is the source each car
/// reads its paint from on spawn.
///
/// Colours are referenced by a small integer *id* so they can travel on a plain
/// NetworkVariable&lt;int&gt; (which NGO serialises out of the box) rather than a
/// raw Color.  Players use ids 0..PlayerPalette.Length-1; AI cars use 100+index.
/// ColorById resolves either kind back to a Color on the receiving client.
public static class PlayerProfile
{
    const string ColorKey = "spatialdrift.car_color";
    const string NameKey  = "spatialdrift.driver_name";

    /// Display names for the paints, same order as PlayerPalette.
    public static readonly string[] PaintNames =
        { "INFERNO", "COBALT", "AMBER", "VIPER", "VOID", "SAKURA", "GLACIER", "PEARL" };

    /// The driver's name — shown in the lobby, results and on the menu chip.
    /// First run gets a random "DRIVER-1234" tag that sticks until changed.
    public static string Name
    {
        get
        {
            string n = PlayerPrefs.GetString(NameKey, "");
            if (string.IsNullOrWhiteSpace(n))
            {
                n = "DRIVER-" + Random.Range(1000, 10000);
                PlayerPrefs.SetString(NameKey, n);
                PlayerPrefs.Save();
            }
            return n;
        }
        set
        {
            string v = (value ?? "").Trim().ToUpperInvariant();
            if (v.Length == 0) return;
            if (v.Length > 16) v = v.Substring(0, 16);
            PlayerPrefs.SetString(NameKey, v);
            PlayerPrefs.Save();
        }
    }

    /// Named AI rivals (by grid index) — they read as characters, not "AI 3".
    static readonly string[] AiNames = { "VEGA", "NOVA", "RONIN", "KESTREL", "ONYX", "HALO", "ZEPHYR" };
    public static string AIName(int aiIndex) => AiNames[Mathf.Abs(aiIndex) % AiNames.Length];

    /// Pickable player paints — bright, distinct, on-theme.
    public static readonly Color[] PlayerPalette =
    {
        new Color(0.85f, 0.12f, 0.08f),   // red
        new Color(0.20f, 0.50f, 1.00f),   // blue
        new Color(0.95f, 0.65f, 0.10f),   // amber
        new Color(0.20f, 0.85f, 0.45f),   // green
        new Color(0.69f, 0.42f, 1.00f),   // violet (theme)
        new Color(0.95f, 0.35f, 0.70f),   // pink
        new Color(0.10f, 0.85f, 0.90f),   // cyan
        new Color(0.95f, 0.92f, 0.95f),   // white
    };

    /// Cooler, slightly desaturated tones for AI so they read as "the field"
    /// and don't get mistaken for a human player's paint.
    static readonly Color[] AiPalette =
    {
        new Color(0.55f, 0.58f, 0.62f),
        new Color(0.45f, 0.35f, 0.55f),
        new Color(0.30f, 0.45f, 0.55f),
        new Color(0.55f, 0.45f, 0.35f),
        new Color(0.40f, 0.55f, 0.45f),
        new Color(0.55f, 0.40f, 0.45f),
        new Color(0.38f, 0.42f, 0.58f),
    };

    /// Index into PlayerPalette currently chosen by the local player.
    public static int ColorIndex
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ColorKey, 4), 0, PlayerPalette.Length - 1);
        set
        {
            PlayerPrefs.SetInt(ColorKey, Mathf.Clamp(value, 0, PlayerPalette.Length - 1));
            PlayerPrefs.Save();
        }
    }

    /// The local player's resolved car colour.
    public static Color LocalColor => PlayerPalette[ColorIndex];

    /// The colour id the local player publishes to its networked car.
    public static int LocalColorId => ColorIndex;

    /// Encode an AI car's colour as a transmittable id.
    public static int AIColorId(int aiIndex) => 100 + Mathf.Max(0, aiIndex);

    /// Resolve any colour id (player or AI) back to a Color.
    public static Color ColorById(int id)
    {
        if (id >= 100) return AiPalette[(id - 100) % AiPalette.Length];
        if (id < 0)    return PlayerPalette[0];
        return PlayerPalette[id % PlayerPalette.Length];
    }
}
