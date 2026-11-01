#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using KLTN.Game.Content;
using KLTN.Game.Domain.Economy;
using UnityEditor;
using UnityEngine;

namespace KLTN.Game.Editor
{
    /// <summary>
    /// Creates Resources/ShopCatalog.asset from the balancing table and exports
    /// Firebase/firestore.rules with the price table embedded
    /// (+ Firebase/firestore.indexes.json for the leaderboard queries).
    /// </summary>
    [InitializeOnLoad]
    public static class ShopCatalogInstaller
    {
        private const string AssetPath = "Assets/Resources/ShopCatalog.asset";
        private const string RulesPath = "Firebase/firestore.rules";
        private const string IndexesPath = "Firebase/firestore.indexes.json";

        static ShopCatalogInstaller()
        {
            EditorApplication.delayCall += () =>
            {
                if (AssetDatabase.LoadAssetAtPath<ShopCatalog>(AssetPath) == null)
                {
                    Install();
                }
            };
        }

        // P = I x clamp(sqrt(I / (2*cost + 1)), 0.8, 1.3). See claude/shop-economy-plan.md.
        private static List<ShopCatalogEntry> Table()
        {
            return new List<ShopCatalogEntry>
            {
                E("MaDa", Currency.None, 0, CardTier.Weak, 3.6f),
                E("MaDoi", Currency.None, 0, CardTier.Weak, 5.2f),
                E("MaGa", Currency.None, 0, CardTier.Weak, 6.5f),
                E("MaLon", Currency.None, 0, CardTier.Weak, 6.5f),
                E("QuyMotDo", Currency.None, 0, CardTier.Weak, 6.6f),
                E("VongNhi", Currency.None, 0, CardTier.Weak, 7.0f),
                E("OngKe", Currency.None, 0, CardTier.Weak, 7.5f),
                E("QuyCau", Currency.None, 0, CardTier.Weak, 8.3f),

                E("MaMatMam", Currency.Coins, 150, CardTier.Weak, 8.3f),
                E("MaLai", Currency.Coins, 200, CardTier.Weak, 8.6f),
                E("ThienLinhCai", Currency.Coins, 200, CardTier.Mid, 9.0f),
                E("AmBinh", Currency.Coins, 350, CardTier.Mid, 10.2f),
                E("MaCo", Currency.Coins, 350, CardTier.Mid, 10.2f),
                E("MaCangSung", Currency.Coins, 550, CardTier.Mid, 12.5f),
                E("MaTroi", Currency.Coins, 550, CardTier.Mid, 12.7f),
                E("ThanTrung", Currency.Coins, 600, CardTier.Mid, 13.0f),

                E("LinhMieu", Currency.Silver, 10, CardTier.Strong, 15.0f),
                E("MaTranh", Currency.Silver, 12, CardTier.Strong, 20.1f),
                E("QuyDaXoa", Currency.Silver, 15, CardTier.Strong, 34.0f),
            };
        }

        [MenuItem("Tools/KLTN/Install Shop Catalog")]
        public static void Install()
        {
            ShopCatalog catalog = AssetDatabase.LoadAssetAtPath<ShopCatalog>(AssetPath);

            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ShopCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }

            Undo.RecordObject(catalog, "Install Shop Catalog");
            catalog.ReplaceEntries(Table());
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            foreach (ShopCatalogEntry entry in Table())
            {
                if (Resources.Load($"Cards/{entry.cardId}") == null)
                {
                    Debug.LogWarning($"[ShopCatalog] Card asset not found: Resources/Cards/{entry.cardId}");
                }
            }

            ExportRules();
            Debug.Log($"[ShopCatalog] Installed {Table().Count} entries at {AssetPath}.");
        }

        [MenuItem("Tools/KLTN/Export Firestore Rules")]
        public static void ExportRules()
        {
            var prices = new StringBuilder();
            var starters = new StringBuilder();

            foreach (ShopCatalogEntry e in Table())
            {
                if (e.currency == Currency.None)
                {
                    starters.Append(starters.Length == 0 ? "" : ", ").Append($"'{e.cardId}'");
                    continue;
                }

                string currency = e.currency == Currency.Coins ? "coins" : "silver";
                prices.Append(prices.Length == 0 ? "" : ",\n        ")
                    .Append($"'{e.cardId}': ['{currency}', {e.price}]");
            }

            string rules = RulesTemplate
                .Replace("__PRICES__", prices.ToString())
                .Replace("__STARTERS__", starters.ToString());

            string root = Directory.GetParent(Application.dataPath).FullName;
            string fullPath = Path.Combine(root, RulesPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, rules.Replace("\r\n", "\n"));
            File.WriteAllText(Path.Combine(root, IndexesPath), IndexesJson.Replace("\r\n", "\n"));
            Debug.Log(
                $"[ShopCatalog] Firestore rules written to {RulesPath} (+ {IndexesPath}). "
                    + "Paste the rules into Firebase Console > Firestore > Rules and create the indexes."
            );
        }

        // Leaderboard page: points desc, winRate desc, username asc (+ __name__).
        // Own position: points == x AND (winRate > y, or winRate == y AND username < z).
        private const string IndexesJson = @"{
  ""indexes"": [
    {
      ""collectionGroup"": ""entries"",
      ""queryScope"": ""COLLECTION"",
      ""fields"": [
        { ""fieldPath"": ""points"", ""order"": ""DESCENDING"" },
        { ""fieldPath"": ""winRate"", ""order"": ""DESCENDING"" },
        { ""fieldPath"": ""username"", ""order"": ""ASCENDING"" }
      ]
    },
    {
      ""collectionGroup"": ""entries"",
      ""queryScope"": ""COLLECTION"",
      ""fields"": [
        { ""fieldPath"": ""points"", ""order"": ""ASCENDING"" },
        { ""fieldPath"": ""winRate"", ""order"": ""ASCENDING"" }
      ]
    },
    {
      ""collectionGroup"": ""entries"",
      ""queryScope"": ""COLLECTION"",
      ""fields"": [
        { ""fieldPath"": ""points"", ""order"": ""ASCENDING"" },
        { ""fieldPath"": ""winRate"", ""order"": ""ASCENDING"" },
        { ""fieldPath"": ""username"", ""order"": ""ASCENDING"" }
      ]
    }
  ],
  ""fieldOverrides"": []
}
";

        // Prices are embedded in the rules, so no /shopCatalog collection needs seeding.
        // Every economy write is a :commit batch; rules check the batch result with getAfter/existsAfter.
        private const string RulesTemplate = @"rules_version = '2';

// GENERATED by Tools/KLTN/Export Firestore Rules (ShopCatalogInstaller.cs). Do not edit by hand.
// Economy: clients only spend (exact catalog price, checked here). Match rewards and season
// points are server-trusted: Cloud Code MatchServerModule.ReportResult writes them with a
// service account (bypasses rules), so every client write to them is denied.
// Ranking: config/season and seasons/{s} are written only by the Cloud Code season module.
// Clients may only create their own empty season row (0 points) so they are listed before playing.
// matches/{matchId} (allocation + MATCH_TOKEN hash) and ugsPlayers/{playerId} are server-only.
service cloud.firestore {
  match /databases/{database}/documents {

    function prices() {
      return {
        __PRICES__
      };
    }

    function starters() {
      return [__STARTERS__];
    }

    function isOwner(uid) {
      return request.auth != null && request.auth.uid == uid;
    }

    function signedIn() {
      return request.auth != null;
    }

    function usernamePath(name) {
      return /databases/$(database)/documents/usernames/$(name);
    }

    function seasonConfig() {
      return get(/databases/$(database)/documents/config/season).data;
    }

    // Seasons are consecutive fixed-length periods counted from an anchor (config/season), so
    // season s is open as soon as the clock reaches it: no scheduler tick is needed to reset.
    // A settings change is stored as a pending calendar that applies from pendingSeasonId on.
    function seasonCalendar(cfg, sid) {
      return cfg.get('pendingSeasonId', 0) > 0 && sid >= cfg.pendingSeasonId
        ? {'id': cfg.pendingSeasonId, 'start': cfg.pendingStartMs, 'dur': cfg.pendingDurationMs, 'lock': cfg.pendingLockMs}
        : {'id': cfg.anchorSeasonId, 'start': cfg.anchorStartMs, 'dur': cfg.durationMs, 'lock': cfg.lockMs};
    }

    // Season s is running now (used for the empty row a player creates before playing).
    function seasonRunning(s) {
      let sid = int(s);
      let c = seasonCalendar(seasonConfig(), sid);
      let start = c.start + (sid - c.id) * c.dur;
      let now = request.time.toMillis();
      return s == string(sid) && sid >= c.id && now >= start && now < start + c.dur;
    }

    function validUsername(name) {
      return name is string && name.matches('user[0-9]{6}');
    }

    function userPath(uid) {
      return /databases/$(database)/documents/users/$(uid);
    }

    function cardPath(uid, cardId) {
      return /databases/$(database)/documents/users/$(uid)/ownedCards/$(cardId);
    }

    // FR-SHOP-05: the only wallet change a client may make is paying the exact price of a card
    // it does not own yet, in the same batch that creates the card. Coins/silver never go up here.
    function isPurchase(uid) {
      let d = request.resource.data;
      let o = resource.data;
      let card = d.get('lastPurchase', '');
      return d.diff(o).affectedKeys().hasOnly(['coins', 'silver', 'lastPurchase', 'updatedAt'])
        && card is string
        && card in prices()
        && !exists(cardPath(uid, card))
        && existsAfter(cardPath(uid, card))
        && d.coins >= 0 && d.silver >= 0
        && ((prices()[card][0] == 'coins'
              && d.coins == o.coins - prices()[card][1]
              && d.silver == o.silver)
          || (prices()[card][0] == 'silver'
              && d.silver == o.silver - prices()[card][1]
              && d.coins == o.coins));
    }

    function isUsernameClaim(uid) {
      let d = request.resource.data;
      let o = resource.data;
      return d.diff(o).affectedKeys().hasOnly(['username', 'updatedAt'])
        && !('username' in o)
        && validUsername(d.username)
        && existsAfter(usernamePath(d.username))
        && getAfter(usernamePath(d.username)).data.uid == uid;
    }

    // Empty row (0 points = Bronze, 0 %) so a player is listed before their first match.
    function isEmptyStanding(s, uid) {
      let d = request.resource.data;
      return d.keys().hasOnly(['username', 'points', 'wins', 'losses', 'draws', 'matches', 'winRate', 'updatedAt'])
        && d.points == 0 && d.wins == 0 && d.losses == 0 && d.draws == 0 && d.matches == 0 && d.winRate == 0
        && d.username == get(userPath(uid)).data.username
        && d.updatedAt == request.time
        && seasonRunning(s);
    }

    match /users/{uid} {
      allow read: if isOwner(uid);

      allow create: if isOwner(uid)
        && request.resource.data.keys().hasOnly(['coins', 'silver', 'schemaVersion', 'createdAt', 'updatedAt', 'username'])
        && request.resource.data.coins == 0
        && request.resource.data.silver == 0
        && (!('username' in request.resource.data)
          || (validUsername(request.resource.data.username)
            && existsAfter(usernamePath(request.resource.data.username))));

      allow update: if isOwner(uid) && (isPurchase(uid) || isUsernameClaim(uid));
      allow delete: if false;

      match /ownedCards/{cardId} {
        allow read: if isOwner(uid);

        allow create: if isOwner(uid)
          && request.resource.data.keys().hasOnly(['acquiredAt', 'source'])
          && (
            (request.resource.data.source == 'starter'
              && cardId in starters()
              && !exists(userPath(uid))
              && existsAfter(userPath(uid)))
            || (cardId in prices()
              && request.resource.data.source == prices()[cardId][0]
              && getAfter(userPath(uid)).data.get('lastPurchase', '') == cardId)
          );

        allow update, delete: if false;
      }

      // Written by MatchServerModule.ReportResult only (FR-SHOP-08/09).
      match /matchRewards/{matchId} {
        allow read: if isOwner(uid);
        allow write: if false;
      }
    }

    // Unique public names. The user doc must point back at the name in the same batch.
    match /usernames/{name} {
      allow get: if signedIn();
      allow list: if false;

      allow create: if signedIn()
        && validUsername(name)
        && request.resource.data.keys().hasOnly(['uid', 'createdAt'])
        && request.resource.data.uid == request.auth.uid
        && getAfter(userPath(request.auth.uid)).data.get('username', '') == name;

      allow update, delete: if false;
    }

    // Written by the Cloud Code season module only.
    match /config/{doc} {
      allow read: if signedIn();
      allow write: if false;
    }

    match /seasons/{s} {
      allow read: if signedIn();
      allow write: if false;

      match /entries/{uid} {
        allow read: if signedIn();

        // Points and statistics are written by MatchServerModule.ReportResult only (FR-RANK-07/11).
        allow create: if isOwner(uid) && isEmptyStanding(s, uid);
        allow update, delete: if false;

        match /results/{matchId} {
          allow read: if isOwner(uid);
          allow write: if false;
        }
      }
    }

    // Allocation records (players, MATCH_TOKEN hash, settlement): allocator and MatchServerModule only.
    match /matches/{matchId=**} {
      allow read, write: if false;
    }

    // UGS PlayerId -> Firebase uid, verified by MatchServerModule.
    match /ugsPlayers/{playerId} {
      allow read, write: if false;
    }

    // Match servers (capacity, slots, heartbeat, SERVER_TOKEN hash): allocator and MatchServerModule only.
    match /servers/{serverId=**} {
      allow read, write: if false;
    }
  }
}
";

        private static ShopCatalogEntry E(string id, Currency currency, int price, CardTier tier, float power)
        {
            return new ShopCatalogEntry
            {
                cardId = id,
                currency = currency,
                price = price,
                tier = tier,
                powerScore = power,
            };
        }
    }
}
#endif
