using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// Minimal host/join harness for early netcode testing.  Put this on the
/// NetworkManager GameObject.  In Play mode it draws three buttons (Host /
/// Client / Server) until a session starts, then shows status + a Disconnect
/// button.  Localhost by default — type a LAN IP into the field to join
/// another machine on the same network.
///
/// This is scaffolding for development, not the final UI — the real host/join
/// flow will live on the main menu once the core sync works.
[RequireComponent(typeof(NetworkManager))]
public class NetworkBootstrap : MonoBehaviour
{
    [Tooltip("Address a Client connects to.  127.0.0.1 = same machine.")]
    public string connectAddress = "127.0.0.1";
    public ushort port = 7777;

    NetworkManager net;
    UnityTransport transport;
    string addressField;

    void Awake()
    {
        net       = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
        addressField = connectAddress;
    }

    void ApplyConnectionData()
    {
        if (transport != null)
            transport.SetConnectionData(addressField, port);
    }

    void OnGUI()
    {
        // Scale the dev overlay up a bit so it's usable at any resolution.
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * 1.6f);

        GUILayout.BeginArea(new Rect(12, 12, 260, 260), GUI.skin.box);

        if (net != null && (net.IsServer || net.IsClient))
        {
            string role = net.IsHost ? "HOST" : net.IsServer ? "SERVER" : "CLIENT";
            GUILayout.Label($"<b>{role}</b>");
            GUILayout.Label($"Connected clients: {net.ConnectedClientsList.Count}");
            if (GUILayout.Button("Disconnect")) net.Shutdown();
        }
        else
        {
            GUILayout.Label("<b>SPATIAL DRIFT — NET TEST</b>");
            GUILayout.Label("Connect address:");
            addressField = GUILayout.TextField(addressField);

            if (GUILayout.Button("Host  (play + serve)"))
            {
                ApplyConnectionData();
                net.StartHost();
            }
            if (GUILayout.Button("Client  (join)"))
            {
                ApplyConnectionData();
                net.StartClient();
            }
            if (GUILayout.Button("Server  (dedicated)"))
            {
                ApplyConnectionData();
                net.StartServer();
            }
        }

        GUILayout.EndArea();
    }
}
