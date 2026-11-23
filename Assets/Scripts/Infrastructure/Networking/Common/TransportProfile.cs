using System;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// Chooses the Unity Transport protocol for each connection route so desktop and WebGL
/// clients reach the same servers. Browsers cannot open UDP sockets, so the dedicated
/// server listens on WebSocket and clients use WSS whenever the endpoint is a hostname.
/// </summary>
/// <remarks>
/// Edgegap terminates TLS at its edge (port <c>tls_upgrade</c>), so the server itself
/// listens on plain WebSocket while clients connect with <c>wss://&lt;fqdn&gt;:&lt;port&gt;</c>.
/// An IP endpoint (local server, load test) keeps plain <c>ws://</c> because a certificate
/// cannot be validated against an IP address.
/// </remarks>
public static class TransportProfile
{
    #region Queries

    /// <summary>True when the build runs inside a browser (WebGL player, not the Editor).</summary>
    public static bool IsBrowser
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Returns true when the endpoint must be reached over TLS: a DNS hostname other than
    /// <c>localhost</c>. IP addresses and localhost stay unencrypted.
    /// </summary>
    public static bool RequiresTls(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        return Uri.CheckHostName(host) == UriHostNameType.Dns
            && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Configures the dedicated server to listen on plain WebSocket on every interface.
    /// TLS is added by the hosting edge, never by the server process.
    /// </summary>
    public static void ConfigureDedicatedServer(UnityTransport transport, ushort port)
    {
        transport.UseWebSockets = true;
        transport.UseEncryption = false;
        transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");
    }

    /// <summary>
    /// Points a client at a dedicated server over WebSocket, enabling TLS for hostnames.
    /// Used by matchmaking, reconnect and the load-test bot so all reach the same listener.
    /// </summary>
    /// <param name="host">Edgegap FQDN (WSS) or an IP address (plain WS).</param>
    public static void ConfigureDedicatedClient(UnityTransport transport, string host, ushort port)
    {
        bool secure = RequiresTls(host);
        transport.UseWebSockets = true;
        transport.UseEncryption = secure;

        if (secure)
        {
            // No CA certificate: validation uses the OS/browser store (Edgegap uses a public CA).
            transport.SetClientSecrets(host);
        }

        transport.SetConnectionData(host, port);
    }

    /// <summary>
    /// Restores the listen-host (Relay) profile. Relay encrypts on its own; only the socket
    /// type has to match the protocol the Relay SDK picks.
    /// </summary>
    /// <remarks>
    /// The Multiplayer SDK's <c>RelayProtocol.Default</c> is WSS whenever the build target is
    /// WebGL, including Play Mode in the Editor, so this follows <c>UNITY_WEBGL</c> rather
    /// than <see cref="IsBrowser"/>.
    /// </remarks>
    public static void ConfigureListen(UnityTransport transport)
    {
#if UNITY_WEBGL
        transport.UseWebSockets = true;
#else
        transport.UseWebSockets = false;
#endif
        transport.UseEncryption = false;
    }

    #endregion
}
