using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EdgegapAllocatorModule.Client;
using EdgegapAllocatorModule.Client.Models;
using KLTN.CloudCode.Shared;
using KLTN.Game.Domain.Ranking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Matchmaker;
using Unity.Services.CloudCode.Core;
using Unity.Services.Matchmaker.Model;
using IExecutionContext = Unity.Services.CloudCode.Core.IExecutionContext;

namespace EdgegapAllocatorModule;

/// <summary>
/// Module configuration for dependency injection.
/// Registers IGameApiClient as a singleton for accessing Unity services like Secret Manager.
/// </summary>
public class ModuleConfig : ICloudCodeSetup
{
    public void Setup(ICloudCodeConfig config)
    {
        config.Dependencies.AddSingleton(GameApiClient.Create());
        config.Dependencies.AddScoped<IEdgegapHttpClientFactory, EdgegapHttpClientFactory>();
        config.Dependencies.AddSingleton(
            new FirestoreHttp(new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
        );
    }
}

/// <summary>HttpClient for Firestore (wrapped so it is not confused with the Edgegap clients).</summary>
public sealed class FirestoreHttp(HttpClient client)
{
    public HttpClient Client { get; } = client;
}

/// <summary>
/// Where a match runs. ServerId goes to matches/{matchId}.serverId (servers/{serverId} for a
/// multi-match server, the Edgegap request id otherwise); DeploymentId is the Edgegap request id
/// Matchmaker_PollAllocation reads the IP and port from.
/// </summary>
public sealed record ServerPlacement(string ServerId, string DeploymentId, string? Error);

public class EdgegapAllocator(
    IGameApiClient gameApiClient,
    IEdgegapHttpClientFactory httpClientFactory,
    FirestoreHttp firestoreHttp,
    ILogger<EdgegapAllocator> logger
) : IMatchmakerAllocator
{
    // Configuration - users should modify these constants for their setup
    private const string ApplicationName = "kltn"; // TODO: Replace with actual application name
    private const string VersionName = "26.11.01-01.20.09-UTC"; // TODO: Replace with actual version name
    private const string PortName = "gameport"; // TODO: Replace with actual port name

    // Edgegap Constants
    private const string EdgegapApiUrl = "https://api.edgegap.com";
    private const bool ForceCachedLocation = false; // You need to activate a cached application version in Edgegap to use this feature

    // Secret names - these must match the secrets stored in Unity Dashboard
    private const string EdgegapApiTokenSecretName = "EDGEGAP_API_TOKEN_KXD";
    private const string FirebaseSecretName = "FIREBASE_SERVICE_ACCOUNT";

    // UGS service account the match server uses to call MatchServerModule (AdmitPlayer, ReportResult).
    private const string MatchServerKeyIdSecretName = "MATCH_SERVER_SA_KEY_ID";
    private const string MatchServerSecretSecretName = "MATCH_SERVER_SA_SECRET";

    // Optional: matches per server process (MATCH_CAPACITY). Missing → 1, one match per deployment.
    private const string ServerCapacitySecretName = "MATCH_SERVER_CAPACITY";
    private const int DefaultServerCapacity = 1;

    private const int PlayersPerMatch = 2;

    private static GoogleServiceAccount? firebaseAccount;

    /// <summary>
    /// Checks the players' real tiers (FR-RANK-12, the ticket's rank_tier is only a hint), records
    /// matches/{matchId} with the SHA-256 of a fresh MATCH_TOKEN, then starts the match server with
    /// MATCH_ID, MATCH_TOKEN and the UGS_* service account variables.
    /// </summary>
    [CloudCodeFunction("Matchmaker_AllocateServer")]
    public async Task<AllocateResponse> Allocate(IExecutionContext context, AllocateRequest request)
    {
        try
        {
            List<Player> players = ExtractPlayers(request.MatchmakingResults.MatchProperties);
            List<string> playerIds = players
                .Select(player => player.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();

            if (playerIds.Count != PlayersPerMatch)
            {
                return Error(
                    $"Expected {PlayersPerMatch} players in the match, got {playerIds.Count}."
                );
            }

            FirestoreAdmin firestore = await FirestoreAsync(context);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var tiers = new List<RankTier>();

            foreach (string playerId in playerIds)
            {
                string? uid = await PlayerDirectory.GetUidAsync(firestore, playerId);

                if (uid == null)
                {
                    return Error(
                        $"Player {playerId} has no verified tier: call MatchServerModule/GetTicketTier before matchmaking."
                    );
                }

                (RankTier tier, _) = await SeasonDocuments.TierAsync(firestore, uid, now);
                tiers.Add(tier);
            }

            if (!MatchRecords.TiersCanMeet(tiers))
            {
                logger.LogWarning(
                    "Refused match {MatchmakerMatchId}: tiers {Tiers} differ by more than {Max}.",
                    request.MatchId,
                    string.Join(",", tiers),
                    RankMatchmakingPolicy.MaxTierDifference
                );
                return Error(
                    $"Rank tiers {string.Join(", ", tiers)} differ by more than {RankMatchmakingPolicy.MaxTierDifference}."
                );
            }

            string matchId = MatchRecords.NewMatchId();
            string matchToken = MatchRecords.NewToken();
            await MatchRecords.CreateAsync(
                firestore,
                matchId,
                MatchRecords.HashToken(matchToken),
                playerIds,
                tiers,
                true,
                request.MatchId,
                now
            );

            int capacity = await ServerCapacityAsync(context);
            ServerPlacement? placement = null;

            if (capacity > 1)
            {
                // FR-MM-10: a running server with a free slot first; it fetches the match
                // (with a new MATCH_TOKEN) through MatchServerModule.GetAssignedMatches.
                ServerInfo? reused = await ServerRecords.TryReserveAsync(firestore, matchId, now);

                if (reused != null)
                {
                    logger.LogInformation(
                        "Match {MatchId} placed on running server {ServerId} ({Used}/{Capacity}).",
                        matchId,
                        reused.ServerId,
                        reused.Slots.Count,
                        reused.Capacity
                    );
                    placement = new ServerPlacement(reused.ServerId, reused.DeploymentId, null);
                }
            }

            placement ??= await PlaceMatchAsync(
                context,
                firestore,
                players,
                playerIds,
                request.MatchId,
                matchId,
                matchToken,
                capacity,
                now
            );

            if (placement.Error != null)
            {
                return Error(placement.Error);
            }

            await MatchRecords.SetServerAsync(firestore, matchId, placement.ServerId);

            return new AllocateResponse(AllocateStatus.Created)
            {
                AllocationData = new Dictionary<string, object>
                {
                    { "requestId", placement.DeploymentId },
                    { "matchId", matchId },
                    { "serverId", placement.ServerId },
                },
            };
        }
        catch (Exception e)
        {
            logger.LogError(e, "Edgegap deployment failed");
            return Error(e.Message);
        }
    }

    /// <summary>
    /// Starts a new Edgegap deployment for the match. With capacity > 1 the deployment also gets
    /// SERVER_ID/SERVER_TOKEN and a servers/{serverId} record, so later matches can reuse it.
    /// </summary>
    private async Task<ServerPlacement> PlaceMatchAsync(
        IExecutionContext context,
        FirestoreAdmin firestore,
        List<Player> players,
        List<string> playerIds,
        string matchmakerMatchId,
        string matchId,
        string matchToken,
        int capacity,
        long now
    )
    {
        Secret edgegapApiToken = await gameApiClient.SecretManager.GetSecret(
            context,
            EdgegapApiTokenSecretName
        );
        Secret serviceKeyId = await gameApiClient.SecretManager.GetSecret(
            context,
            MatchServerKeyIdSecretName
        );
        Secret serviceSecret = await gameApiClient.SecretManager.GetSecret(
            context,
            MatchServerSecretSecretName
        );
        using HttpClient client = httpClientFactory.Create(edgegapApiToken.Value);

        var playerIps = ExtractValidPlayerIps(players);

        var users = playerIps
            .Select(ip => new DeploymentUser
            {
                UserType = "ip_address",
                UserData = new UserData { IpAddress = ip.ToString() },
            })
            .ToList();

        if (users.Count == 0)
        {
            // If no valid player IPs were found, add a placeholder user with geo coordinates
            logger.LogWarning(
                "No valid player IPs found in match properties, adding dummy user with geo coordinates, please add player_ip to player custom data"
            );
            users.Add(
                new DeploymentUser
                {
                    UserType = "geo_coordinates",
                    UserData = new UserData { Latitude = 35.891111, Longitude = -106.263889 },
                }
            );
        }

        // Contract with the match server (00_TongQuan_KeHoach.md): secrets are hidden in the Edgegap dashboard.
        var environment = new List<EnvironmentVariable>
        {
            Variable("MATCH_ID", matchId, false),
            Variable("MATCH_TOKEN", matchToken, true),
            Variable("MATCHMAKER_MATCH_ID", matchmakerMatchId, false),
            Variable("MATCH_PLAYERS", string.Join(",", playerIds), false),
            Variable("MATCH_CAPACITY", capacity.ToString(System.Globalization.CultureInfo.InvariantCulture), false),
            Variable("UGS_PROJECT_ID", context.ProjectId, false),
            Variable("UGS_ENVIRONMENT_ID", context.EnvironmentId, false),
            Variable("UGS_SERVICE_ACCOUNT_KEY_ID", serviceKeyId.Value, true),
            Variable("UGS_SERVICE_ACCOUNT_SECRET", serviceSecret.Value, true),
        };

        string serverId = string.Empty;
        string serverToken = string.Empty;

        if (capacity > 1)
        {
            serverId = MatchRecords.NewMatchId();
            serverToken = MatchRecords.NewToken();
            environment.Add(Variable("SERVER_ID", serverId, false));
            environment.Add(Variable("SERVER_TOKEN", serverToken, true));
        }

        var deploymentRequest = new DeploymentRequest
        {
            Application = ApplicationName,
            Version = VersionName,
            RequireCachedLocations = ForceCachedLocation,
            Users = users,
            EnvironmentVariables = environment,
            Tags = ["ugs-matchmaker"],
        };

        var content = new StringContent(
            JsonConvert.SerializeObject(deploymentRequest),
            Encoding.UTF8,
            "application/json"
        );
        HttpResponseMessage response = await client.PostAsync(
            $"{EdgegapApiUrl}/v2/deployments",
            content
        );

        string responseContent = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError(
                "Edgegap deployment failed with status code {ResponseStatusCode}: {ResponseContent}",
                response.StatusCode,
                responseContent
            );
            return new ServerPlacement(string.Empty, string.Empty, responseContent);
        }

        var edgegapDeployment = JsonConvert.DeserializeObject<DeploymentResponse>(responseContent);
        string deploymentId = edgegapDeployment?.RequestId ?? string.Empty;

        if (capacity <= 1)
        {
            return new ServerPlacement(deploymentId, deploymentId, null);
        }

        // The first match already fills a slot; the server is reused once it sends a heartbeat.
        await ServerRecords.CreateAsync(
            firestore,
            serverId,
            deploymentId,
            MatchRecords.HashToken(serverToken),
            capacity,
            matchId,
            now
        );
        return new ServerPlacement(serverId, deploymentId, null);
    }

    /// <summary>MATCH_SERVER_CAPACITY secret (1..16); missing or invalid means one match per server.</summary>
    private async Task<int> ServerCapacityAsync(IExecutionContext context)
    {
        try
        {
            Secret secret = await gameApiClient.SecretManager.GetSecret(context, ServerCapacitySecretName);

            if (int.TryParse(secret.Value, out int capacity))
            {
                return Math.Clamp(capacity, 1, ServerRecords.MaxCapacity);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "No {Secret} secret; one match per server.", ServerCapacitySecretName);
        }

        return DefaultServerCapacity;
    }

    private static EnvironmentVariable Variable(string key, string value, bool hidden)
    {
        return new EnvironmentVariable
        {
            Key = key,
            Value = value ?? string.Empty,
            IsHidden = hidden,
        };
    }

    private static AllocateResponse Error(string message)
    {
        return new AllocateResponse(AllocateStatus.Error) { Message = message };
    }

    private async Task<FirestoreAdmin> FirestoreAsync(IExecutionContext context)
    {
        if (firebaseAccount == null)
        {
            Secret secret = await gameApiClient.SecretManager.GetSecret(
                context,
                FirebaseSecretName
            );
            firebaseAccount = GoogleServiceAccount.FromJson(secret.Value);
        }

        return new FirestoreAdmin(firestoreHttp.Client, firebaseAccount);
    }

    [CloudCodeFunction("Matchmaker_PollAllocation")]
    public async Task<PollResponse> Poll(IExecutionContext context, PollRequest request)
    {
        var requestId = request.AllocationData["requestId"].ToString();
        try
        {
            Secret edgegapApiToken = await gameApiClient.SecretManager.GetSecret(
                context,
                EdgegapApiTokenSecretName
            );
            HttpClient client = httpClientFactory.Create(edgegapApiToken.Value);
            HttpResponseMessage response = await client.GetAsync(
                $"{EdgegapApiUrl}/v1/status/{requestId}"
            );
            string responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new PollResponse(PollStatus.Error) { Message = responseContent };
            }

            var deploymentStatus = JsonConvert.DeserializeObject<DeploymentStatusResponse>(
                responseContent
            );

            if (deploymentStatus == null)
            {
                return new PollResponse(PollStatus.Error)
                {
                    Message = "Deployment status response is null",
                };
            }

            switch (deploymentStatus.CurrentStatus)
            {
                case DeploymentStatus.Ready:
                {
                    if (deploymentStatus.Ports == null || deploymentStatus.Ports.Count == 0)
                    {
                        return new PollResponse(PollStatus.Error)
                        {
                            Message =
                                $"Deployment has no exposed ports. Response: {responseContent}",
                        };
                    }

                    if (!deploymentStatus.Ports.TryGetValue(PortName, out var port))
                    {
                        return new PollResponse(PollStatus.Error)
                        {
                            Message =
                                $"Requested port {PortName} is not exposed by the deployment. Please verify the name of the port in the Edgegap dashboard.",
                        };
                    }

                    if (deploymentStatus.PublicIp == null)
                    {
                        return new PollResponse(PollStatus.Error)
                        {
                            Message =
                                $"Deployment is READY but public_ip is missing. Response: {responseContent}",
                        };
                    }

                    return new PollResponse(PollStatus.Allocated)
                    {
                        AssignmentData = AssignmentData.IpPort(
                            deploymentStatus.PublicIp,
                            port.External
                        ),
                    };
                }
                case DeploymentStatus.Error:
                    return new PollResponse(PollStatus.Error)
                    {
                        Message =
                            $"Deployment failed with the current error: {deploymentStatus.ErrorDetail}",
                    };
                case DeploymentStatus.Terminating:
                case DeploymentStatus.Terminated:
                    return new PollResponse(PollStatus.Error)
                    {
                        Message =
                            "Deployment is terminated or terminated and can't receive connection anymore",
                    };
                case DeploymentStatus.Deploying:
                case DeploymentStatus.Seeking:
                default:
                    return new PollResponse(PollStatus.Pending);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error polling Edgegap");
            return new PollResponse(PollStatus.Error) { Message = e.Message };
        }
    }

    /// <summary>
    /// Safely extracts the matched players from Matchmaker match properties ("Players").
    /// </summary>
    private List<Player> ExtractPlayers(Dictionary<string, object> matchProperties)
    {
        var result = new List<Player>();

        if (!matchProperties.TryGetValue("Players", out var playersObj))
        {
            logger.LogDebug("No Players key found in MatchProperties");
            return result;
        }

        if (playersObj is not JArray playersArray)
        {
            logger.LogWarning(
                "Players is not a JArray (actual type: {type})",
                playersObj.GetType()
            );
            return result;
        }

        try
        {
            return playersArray.ToObject<List<Player>>() ?? result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize Players array");
            return result;
        }
    }

    /// <summary>
    /// Safely extracts all valid player IP addresses from the players' "player_ip" custom data key.
    /// </summary>
    private IReadOnlyList<IPAddress> ExtractValidPlayerIps(List<Player> players)
    {
        var result = new List<IPAddress>();

        foreach (var player in players)
        {
            if (player.CustomData is not JObject customData)
                continue;

            if (!customData.TryGetValue("player_ip", out var ipToken))
                continue;

            var ipString = ipToken.Type switch
            {
                JTokenType.String => ipToken.Value<string>(),
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(ipString))
                continue;

            if (IPAddress.TryParse(ipString, out var ip))
            {
                result.Add(ip);
            }
            else
            {
                logger.LogDebug("Invalid IP format ignored: {ip}", ipString);
            }
        }

        return result;
    }
}
