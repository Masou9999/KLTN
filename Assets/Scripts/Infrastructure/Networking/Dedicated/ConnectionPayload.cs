using System;
using System.Text;
using UnityEngine;

/// <summary>
/// ConnectionData a dedicated client sends with its NGO connection request:
/// UTF-8 JSON { "playerId", "accessToken", "matchId" } (FR-MM-09). The server
/// verifies it through AdmitPlayer before approving the connection.
/// </summary>
[Serializable]
public sealed class ConnectionPayload
{
    #region Constants

    // Keeps a malicious client from making the server parse a huge payload.
    public const int MaxPayloadBytes = 8192;

    #endregion

    #region Fields

    public string playerId;
    public string accessToken;
    public string matchId;

    #endregion

    #region Serialization

    public byte[] ToBytes()
    {
        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(this));
    }

    public static bool TryParse(byte[] data, out ConnectionPayload payload)
    {
        payload = null;

        if (data == null || data.Length == 0 || data.Length > MaxPayloadBytes)
        {
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<ConnectionPayload>(Encoding.UTF8.GetString(data));
            return payload != null;
        }
        catch (Exception)
        {
            payload = null;
            return false;
        }
    }

    #endregion
}
