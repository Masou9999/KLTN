using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public sealed class PersistentNetworkRoot : MonoBehaviour
{
    #region Singleton State

    private static PersistentNetworkRoot instance;

    #endregion

    #region Configuration

    // UTP's default 30 s would let a dead connection eat the whole reconnect grace period;
    // 5 s lets the server tell the opponent within the 10 s target (NFR-09, FR-NET-06).
    [SerializeField]
    private int disconnectTimeoutMilliseconds = 5000;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        UnityTransport transport = GetComponent<UnityTransport>();

        if (transport != null && disconnectTimeoutMilliseconds > 0)
        {
            transport.DisconnectTimeoutMS = disconnectTimeoutMilliseconds;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    #endregion
}
