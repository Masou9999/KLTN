using System;
using System.Collections;
using System.Collections.Generic;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using KLTN.Infrastructure.CloudCode;
using Unity.Netcode;
using UnityEngine;

namespace KLTN.Game.Networking
{
    /// <summary>
    /// Bridges the client's owned-card list into the match without making Networking
    /// depend on the economy/persistence layer. PlayerProfileService fills the provider.
    /// </summary>
    public static class MatchLoadoutRegistry
    {
        public static Func<IReadOnlyList<string>> LocalOwnedCardIds;
    }

    public sealed partial class NetworkMatchBridge : NetworkBehaviour
    {
        #region Loadout State

        private const float LoadoutTimeoutSeconds = 10f;

        // Server: owned card IDs reported by each seat. On a dedicated server they only narrow
        // the list verified by AdmitPlayer (see ServerLoadoutPolicy).
        private readonly Dictionary<SeatId, string[]> loadoutBySeat = new Dictionary<SeatId, string[]>();
        private Coroutine loadoutTimeoutCoroutine;
        private bool loadoutTimeoutElapsed;
        private string currentMatchId;
        private bool currentMatchRanked;
        private long currentMatchStartedUtcMs;
        private ShopCatalog shopCatalog;

        #endregion

        #region Client Side

        private void SubmitLocalLoadout()
        {
            IReadOnlyList<string> owned = MatchLoadoutRegistry.LocalOwnedCardIds?.Invoke();

            if (owned == null || owned.Count == 0)
            {
                Debug.LogWarning("[Loadout] No owned cards available locally; server will use the starter set.");
                owned = Array.Empty<string>();
            }

            SubmitLoadoutRpc(string.Join(",", owned));
        }

        #endregion

        #region Server Side

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitLoadoutRpc(string ownedCardIdsCsv, RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;

            AssignClient(senderClientId);

            if (!seatByClient.TryGetValue(senderClientId, out SeatId seat))
            {
                Debug.LogWarning($"[Loadout] Ignored loadout from unseated client {senderClientId}.");
                return;
            }

            if (matchState != null)
            {
                Debug.LogWarning($"[Loadout] Match already started; ignored late loadout for {seat}.");
                return;
            }

            string[] ids = string.IsNullOrWhiteSpace(ownedCardIdsCsv)
                ? Array.Empty<string>()
                : ownedCardIdsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            loadoutBySeat[seat] = ids;
            Debug.Log($"[Loadout] {seat} (client {senderClientId}) reported {ids.Length} owned cards.");

            if (TryInitializeMatch())
            {
                BroadcastUpdate();
            }
        }

        private bool AreLoadoutsReady()
        {
            if (loadoutBySeat.ContainsKey(SeatId.Host) && loadoutBySeat.ContainsKey(SeatId.Guest))
            {
                return true;
            }

            if (loadoutTimeoutElapsed)
            {
                return true;
            }

            if (loadoutTimeoutCoroutine == null)
            {
                loadoutTimeoutCoroutine = StartCoroutine(LoadoutTimeout());
            }

            return false;
        }

        private IEnumerator LoadoutTimeout()
        {
            yield return new WaitForSecondsRealtime(LoadoutTimeoutSeconds);

            loadoutTimeoutCoroutine = null;

            if (matchState != null || !IsServer)
            {
                yield break;
            }

            Debug.LogWarning("[Loadout] Timed out waiting for loadouts; missing seats use the starter set.");
            loadoutTimeoutElapsed = true;

            if (TryInitializeMatch())
            {
                BroadcastUpdate();
            }
        }

        private void ResetLoadouts()
        {
            if (loadoutTimeoutCoroutine != null)
            {
                StopCoroutine(loadoutTimeoutCoroutine);
                loadoutTimeoutCoroutine = null;
            }

            loadoutBySeat.Clear();
            loadoutTimeoutElapsed = false;
            currentMatchId = null;
        }

        /// <summary>
        /// Keeps only known card IDs. On a dedicated server the owned list comes from
        /// AdmitPlayer and the client's report can only narrow it (FR-MATCH-11); listen-host
        /// trusts the report (private match). Falls back to the starter set when the list is
        /// missing or smaller than the starter set, then builds a weighted 25-card deck.
        /// Requires definitionsById to be populated.
        /// </summary>
        private IReadOnlyList<CardDefinition> BuildDeckForSeat(SeatId seat)
        {
            shopCatalog ??= ShopCatalog.Load();

            List<string> starterIds = shopCatalog != null ? shopCatalog.StarterCardIds() : new List<string>();
            loadoutBySeat.TryGetValue(seat, out string[] reported);
            IReadOnlyList<string> verified = VerifiedOwnedCardIds(seat);
            IReadOnlyList<string> allowed = ServerLoadoutPolicy.Resolve(verified, reported);

            if (verified != null)
            {
                Debug.Log(
                    $"[Loadout] {seat}: {verified.Count} verified owned, "
                        + $"{reported?.Length ?? 0} reported, {allowed.Count} allowed."
                );
            }

            List<CardDefinition> owned = ResolveDefinitions(allowed);
            List<CardDefinition> starters = ResolveDefinitions(starterIds);

            if (owned.Count < Math.Max(1, starters.Count))
            {
                owned = starters.Count > 0 ? starters : new List<CardDefinition>(definitionsById.Values);
            }

            try
            {
                IReadOnlyList<CardDefinition> deck = WeightedDeckBuilder.Build(
                    owned,
                    id => shopCatalog != null ? shopCatalog.WeightOf(id) : 2
                );

                Debug.Log($"[Loadout] {seat} deck: {Describe(deck)}");
                return deck;
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"[Loadout] Weighted deck failed for {seat} ({exception.Message}); using default builder.");
                return DefaultDeckBuilder.Build(owned);
            }
        }

        /// <summary>Owned IDs verified by the backend for the seat; null when unverified.</summary>
        private IReadOnlyList<string> VerifiedOwnedCardIds(SeatId seat)
        {
            if (!MatchServerContext.IsDedicated)
            {
                return null;
            }

            foreach (KeyValuePair<ulong, SeatId> pair in seatByClient)
            {
                if (pair.Value == seat && MatchServerContext.TryGetAdmission(pair.Key, out PlayerAdmission admission))
                {
                    return admission.OwnedCardIds;
                }
            }

            return null;
        }

        private List<CardDefinition> ResolveDefinitions(IEnumerable<string> ids)
        {
            var result = new List<CardDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (ids == null)
            {
                return result;
            }

            foreach (string raw in ids)
            {
                string id = raw?.Trim();

                if (!string.IsNullOrEmpty(id) && seen.Add(id) && definitionsById.TryGetValue(id, out CardDefinition definition))
                {
                    result.Add(definition);
                }
            }

            return result;
        }

        private static string Describe(IReadOnlyList<CardDefinition> deck)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (CardDefinition definition in deck)
            {
                counts.TryGetValue(definition.Id, out int current);
                counts[definition.Id] = current + 1;
            }

            var parts = new List<string>();

            foreach (KeyValuePair<string, int> pair in counts)
            {
                parts.Add($"{pair.Key}x{pair.Value}");
            }

            return string.Join(", ", parts);
        }

        #endregion
    }
}
