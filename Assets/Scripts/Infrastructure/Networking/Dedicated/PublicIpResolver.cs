using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

public static class PublicIpResolver
{
    #region Configuration

    private const string Endpoint = "https://api.ipify.org";

    #endregion

    #region Public API

    public static async Task<string> ResolveAsync(CancellationToken cancellationToken)
    {
        using UnityWebRequest request = UnityWebRequest.Get(Endpoint);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new MatchmakingFailedException(
                MatchmakingFailure.NoConnection,
                $"Cannot resolve public IP: {request.error}"
            );
        }

        string publicIp = request.downloadHandler.text.Trim();

        if (string.IsNullOrWhiteSpace(publicIp))
        {
            throw new MatchmakingFailedException(
                MatchmakingFailure.NoConnection,
                "Public IP response was empty."
            );
        }

        return publicIp;
    }

    #endregion
}
