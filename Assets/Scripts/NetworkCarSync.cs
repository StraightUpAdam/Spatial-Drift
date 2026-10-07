using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// Client-authoritative car networking + the per-car slice of race state.
///
/// Transform: the owner simulates locally and publishes its world pose; remote
/// clients interpolate a ghost.  Race: the owner also publishes its spline
/// distance (so the server can count laps for every car), and the SERVER writes
/// back this car's lap and finish position.  The grid-slot start position is
/// pushed from the server so client-owned cars line up correctly.
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(CarController))]
public class NetworkCarSync : NetworkBehaviour
{
    [Tooltip("How quickly a remote 'ghost' car eases toward its replicated pose.")]
    public float interpSpeed = 16f;

    CarController car;
    NetworkRaceDirector director;

    // Pose — owner writes, everyone interpolates.
    readonly NetworkVariable<Vector3> netPos =
        new NetworkVariable<Vector3>(writePerm: NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<Quaternion> netRot =
        new NetworkVariable<Quaternion>(Quaternion.identity,
                                        writePerm: NetworkVariableWritePermission.Owner);

    // Spline progress — owner writes; the server reads it to count laps.
    // (Declared as fields, not properties — NGO discovers NetworkVariables by
    //  scanning the type's fields.)
    public readonly NetworkVariable<float> Distance =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    // Lateral offset across the track — owner writes; read by NetworkBumpSystem
    // so every car knows where the others sit for car-to-car contact.
    public readonly NetworkVariable<float> Lateral =
        new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

    // Race results — SERVER writes, everyone reads (HUD / position ranking).
    public readonly NetworkVariable<int> Lap =
        new NetworkVariable<int>(0, writePerm: NetworkVariableWritePermission.Server);
    public readonly NetworkVariable<int> FinishPos =
        new NetworkVariable<int>(0, writePerm: NetworkVariableWritePermission.Server);
    // Race clock at the moment this car took the flag (results table).
    public readonly NetworkVariable<float> FinishTime =
        new NetworkVariable<float>(0f, writePerm: NetworkVariableWritePermission.Server);

    // Driver name — the owner publishes PlayerProfile.Name; the server names AI.
    public readonly NetworkVariable<FixedString32Bytes> PlayerName =
        new NetworkVariable<FixedString32Bytes>(default, writePerm: NetworkVariableWritePermission.Owner);

    // Grid slot (startDistance, startLateral) — SERVER writes, owner applies.
    readonly NetworkVariable<Vector2> netStart =
        new NetworkVariable<Vector2>(writePerm: NetworkVariableWritePermission.Server);

    // Car paint id (see PlayerProfile.ColorById) — the OWNER writes it.  Player
    // cars: the local owner publishes its chosen colour.  AI cars: the server
    // owns them, so the server publishes their colour via ServerSetColorId().
    public readonly NetworkVariable<int> ColorId =
        new NetworkVariable<int>(-1, writePerm: NetworkVariableWritePermission.Owner);

    /// In the Spatial Drift minigame (owner writes).  Other players see the car
    /// burst through a rift into / out of its ghost form.
    public readonly NetworkVariable<bool> InSpatialDrift =
        new NetworkVariable<bool>(false, writePerm: NetworkVariableWritePermission.Owner);
    SpatialDriftMode sdMode;

    public bool IsLocalPlayerCar => IsOwner && car != null && !car.aiControlled;

    /// AI cars carry AI paint ids (100+) — the one tell clients can see.
    public bool IsAI => ColorId.Value >= 100;

    public string DisplayName
    {
        get
        {
            string n = PlayerName.Value.ToString();
            if (!string.IsNullOrEmpty(n)) return n;
            return IsAI ? PlayerProfile.AIName(ColorId.Value - 100) : $"DRIVER {OwnerClientId + 1}";
        }
    }

    void Awake() => car = GetComponent<CarController>();

    public override void OnNetworkSpawn()
    {
        netStart.OnValueChanged += OnStartChanged;
        ColorId.OnValueChanged  += OnColorChanged;
        InSpatialDrift.OnValueChanged += OnSpatialDriftChanged;

        if (IsOwner)
        {
            if (car != null)
            {
                car.enabled = true; car.inputEnabled = false;
                // Owned cars bump themselves away from others (mutual contact).
                if (GetComponent<NetworkBumpSystem>() == null)
                    gameObject.AddComponent<NetworkBumpSystem>();
            }

            if (car != null && !car.aiControlled)
            {
                CarController.LocalPlayerOverride = car;
                car.EnablePlayerSystems();
                if (GetComponent<RacingHUD>() == null)
                    gameObject.AddComponent<RacingHUD>();   // self-wires

                // Publish the local player's chosen paint to everyone.
                ColorId.Value = PlayerProfile.LocalColorId;
                PlayerName.Value = new FixedString32Bytes(PlayerProfile.Name);
            }
        }
        else if (car != null)
        {
            car.enabled = false;   // ghost — network drives the transform
        }

        // Apply any state that already arrived with the spawn.
        if (netStart.Value != Vector2.zero) OnStartChanged(default, netStart.Value);
        if (ColorId.Value >= 0) ApplyColor(ColorId.Value);
    }

    public override void OnNetworkDespawn()
    {
        netStart.OnValueChanged -= OnStartChanged;
        ColorId.OnValueChanged  -= OnColorChanged;
        InSpatialDrift.OnValueChanged -= OnSpatialDriftChanged;
        if (IsOwner && CarController.LocalPlayerOverride == car)
            CarController.LocalPlayerOverride = null;
    }

    /// Server-only: assign an AI car's paint (the server owns AI cars).
    public void ServerSetColorId(int id)
    {
        if (IsOwner) ColorId.Value = id;   // server is the owner of AI cars
    }

    /// Server-only: name an AI car.
    public void ServerSetName(string n)
    {
        if (IsOwner) PlayerName.Value = new FixedString32Bytes(n ?? "");
    }

    void OnColorChanged(int _, int id) => ApplyColor(id);

    void ApplyColor(int id)
    {
        if (id < 0) return;
        var visual = GetComponent<CarVisual>();
        if (visual != null) visual.SetColor(PlayerProfile.ColorById(id));
    }

    /// True once the server has assigned this car a grid slot.
    public bool HasGridSlot => netStart.Value != Vector2.zero;

    /// Server-only: assign this car's grid slot.  Owner applies it locally.
    public void ServerSetGridSlot(float distance, float lateral)
    {
        if (IsServer) netStart.Value = new Vector2(distance, lateral);
    }

    void OnStartChanged(Vector2 _, Vector2 slot)
    {
        // Only the owner re-homes its own simulated car onto the grid slot.
        if (!IsOwner || car == null) return;
        car.startDistance      = slot.x;
        car.startLateralOffset = slot.y;
        car.ResetToStart();
    }

    void OnSpatialDriftChanged(bool was, bool now)
    {
        if (IsOwner) return;                    // the owner already shows its own rift
        var ghost = GetComponent<CarGhost>();
        if (ghost == null) ghost = gameObject.AddComponent<CarGhost>();
        ghost.SetGhost(now, true);
        SpatialRift.SpawnAround(transform);
    }

    void Update()
    {
        if (IsOwner)
        {
            if (sdMode == null) sdMode = GetComponent<SpatialDriftMode>();
            bool inSd = sdMode != null && sdMode.CurrentPhase == SpatialDriftMode.Phase.Active;
            if (InSpatialDrift.Value != inSd) InSpatialDrift.Value = inSd;
            netPos.Value = transform.position;
            netRot.Value = transform.rotation;
            if (car != null)
            {
                Distance.Value   = car.DistanceAlongTrack;
                Lateral.Value    = car.LateralOffset;
                car.inputEnabled = RaceAllowsInput();
            }
        }
        else if (car != null)
        {
            float k = 1f - Mathf.Exp(-interpSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, netPos.Value, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, netRot.Value, k);
        }
    }

    // Cars are frozen until the director says we're racing (countdown lock).
    // Time trial has no countdown lock — everyone free-drives immediately.
    bool RaceAllowsInput()
    {
        if (director == null) director = FindFirstObjectByType<NetworkRaceDirector>();
        if (director == null) return true;     // no race in this scene → free drive
        return director.IsTimeTrial
            || director.CurrentState == NetworkRaceDirector.RaceState.Racing;
    }
}
