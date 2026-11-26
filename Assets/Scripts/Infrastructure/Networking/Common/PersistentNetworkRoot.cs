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

    // Browsers throttle or pause hidden tabs and block the single main thread while loading
    // scenes, so 5 s of silence is normal there. Still well inside the 30 s seat hold.
    [SerializeField]
    private int browserDisconnectTimeoutMilliseconds = 15000;

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

        if (transport == null)
        {
            return;
        }

        // Listen-host is the default route; dedicated routes switch to WebSocket on demand.
        TransportProfile.ConfigureListen(transport);

        int timeout = TransportProfile.IsBrowser
            ? browserDisconnectTimeoutMilliseconds
            : disconnectTimeoutMilliseconds;

        if (timeout > 0)
        {
            transport.DisconnectTimeoutMS = timeout;
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
