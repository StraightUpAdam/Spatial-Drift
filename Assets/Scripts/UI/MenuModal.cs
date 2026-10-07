using UnityEngine;
using TMPro;

/// Baked confirm dialog ("QUIT RACE?  YES / NO").  A MenuScreen on top of
/// whatever is open; Back = the cancel choice.
[DisallowMultipleComponent]
[RequireComponent(typeof(MenuScreen))]
public class MenuModal : MonoBehaviour
{
    public TextMeshProUGUI title, body;
    public MenuButton      yesButton, noButton;

    MenuScreen screen;
    System.Action onYes, onNo;

    void Awake()
    {
        screen = GetComponent<MenuScreen>();
        screen.showOnStart = false;
        screen.Backed += () => Close(false);
        if (yesButton != null) yesButton.Clicked += () => Close(true);
        if (noButton  != null) noButton.Clicked  += () => Close(false);
    }

    public bool IsOpen => screen != null && screen.IsOpen;

    public void Ask(string t, string b, string yes, string no, System.Action yesAction, System.Action noAction = null)
    {
        if (title != null) title.text = t;
        if (body  != null) body.text  = b;
        yesButton?.SetLabel(yes);
        noButton?.SetLabel(no);
        onYes = yesAction;
        onNo  = noAction;
        MenuAudio.Play(Sfx.Open, 0.8f);
        // Default focus on the safe choice.
        screen.Show(screen.items.IndexOf(noButton));
    }

    void Close(bool yes)
    {
        screen.Hide();
        var a = yes ? onYes : onNo;
        onYes = onNo = null;
        a?.Invoke();
    }
}
