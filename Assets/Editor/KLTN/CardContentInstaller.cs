#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using CMCMProductions;
using KLTN.Game.Content;
using KLTN.Game.Domain;
using UnityEditor;
using UnityEngine;

namespace KLTN.Game.Editor
{
    public static class CardContentInstaller
    {
        private const string CardFolder = "Assets/Resources/Cards/";

        private static readonly string[] CardIds =
        {
            "MaTroi",
            "QuyCau",
            "MaDoi",
            "MaGa",
            "LinhMieu",
            "MaDa",
            "ThienLinhCai",
            "QuyDaXoa",
            "MaTranh",
            "QuyMotDo",
            "AmBinh",
            "MaCangSung",
            "MaLon",
            "MaMatMam",
            "OngKe",
            "MaLai",
            "MaCo",
            "VongNhi",
            "ThanTrung",
        };

        [MenuItem("Tools/KLTN/Install Unit Card Content")]
        public static void Install()
        {
            ValidateAssetsExist();

            Configure(
                "MaTroi",
                "Ma Trơi",
                health: 5,
                damage: 5,
                energy: 10,
                UnitKeyword.Fearsome,
                "Khi ở DrawHand, tôi tốn ít hơn 1 mana với mỗi đồng minh "
                    + "đã chết trong trận này.",
                Passive(costReductionPerAlliedDeathThisGame: 1)
            );

            Configure(
                "QuyCau",
                "Quỷ Cẩu",
                2,
                3,
                2,
                UnitKeyword.None,
                "Hỗ trợ khi tấn công hoặc phòng thủ: đồng minh "
                    + "bên phải nhận +2|+0 khi còn trên sân.",
                Passive(),
                Ability(
                    "quy_cau_support",
                    AbilityTrigger.Support,
                    new[]
                    {
                        Effect(
                            EffectKind.Buff,
                            EffectTarget.TriggerSubject,
                            amount: 2,
                            duration: EffectDuration.WhileOnBoard
                        ),
                    }
                )
            );

            Configure(
                "MaDoi",
                "Ma Đói",
                2,
                3,
                0,
                UnitKeyword.None,
                "Play: giết một đồng minh.",
                Passive(),
                Ability(
                    "ma_doi_play",
                    AbilityTrigger.Play,
                    new[] { Effect(EffectKind.Kill, EffectTarget.PrimarySelection) },
                    new[]
                    {
                        Target(
                            AbilityTargetSlot.Primary,
                            TargetRelation.Ally,
                            AbilityTargetZone.ActiveRoster
                        ),
                    }
                )
            );

            Configure(
                "MaGa",
                "Ma Gà",
                1,
                2,
                1,
                UnitKeyword.None,
                "Play: giảm 1 mana cho một Unit đồng minh " + "khác trong DrawHand.",
                Passive(),
                Ability(
                    "ma_ga_play",
                    AbilityTrigger.Play,
                    new[]
                    {
                        Effect(
                            EffectKind.ReduceCost,
                            EffectTarget.PrimarySelection,
                            amount: 1
                        ),
                    },
                    new[]
                    {
                        Target(
                            AbilityTargetSlot.Primary,
                            TargetRelation.Ally,
                            AbilityTargetZone.DrawHand
                        ),
                    },
                    missingTargetPolicy: MissingTargetPolicy.SkipAbility
                )
            );

            Configure(
                "LinhMieu",
                "Linh Miêu",
                4,
                4,
                7,
                UnitKeyword.None,
                "Summon: hồi sinh đồng minh mạnh nhất đang ở Graveyard, "
                    + "xét trên toàn bộ trận đấu.",
                Passive(),
                Ability(
                    "linh_mieu_summon",
                    AbilityTrigger.Summon,
                    new[] { Effect(EffectKind.Revive, EffectTarget.StrongestDeadAlly) }
                )
            );

            Configure(
                "MaDa",
                "Ma Da",
                1,
                3,
                2,
                UnitKeyword.CannotBlock | UnitKeyword.Ephemeral,
                "Không thể chặn. Phù du. Last Breath: Khi một đồng minh "
                    + "Phù du tấn công, hồi sinh tôi để cùng tấn công. "
                    + "Hiệu ứng vẫn tồn tại nếu tôi được hồi sinh trước đó.",
                Passive(joinsAttackFromGraveyard: true, requiresEphemeralAttacker: true)
            );

            Configure(
                "ThienLinhCai",
                "Thiên Linh Cái",
                3,
                3,
                4,
                UnitKeyword.None,
                "Play: giết một đồng minh rồi hồi sinh nó.",
                Passive(),
                Ability(
                    "thien_linh_cai_play",
                    AbilityTrigger.Play,
                    new[]
                    {
                        Effect(EffectKind.Kill, EffectTarget.PrimarySelection),
                        Effect(EffectKind.Revive, EffectTarget.PrimarySelection),
                    },
                    new[]
                    {
                        Target(
                            AbilityTargetSlot.Primary,
                            TargetRelation.Ally,
                            AbilityTargetZone.ActiveRoster
                        ),
                    }
                )
            );

            Configure(
                "QuyDaXoa",
                "Quỷ Dạ Xoa",
                6,
                10,
                9,
                UnitKeyword.Fearsome,
                "Đáng sợ. Play: giảm một nửa máu Nexus địch, "
                    + "làm tròn máu còn lại lên. Last Breath: trở về DrawHand.",
                Passive(),
                Ability(
                    "quy_da_xoa_play",
                    AbilityTrigger.Play,
                    new[] { Effect(EffectKind.HalfNexus, EffectTarget.EnemyNexus) }
                ),
                Ability(
                    "quy_da_xoa_last_breath",
                    AbilityTrigger.Death,
                    new[] { Effect(EffectKind.ReturnToDrawHand, EffectTarget.Source) }
                )
            );

            Configure(
                "MaTranh",
                "Ma Trành",
                6,
                8,
                8,
                UnitKeyword.Fearsome,
                "Đáng sợ. Summon: nếu một đồng minh đã chết "
                    + "ở vòng trước, giết 2 kẻ địch yếu nhất.",
                Passive(),
                Ability(
                    "ma_tranh_summon",
                    AbilityTrigger.Summon,
                    new[]
                    {
                        Effect(EffectKind.Kill, EffectTarget.WeakestEnemies, count: 2),
                    },
                    condition: AbilityCondition.AllyDiedPreviousRound
                )
            );

            Configure(
                "QuyMotDo",
                "Quỷ Một Dò",
                2,
                3,
                2,
                UnitKeyword.Fearsome,
                "Đáng sợ.",
                Passive()
            );

            Configure(
                "AmBinh",
                "Âm Binh",
                2,
                2,
                3,
                UnitKeyword.CannotBlock,
                "Không thể chặn. Khi chết, hồi sinh ở RoundStart "
                    + "kế tiếp và nhận +1|+1 cho mỗi lần đã chết.",
                Passive(revivesAtRoundStart: true, powerAndHealthPerDeath: 1)
            );

            Configure(
                "MaCangSung",
                "Ma Càng Sùng",
                5,
                2,
                5,
                UnitKeyword.None,
                "Khi một đồng minh khác chết, hút 1 máu " + "từ Nexus địch.",
                Passive(),
                Ability(
                    "ma_cang_sung_ally_death",
                    AbilityTrigger.AllyDeath,
                    new[]
                    {
                        Effect(EffectKind.Damage, EffectTarget.EnemyNexus, amount: 1),
                        Effect(EffectKind.Heal, EffectTarget.AlliedNexus, amount: 1),
                    }
                )
            );

            Configure(
                "MaLon",
                "Ma Lon",
                1,
                1,
                1,
                UnitKeyword.None,
                "Lần đầu một đồng minh khác chết, " + "ban cho tôi +2|+2.",
                Passive(),
                Ability(
                    "ma_lon_first_ally_death",
                    AbilityTrigger.AllyDeath,
                    new[]
                    {
                        Effect(
                            EffectKind.Buff,
                            EffectTarget.Source,
                            amount: 2,
                            secondaryAmount: 2
                        ),
                    },
                    oncePerMatch: true
                )
            );

            Configure(
                "MaMatMam",
                "Ma Mặt Mâm",
                1,
                3,
                2,
                UnitKeyword.None,
                "Last Breath: rút 1 lá và hồi 2 máu cho Nexus.",
                Passive(),
                Ability(
                    "ma_mat_mam_last_breath",
                    AbilityTrigger.Death,
                    new[]
                    {
                        Effect(EffectKind.Draw, EffectTarget.SourceOwner, amount: 1),
                        Effect(EffectKind.Heal, EffectTarget.AlliedNexus, amount: 2),
                    }
                )
            );

            Configure(
                "OngKe",
                "Ông Kẹ",
                1,
                4,
                4,
                UnitKeyword.None,
                "Play: giết một đồng minh để rút 2 lá.",
                Passive(),
                Ability(
                    "ong_ke_play",
                    AbilityTrigger.Play,
                    new[]
                    {
                        Effect(EffectKind.Kill, EffectTarget.PrimarySelection),
                        Effect(EffectKind.Draw, EffectTarget.SourceOwner, amount: 2),
                    },
                    new[]
                    {
                        Target(
                            AbilityTargetSlot.Primary,
                            TargetRelation.Ally,
                            AbilityTargetZone.ActiveRoster
                        ),
                    }
                )
            );

            Configure(
                "MaLai",
                "Ma Lai",
                5,
                5,
                3,
                UnitKeyword.Ephemeral | UnitKeyword.Lifesteal,
                "Phù du. Hút máu.",
                Passive()
            );

            Configure(
                "MaCo",
                "Ma Cơ",
                3,
                2,
                3,
                UnitKeyword.None,
                "Summon: tạo trong DrawHand một bản sao ngẫu nhiên "
                    + "của đồng minh đã chết trong trận này.",
                Passive(),
                Ability(
                    "ma_co_summon",
                    AbilityTrigger.Summon,
                    new[] { Effect(EffectKind.Copy, EffectTarget.RandomDeadAlly) }
                )
            );

            Configure(
                "VongNhi",
                "Vong Nhi",
                3,
                0,
                3,
                UnitKeyword.None,
                "Khi một đồng minh khác chết, gây 1 sát thương " + "lên Nexus địch.",
                Passive(),
                Ability(
                    "vong_nhi_ally_death",
                    AbilityTrigger.AllyDeath,
                    new[]
                    {
                        Effect(EffectKind.Damage, EffectTarget.EnemyNexus, amount: 1),
                    }
                )
            );

            Configure(
                "ThanTrung",
                "Thần Trùng",
                3,
                5,
                6,
                UnitKeyword.Fearsome,
                "Đáng sợ. Play: chọn một đồng minh và một kẻ địch; "
                    + "sau khi cả hai target hợp lệ, giết cả hai.",
                Passive(),
                Ability(
                    "than_trung_play",
                    AbilityTrigger.Play,
                    new[]
                    {
                        Effect(EffectKind.Kill, EffectTarget.PrimarySelection),
                        Effect(EffectKind.Kill, EffectTarget.SecondarySelection),
                    },
                    new[]
                    {
                        Target(
                            AbilityTargetSlot.Primary,
                            TargetRelation.Ally,
                            AbilityTargetZone.ActiveRoster
                        ),
                        Target(
                            AbilityTargetSlot.Secondary,
                            TargetRelation.Enemy,
                            AbilityTargetZone.ActiveRoster
                        ),
                    }
                )
            );
            SetMaximumCopiesPerDeck("AmBinh", 1);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Configured 19 Unit cards. Card 7 was not created.");
        }

        private static void Configure(
            string assetId,
            string displayName,
            int health,
            int damage,
            int energy,
            UnitKeyword keywords,
            string rulesText,
            CardPassiveData passiveRules,
            params CardAbilityData[] abilities
        )
        {
            string path = CardFolder + assetId + ".asset";

            Card card = AssetDatabase.LoadAssetAtPath<Card>(path);

            if (card == null)
            {
                throw new InvalidOperationException($"Card asset was not found: {path}");
            }

            Undo.RecordObject(card, "Configure Unit Card Content");

            card.cardName = displayName;
            card.health = health;
            card.damage = damage;
            card.energy = energy;
            card.maximumCopiesPerDeck = 0;
            card.keywords = keywords;
            card.rulesText = rulesText;

            card.passiveRules = passiveRules ?? new CardPassiveData();

            card.abilities =
                abilities == null
                    ? new List<CardAbilityData>()
                    : new List<CardAbilityData>(abilities);

            EditorUtility.SetDirty(card);
        }

        private static void SetMaximumCopiesPerDeck(string assetId, int maximumCopies)
        {
            if (maximumCopies < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCopies));
            }

            string path = CardFolder + assetId + ".asset";

            Card card = AssetDatabase.LoadAssetAtPath<Card>(path);

            if (card == null)
            {
                throw new InvalidOperationException($"Card asset was not found: {path}");
            }

            Undo.RecordObject(card, "Configure Card Deck Limit");

            card.maximumCopiesPerDeck = maximumCopies;

            EditorUtility.SetDirty(card);
        }

        private static CardAbilityData Ability(
            string id,
            AbilityTrigger trigger,
            CardEffectData[] effects,
            AbilityTargetRequirementData[] requiredTargets = null,
            AbilityCondition condition = AbilityCondition.None,
            bool oncePerRound = false,
            bool oncePerMatch = false,
            MissingTargetPolicy missingTargetPolicy = MissingTargetPolicy.RejectPlay
        )
        {
            return new CardAbilityData
            {
                abilityId = id,
                trigger = trigger,
                condition = condition,
                oncePerRound = oncePerRound,
                oncePerMatch = oncePerMatch,
                missingTargetPolicy = missingTargetPolicy,

                requiredTargets =
                    requiredTargets == null
                        ? new List<AbilityTargetRequirementData>()
                        : new List<AbilityTargetRequirementData>(requiredTargets),

                effects =
                    effects == null
                        ? new List<CardEffectData>()
                        : new List<CardEffectData>(effects),
            };
        }

        private static CardEffectData Effect(
            EffectKind kind,
            EffectTarget target,
            int amount = 0,
            int secondaryAmount = 0,
            int count = 1,
            EffectDuration duration = EffectDuration.Permanent,
            string cardDefinitionId = null
        )
        {
            return new CardEffectData
            {
                kind = kind,
                target = target,
                amount = amount,
                secondaryAmount = secondaryAmount,
                count = Math.Max(1, count),
                duration = duration,
                cardDefinitionId = cardDefinitionId ?? string.Empty,
            };
        }

        private static AbilityTargetRequirementData Target(
            AbilityTargetSlot slot,
            TargetRelation relation,
            AbilityTargetZone zones,
            int count = 1,
            bool excludeSource = true
        )
        {
            return new AbilityTargetRequirementData
            {
                slot = slot,
                relation = relation,
                zones = zones,
                count = Math.Max(1, count),
                excludeSource = excludeSource,
            };
        }

        private static CardPassiveData Passive(
            int costReductionPerAlliedDeathThisGame = 0,
            bool revivesAtRoundStart = false,
            int powerAndHealthPerDeath = 0,
            bool joinsAttackFromGraveyard = false,
            bool requiresEphemeralAttacker = false
        )
        {
            return new CardPassiveData
            {
                costReductionPerAlliedDeathThisGame = costReductionPerAlliedDeathThisGame,

                revivesAtRoundStart = revivesAtRoundStart,

                powerAndHealthPerDeath = powerAndHealthPerDeath,

                joinsAttackFromGraveyard = joinsAttackFromGraveyard,

                requiresEphemeralAttacker = requiresEphemeralAttacker,
            };
        }

        private static void ValidateAssetsExist()
        {
            foreach (string cardId in CardIds)
            {
                string path = CardFolder + cardId + ".asset";

                if (AssetDatabase.LoadAssetAtPath<Card>(path) == null)
                {
                    throw new InvalidOperationException($"Missing card asset: {path}");
                }
            }
        }
    }
}

#endif
