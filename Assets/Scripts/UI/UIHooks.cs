using UnityEngine;
using UnityEngine.EventSystems;

/// Single-purpose pointer callbacks.  (EventTrigger implements EVERY pointer
/// interface, which swallows scroll/drag events meant for a parent ScrollRect —
/// these only catch the one event they're for.)
public class UIHoverHook : MonoBehaviour, IPointerEnterHandler
{
    public System.Action onEnter;
    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
}

public class UIPointerUpHook : MonoBehaviour, IPointerUpHandler
{
    public System.Action onUp;
    public void OnPointerUp(PointerEventData e) => onUp?.Invoke();
}
