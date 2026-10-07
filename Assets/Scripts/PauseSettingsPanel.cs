using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// One baked row of a results table (solo + multiplayer share it).
[System.Serializable]
public class ResultRow
{
    public GameObject      root;
    public CanvasGroup     group;
    public Image           paint, highlight;
    public TextMeshProUGUI pos, name, time, best;

    /// Shrink the table card to the rows in use (a 3-car race shouldn't sit in
    /// an 8-row box).
    public static void FitCard(ResultRow[] rows, int count)
    {
        if (rows == null || rows.Length == 0 || rows[0] == null || rows[0].root == null) return;
        if (rows[0].root.transform.parent is RectTransform card)
            card.sizeDelta = new Vector2(card.sizeDelta.x, 66f + Mathf.Clamp(count, 1, rows.Length) * 64f + 14f);
    }
}

/// The in-race SETTINGS page (pause ▸ settings): the options you'd actually
/// want mid-race — volumes, camera FOV, speed units, minimap, ghost.  Baked
/// into the race scenes; opens over the pause page and applies live.
[DisallowMultipleComponent]
public class PauseSettingsPanel : MonoBehaviour
{
    public MenuScreen  screen;
    public MenuSlider  master, effects, fov;
    public MenuStepper units, minimap, ghost;

    static readonly string[] OffOn = { "OFF", "ON" };
    System.Action onClose;
    bool wired;

    void Wire()
    {
        if (wired) return;
        wired = true;
        fov.min = 55f; fov.max = 100f; fov.step = 1f; fov.percent = false; fov.format = "{0:0}°";
        units.SetOptions(new[] { "KM/H", "MPH" }, 0);
        minimap.SetOptions(OffOn, 1);
        ghost.SetOptions(OffOn, 1);

        master.Changed  += v => GameSettings.MasterVolume  = v;
        effects.Changed += v => GameSettings.EffectsVolume = v;
        fov.Changed     += v => GameSettings.CameraFov     = v;
        units.Changed   += i => GameSettings.SpeedMph      = i == 1;
        minimap.Changed += i => GameSettings.ShowMinimap   = i == 1;
        ghost.Changed   += i => GameSettings.ShowGhost     = i == 1;
        screen.Backed   += Close;
    }

    public void Open(System.Action closed)
    {
        Wire();
        onClose = closed;
        master.SetValue(GameSettings.MasterVolume);
        effects.SetValue(GameSettings.EffectsVolume);
        fov.SetValue(GameSettings.CameraFov);
        units.SetIndex(GameSettings.SpeedMph ? 1 : 0);
        minimap.SetIndex(GameSettings.ShowMinimap ? 1 : 0);
        ghost.SetIndex(GameSettings.ShowGhost ? 1 : 0);
        screen.Show(0);
        MenuAudio.Play(Sfx.Open, 0.7f);
    }

    public bool IsOpen => screen != null && screen.IsOpen;

    public void Close()
    {
        if (!screen.IsOpen) return;
        screen.Hide();
        var a = onClose;
        onClose = null;
        a?.Invoke();
    }
}
