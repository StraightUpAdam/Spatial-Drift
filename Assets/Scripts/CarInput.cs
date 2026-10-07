/// Per-frame driving inputs for a CarController.
///
/// Single-player: CarController fills this from keyboard each frame.
/// Multiplayer (future): the local owner fills it from keyboard and replicates
/// it to the server; remote players receive replicated inputs and feed them
/// into their CarController.  Keeping the struct tiny (8 bytes packed) makes
/// the eventual netcode trivial.
public struct CarInput
{
    public float accel;   // 0..1 — throttle
    public float steer;   // -1..1 — left/right
    public bool  brake;   // brake / reverse trigger
}
