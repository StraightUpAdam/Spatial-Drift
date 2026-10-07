using UnityEngine;
using UnityEngine.EventSystems;

/// Marks a scene as a MENU scene (baked by the builders): menu music plays,
/// settings apply, and every baked text gets the menu font.  Also makes sure
/// an EventSystem exists for mouse input and turns off Unity's own UI
/// navigation (MenuInput drives focus; letting both run would double-move).
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class MenuSceneRoot : MonoBehaviour
{
    [Tooltip("Menu scenes play music; set false for in-game overlays.")]
    public bool playMusic = true;

    void Awake()
    {
        GameSettings.Apply();
        MenuAudio.Ensure();
        if (playMusic) MenuMusic.SetWanted(true);
        UITheme.ApplyFontToScene();
        EnsureEventSystem();
        Cursor.lockState = CursorLockMode.None;
    }

    public static void EnsureEventSystem()
    {
        var es = EventSystem.current != null ? EventSystem.current : FindAnyObjectByType<EventSystem>();
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            es = go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }
        es.sendNavigationEvents = false;
    }
}
