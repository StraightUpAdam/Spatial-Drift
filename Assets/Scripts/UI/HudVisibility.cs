using UnityEngine;

/// Hides the race HUD (speedo, tach, shifter, lap panel, minimap, hints — the
/// runtime canvases sorted 100–199) while the pause / results menus are up,
/// so the menus sit over a clean frame.  Uses a CanvasGroup so it never
/// fights components that toggle their own canvas.
public static class HudVisibility
{
    public static void Set(bool visible)
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (!c.isRootCanvas || c.sortingOrder < 100 || c.sortingOrder >= 200) continue;
            var g = c.GetComponent<CanvasGroup>();
            if (g == null) g = c.gameObject.AddComponent<CanvasGroup>();
            g.alpha = visible ? 1f : 0f;
        }
    }
}
