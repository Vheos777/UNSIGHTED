﻿namespace Vheos.Mods.UNSIGHTED
{
    using System;
    using System.Linq;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using HarmonyLib;
    using Tools.ModdingCore;
    using Tools.Extensions.Math;
    using Tools.Extensions.General;
    using Tools.Extensions.Collections;
    using Vheos.Tools.UtilityN;

    public class ChipsCogs : AMod
    {
        // Section
        override protected string SectionOverride
        => Sections.BALANCE;
        override protected string ModName
        => "Chips & Cogs";
        override protected string Description =>
            "Mods related to the chip and cog systems" +
            "\n\nExamples:" +
            "\n• Change starting chip slots and unlock costs" +
            "\n• Change number of cog slots" +
            "\n• Limit number of active cog types";

        // Settings
        static private ModSetting<int> _startingChipSlots;
        static private ModSetting<int> _linearChipSlotCosts;
        static private ModSetting<int> _cogSlots;
        static private ModSetting<int> _maxActiveCogTypes;
        static private ModSetting<Vector3> _contextualOperations;
        static private ModSetting<Vector3> _otherOperations;
        static private ModSetting<int> _defenseCogReduction;
        static private Dictionary<CogType, CogSettings> _settingsByCogType;
        override protected void Initialize()
        {
            _startingChipSlots = CreateSetting(nameof(_startingChipSlots), 3, IntRange(0, 14));
            _linearChipSlotCosts = CreateSetting(nameof(_linearChipSlotCosts), -1, IntRange(-1, 2000));
            _cogSlots = CreateSetting(nameof(_cogSlots), 4, IntRange(0, 6));
            _maxActiveCogTypes = CreateSetting(nameof(_maxActiveCogTypes), 4, IntRange(1, 6));

            _contextualOperations = CreateSetting(nameof(_contextualOperations), new Vector3(1, 3, 4));
            _otherOperations = CreateSetting(nameof(_otherOperations), new Vector3(5, 6, 2));
            _defenseCogReduction = CreateSetting(nameof(_defenseCogReduction), 20, IntRange(1, 20));

            _settingsByCogType = new Dictionary<CogType, CogSettings>();
            foreach (var cogType in Utility.GetEnumValues<CogType>())
                _settingsByCogType[cogType] = new CogSettings(this, cogType);

            // Events
            _linearChipSlotCosts.AddEvent(() => TrySetLinearChipSlotCosts(PseudoSingleton<LevelDatabase>.instance));
            _contextualOperations.AddEvent(SortAndCacheDamageTakenOperations);
            _otherOperations.AddEvent(SortAndCacheDamageTakenOperations);
        }
        override protected void SetFormatting()
        {
            _startingChipSlots.Format("Starting chip slots");
            _startingChipSlots.Description =
                "How many free chip slots you start the game with";
            _linearChipSlotCosts.Format("Linear chip slot costs");
            _linearChipSlotCosts.Description =
                "How much more each consecutive chip slot costs" +
                "\nThe first non-starting slot costs exactly this value, the second one costs twice as much, the third one three times as much, and so on" +
                "\nSet to 0 to make all slots free to unlock" +
                "\nSet to -1 to use the original costs";
            _cogSlots.Format("Cog slots");
            _cogSlots.Description =
                "How many cog slots you have";
            _maxActiveCogTypes.Format("Max active cog types");
            _maxActiveCogTypes.Description =
                "How many cogs of different types you can have activated at the same time";

            CreateHeader("Damage taken formula").Description =
                "Allows you to customize the order of operations in the formula for damage taken" +
                "\nOperations will be applied in ascending order (from lowest to highest)" +
                "\nSet the order of an operation to -1 to disable it entirely" +
                "\n\nThe original in-game order is:" +
                "\n1. Defense Chips" +
                "\n2. Clamp to 1 damage" +
                "\n3. Aggressive/Glitch Chips" +
                "\n4. Defense Cog" +
                "\n5. Combat Assit" +
                "\n6. Robot Apocalypse";
            using (Indent)
            {
                _contextualOperations.Format("chips/cogs operations");
                _contextualOperations.Description =
                    "X - Defense Chips' damage reduction" +
                    "\nY - Aggressive/Glitch Chips' extra damage taken" +
                    "\nZ - Defense Cog's damage reduction";
                _otherOperations.Format("other operations");
                _otherOperations.Description =
                    "X - Combat Assist damage taken reduction" +
                    "Y - Robot Apocalypse extra damage taken" +
                    "\nZ - clamp damage taken to a minimum of 1";
            }
            _defenseCogReduction.Format("Defense Cog damage reduction");
            _defenseCogReduction.Description =
                "How much damage is negated when you have the Defense Cog equipped" +
                "\n\nUnit: points of damage";

            CreateHeader("Cogs editor").Description =
                "Allows you to change the duration, price and color of each cog type";
            using (Indent)
                foreach (var settings in _settingsByCogType)
                    settings.Value.Format();
        }
        override protected void LoadPreset(string presetName)
        {
            switch (presetName)
            {
                case nameof(SettingsPreset.Vheos_CoopRebalance):
                    ForceApply();
                    _startingChipSlots.Value = 0;
                    _linearChipSlotCosts.Value = 750;
                    _cogSlots.Value = 6;
                    _maxActiveCogTypes.Value = 1;
                    break;
            }
        }

        // Privates
        #region CogDefaults
        static private readonly Dictionary<CogType, (int Duration, int MinDuration, int MaxDuration, int Price, Color Color)> DEFAULTS_BY_COG_TYPE
            = new Dictionary<CogType, (int, int, int, int, Color)>
            {
                [CogType.Attack] = (20, 1, 100, 350, new Color(1f, 0.2426f, 0.2426f, 1f)),
                [CogType.Stamina] = (120, 1, 600, 400, new Color(0.8382f, 0.4811f, 0.1541f, 1f)),
                [CogType.Reload] = (3, 1, 10, 450, new Color(0.9269f, 1f, 0.2426f, 1f)),
                [CogType.Speed] = (120, 1, 600, 350, new Color(0.6838f, 0.2816f, 0.5729f, 1f)),
                [CogType.Defense] = (3, 1, 10, 400, new Color(0.2426f, 0.4046f, 1f, 1f)),
                [CogType.Revive] = (1, 1, 10, 1750, new Color(1f, 1f, 1f, 1f)),
            };
        #endregion
        private delegate void DamageTakenOperation(ref int damageTaken, int playerID);
        static private void TrySetLinearChipSlotCosts(LevelDatabase levelDatabase)
        {
            if (levelDatabase == null
            || _linearChipSlotCosts < 0)
                return;

            for (int i = 0; i <= 15; i++)
                levelDatabase.levelUpCost[i] = i.Sub(_startingChipSlots).Add(1).Mul(_linearChipSlotCosts).ClampMin(0);
        }
        static private Vector2 SetAnchoredPosition(Component component, float placementX, float placementY)
        => component.GetComponent<RectTransform>().anchoredPosition
            = new Vector2(placementX.MapClamped(-1, +1, -110, +110), placementY.MapClamped(-1, +1, -10, +60));
        static private DamageTakenOperation _cachedSortedOperations;
        static private void SortAndCacheDamageTakenOperations()
        {
            // local functions
            void ApplyDefenseChip(ref int damageTaken, int playerID)
            {
                damageTaken -= PseudoSingleton<Helpers>.instance.NumberOfChipsEquipped("DefenseChip", playerID);
                damageTaken.SetClampMin(0);
            }
            void ApplyNegativeChips(ref int damageTaken, int playerID)
            {
                var helpers = PseudoSingleton<Helpers>.instance;
                damageTaken += helpers.NumberOfChipsEquipped("OffenseChip", playerID)
                            + helpers.NumberOfChipsEquipped("GlitchChip", playerID);
            }
            void ApplyDefenseCog(ref int damageTaken, int playerID)
            {
                if (!PseudoSingleton<Helpers>.instance.PlayerHaveBuff(PlayerBuffTypes.Defense, playerID))
                    return;

                damageTaken -= _defenseCogReduction;
                damageTaken.SetClampMin(0);
                PseudoSingleton<BuffsInterfaceController>.instance.ReduceBuff(playerID, PlayerBuffTypes.Defense, 1);
            }
            void ClampToOne(ref int damageTaken, int playerID)
            => damageTaken.SetClampMin(1);
            void ApplyCombatAssist(ref int damageTaken, int playerID)
            {
                if (!PseudoSingleton<Helpers>.instance.GetPlayerData().combatAssist)
                    return;

                damageTaken--;
                damageTaken.SetClampMin(0);
            }
            void ApplyRobotApocalypse(ref int damageTaken, int playerID)
            {
                if (PseudoSingleton<Helpers>.instance.GetPlayerData().difficulty != Difficulty.Hard)
                    return;

                damageTaken++;
            }

            // sort
            List<(DamageTakenOperation Operation, float Order)> operationOrderPairs = new List<(DamageTakenOperation, float)>
            {
                (ApplyDefenseChip, _contextualOperations.Value.x),
                (ApplyNegativeChips, _contextualOperations.Value.y),
                (ApplyDefenseCog, _contextualOperations.Value.z),
                (ApplyCombatAssist, _otherOperations.Value.x),
                (ApplyRobotApocalypse, _otherOperations.Value.y),
                (ClampToOne, _otherOperations.Value.z),
            };
            operationOrderPairs.Sort((a, b) => a.Order.CompareTo(b.Order));

            // cache
            _cachedSortedOperations = null;
            foreach (var (Operation, Order) in operationOrderPairs)
                if (Order >= 0)
                    _cachedSortedOperations += Operation;
        }

        // Defines
        private enum CogType
        {
            Attack,
            Stamina,
            Reload,
            Speed,
            Defense,
            Revive,
        }

        #region CogSettings
        private class CogSettings
        {
            // Settings
            private readonly ModSetting<bool> Toggle;
            private readonly ModSetting<int> Duration;
            private readonly ModSetting<int> Price;
            private readonly ModSetting<Color> Color;
            internal CogSettings(ChipsCogs mod, CogType cogType)
            {
                _mod = mod;
                _cogType = cogType;

                string keyPrefix = $"{_cogType}_";
                Toggle = _mod.CreateSetting(keyPrefix + nameof(Toggle), false);
                Duration = _mod.CreateSetting(keyPrefix + nameof(Duration), DEFAULTS_BY_COG_TYPE[_cogType].Duration,
                    _mod.IntRange(DEFAULTS_BY_COG_TYPE[_cogType].MinDuration, DEFAULTS_BY_COG_TYPE[_cogType].MaxDuration));
                Price = _mod.CreateSetting(keyPrefix + nameof(Price), DEFAULTS_BY_COG_TYPE[_cogType].Price, _mod.IntRange(0, 10000));
                Color = _mod.CreateSetting(keyPrefix + nameof(Color), DEFAULTS_BY_COG_TYPE[_cogType].Color);

                // Events
                Toggle.AddEventSilently(TryApplyAll);
                Duration.AddEventSilently(ApplyDuration);
                Price.AddEventSilently(ApplyPrice);
                Color.AddEventSilently(ApplyColor);
            }
            internal void Format()
            {
                Toggle.Format(_cogType.ToString());
                using (Indent)
                {
                    Duration.Format("Duration", Toggle);
                    Price.Format("Price", Toggle);
                    Color.Format("Color", Toggle);
                }
            }
            internal string Description
            {
                get => Toggle.Description;
                set => Toggle.Description = value;
            }

            // Publics
            internal void FindCogPrefab()
            => PseudoSingleton<Lists>.instance.cogsDatabase.cogList.TryFind(t => t.myBuff.buffType == GetInternalCogType(_cogType), out _cogPrefab);
            internal void TryApplyAll()
            {
                if (!Toggle
                || _cogPrefab == null)
                    return;

                ApplyDuration();
                ApplyPrice();
                ApplyColor();
            }

            // Privates
            private readonly ChipsCogs _mod;
            private readonly CogType _cogType;
            private CogObject _cogPrefab;
            private PlayerBuffTypes GetInternalCogType(CogType cogType)
            {
                switch (cogType)
                {
                    case CogType.Attack: return PlayerBuffTypes.Attack;
                    case CogType.Stamina: return PlayerBuffTypes.Stamina;
                    case CogType.Reload: return PlayerBuffTypes.Reload;
                    case CogType.Speed: return PlayerBuffTypes.Speed;
                    case CogType.Defense: return PlayerBuffTypes.Defense;
                    case CogType.Revive: return PlayerBuffTypes.Revive;
                    default: return PlayerBuffTypes.None;
                }
            }
            private void ApplyDuration()
            {
                var buff = _cogPrefab.myBuff;
                if (buff.usesSeconds)
                    buff.remainingSeconds = Duration;
                else
                {
                    buff.remainingUses = Duration;
                    buff.totalUses = Duration;
                }
            }
            private void ApplyPrice()
            => _cogPrefab.itemValue = Price;
            private void ApplyColor()
            => _cogPrefab.cogColor = Color;
        }
        #endregion


        // Hooks
#pragma warning disable IDE0051, IDE0060, IDE1006

        // Chips
        [HarmonyPatch(typeof(GlobalGameData), nameof(GlobalGameData.CreateDefaultDataSlot)), HarmonyPostfix]
        static private void GlobalGameData_CreateDefaultDataSlot_Post(GlobalGameData __instance, int slotNumber)
        => __instance.currentData.playerDataSlots[slotNumber].chipSlots = _startingChipSlots;

        [HarmonyPatch(typeof(LevelDatabase), nameof(LevelDatabase.OnEnable)), HarmonyPostfix]
        static private void LevelDatabase_OnEnable_Post(LevelDatabase __instance)
        => TrySetLinearChipSlotCosts(__instance);

        // Cogs
        [HarmonyPatch(typeof(Helpers), nameof(Helpers.GetMaxCogs)), HarmonyPrefix]
        static private bool Helpers_GetMaxCogs_Pre(Helpers __instance, ref int __result)
        {
            __result = _cogSlots;
            return false;
        }

        [HarmonyPatch(typeof(CogButton), nameof(CogButton.OnClick)), HarmonyPrefix]
        static private void CogButton_OnClick_Pre(CogButton __instance)
        {
            if (_maxActiveCogTypes >= _cogSlots
            || !__instance.buttonActive
            || !__instance.currentBuff.TryNonNull(out var buff)
            || buff.active
            || __instance.destroyButton)
                return;

            List<PlayerBuffs> buffsList = TButtonNavigation.myPlayer == 0
                                       ? PseudoSingleton<Helpers>.instance.GetPlayerData().p1Buffs
                                       : PseudoSingleton<Helpers>.instance.GetPlayerData().p2Buffs;
            if (buffsList.Any(t => t.active && t.buffType == buff.buffType))
                return;

            var activeCogTypes = new HashSet<PlayerBuffTypes> { buff.buffType };
            foreach (var otherButton in __instance.myCogsPopup.cogButtons)
                if (otherButton != __instance
                && otherButton.buttonActive
                && otherButton.currentBuff.TryNonNull(out var otherBuff)
                && otherBuff.active
                && !activeCogTypes.Contains(otherBuff.buffType))
                    if (activeCogTypes.Count >= _maxActiveCogTypes)
                        otherButton.OnClick();
                    else
                        activeCogTypes.Add(otherBuff.buffType);
        }

        [HarmonyPatch(typeof(CogsPopup), nameof(CogsPopup.OnEnable)), HarmonyPrefix]
        static private bool CogsPopup_OnEnable_Pre(CogsPopup __instance)
        {
            if (!__instance.alreadyStarted)
            {
                int maxCogs = PseudoSingleton<Helpers>.instance.GetMaxCogs();
                var unusedSlots = new HashSet<int>();
                switch (maxCogs)
                {
                    case 0:
                        unusedSlots.Add(0, 1, 2, 3, 4, 5);
                        break;
                    case 1:
                        SetAnchoredPosition(__instance.cogButtons[4], 0, 0);
                        unusedSlots.Add(0, 1, 2, 3, 5);
                        break;
                    case 2:
                        SetAnchoredPosition(__instance.cogButtons[4], 0, -1);
                        SetAnchoredPosition(__instance.cogButtons[1], 0, +1);
                        unusedSlots.Add(0, 2, 3, 5);
                        break;
                    case 3:
                        SetAnchoredPosition(__instance.cogButtons[3], -1 / 2f, -1);
                        SetAnchoredPosition(__instance.cogButtons[5], +1 / 2f, -1);
                        SetAnchoredPosition(__instance.cogButtons[1], 0, +1);
                        unusedSlots.Add(0, 2, 4);
                        break;
                    case 4:
                        SetAnchoredPosition(__instance.cogButtons[3], -1 / 2f, -1);
                        SetAnchoredPosition(__instance.cogButtons[5], +1 / 2f, -1);
                        SetAnchoredPosition(__instance.cogButtons[0], -1 / 2f, +1);
                        SetAnchoredPosition(__instance.cogButtons[2], +1 / 2f, +1);
                        unusedSlots.Add(1, 4);
                        break;
                    case 5:
                        SetAnchoredPosition(__instance.cogButtons[3], -1, -1);
                        SetAnchoredPosition(__instance.cogButtons[4], 0, -1);
                        SetAnchoredPosition(__instance.cogButtons[5], +1, -1);
                        SetAnchoredPosition(__instance.cogButtons[0], -1 / 2f, +1);
                        SetAnchoredPosition(__instance.cogButtons[2], +1 / 2f, +1);
                        unusedSlots.Add(1);
                        break;
                    case 6:
                        SetAnchoredPosition(__instance.cogButtons[3], -1, -1);
                        SetAnchoredPosition(__instance.cogButtons[4], 0, -1);
                        SetAnchoredPosition(__instance.cogButtons[5], +1, -1);
                        SetAnchoredPosition(__instance.cogButtons[0], -1, +1);
                        SetAnchoredPosition(__instance.cogButtons[1], 0, +1);
                        SetAnchoredPosition(__instance.cogButtons[2], +1, +1);
                        break;
                }

                foreach (var slot in unusedSlots)
                    __instance.cogButtons[slot].gameObject.SetActive(false);
                foreach (var slot in unusedSlots)
                    __instance.cogButtons.Remove(__instance.cogButtons[slot]);
            }

            if (__instance.alreadyStarted)
                for (int i = 0; i < __instance.cogButtons.Count; i++)
                    if (TButtonNavigation.myPlayer == 0 && __instance.cogButtons[i].buttonActive
                    && !PseudoSingleton<Helpers>.instance.GetPlayerData().p1Buffs.Contains(__instance.cogButtons[i].currentBuff)
                    || TButtonNavigation.myPlayer == 1 && __instance.cogButtons[i].buttonActive
                    && !PseudoSingleton<Helpers>.instance.GetPlayerData().p2Buffs.Contains(__instance.cogButtons[i].currentBuff))
                        __instance.cogButtons[i].ShowCogAsInactive();

            return false;
        }

        // Damage taken formula
        [HarmonyPatch(typeof(BasicCharacterCollider), nameof(BasicCharacterCollider.TouchedObject)), HarmonyPrefix]
        static private void BasicCharacterCollider_TouchedObject_Pre(BasicCharacterCollider __instance, ref (bool CombatAssist, Difficulty Difficulty) __state)
        {
            var playerData = PseudoSingleton<Helpers>.instance.GetPlayerData();
            __state = (playerData.combatAssist, playerData.difficulty);
        }

        [HarmonyPatch(typeof(BasicCharacterCollider), nameof(BasicCharacterCollider.TouchedObject)), HarmonyPostfix]
        static private void BasicCharacterCollider_TouchedObject_Post(BasicCharacterCollider __instance, ref (bool CombatAssist, Difficulty Difficulty) __state)
        {
            var playerData = PseudoSingleton<Helpers>.instance.GetPlayerData();
            playerData.combatAssist = __state.CombatAssist;
            playerData.difficulty = __state.Difficulty;
        }

        [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.ApplyChipAndCogsToDamage)), HarmonyPrefix]
        static private bool PlayerInfo_ApplyChipAndCogsToDamage_Pre(PlayerInfo __instance, ref int __result, ref int damage)
        {
            // Cache 
            var helpers = PseudoSingleton<Helpers>.instance;
            var playerData = PseudoSingleton<Helpers>.instance.GetPlayerData();

            // execute
            __result = damage;

            if (InvincibilityCheat.invincibility
            || helpers.GetPlayerData().invencibilityAssist
            || ParryChallenge.IsAnyGymMinigameActive())
            {
                __result = 0;
                if (ParryChallenge.IsParryChallengeActive())
                    PseudoSingleton<GymMinigame>.instance.PlayerGotHit();
            }
            else
                _cachedSortedOperations?.Invoke(ref __result, __instance.playerNum);

            playerData.combatAssist = false;
            playerData.difficulty = Difficulty.Medium;
            return false;
        }

        // Find cog prefab
        [HarmonyPatch(typeof(Lists), nameof(Lists.Start)), HarmonyPostfix]
        static private void Lists_Start_Post(Lists __instance)
        {
            foreach (var cogType in Utility.GetEnumValues<CogType>())
            {
                _settingsByCogType[cogType].FindCogPrefab();
                _settingsByCogType[cogType].TryApplyAll();
            }
        }
    }
}