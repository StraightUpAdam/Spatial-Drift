using UnityEngine;

/// A pressable menu row.  Wire behaviour in code via Clicked.
public class MenuButton : MenuRow
{
    public Sfx sound = Sfx.Confirm;
    public event System.Action Clicked;

    public override void Submit()
    {
        if (!interactable) { Shake(); MenuAudio.Play(Sfx.Error); return; }
        Punch();
        MenuAudio.Play(sound);
        Clicked?.Invoke();
    }

    public void SetLabel(string text) { if (label != null) label.text = text; }
}
