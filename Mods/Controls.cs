﻿namespace Vheos.Mods.UNSIGHTED
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using HarmonyLib;
    using Tools.ModdingCore;
    using Tools.Extensions.UnityObjects;
    using Tools.Extensions.Math;
    using Vheos.Tools.Extensions.General;
    using Vheos.Tools.Extensions.Collections;
    using UnityEngine.UI;

    public class Controls : AMod, IDelayedInit
    {
        // Settings
        static private ModSetting<string> _undbindButton;
        static private ModSetting<CustomControls.ConflictResolution> _bindigsConflictResolution;
        static private Dictionary<int, LoadoutSettings> _loadoutSettingsByPlayerID;
        override protected void Initialize()
        {
            _loadoutSettingsByPlayerID = new Dictionary<int, LoadoutSettings>();
            for (int playerID = 0; playerID < 2; playerID++)
                _loadoutSettingsByPlayerID[playerID] = new LoadoutSettings(this, playerID);
            CustomControls.UpdateButtonsTable();

            _undbindButton = CreateSetting(nameof(_undbindButton), "");
            CustomControls.UnbindButton.Set(() => _undbindButton.ToKeyCode());

            _bindigsConflictResolution = CreateSetting(nameof(_bindigsConflictResolution), CustomControls.ConflictResolution.Swap);
            CustomControls.BindingsConflictResolution.Set(() => _bindigsConflictResolution);
        }
        override protected void SetFormatting()
        {
            CreateHeader("Loadouts").Description =
                "Allows you quickly to switch between pre-defined sets of weapons" +
                "\nHotkeys can be configured in the in-game \"Controls\" menu" +
                "\n(requires game restart to take effect)";
            using (Indent)
                foreach (var settings in _loadoutSettingsByPlayerID)
                    settings.Value.Format();
            _undbindButton.Format("\"Unbind\" button");
            _undbindButton.Description =
                "Press this when assigning a new button in the controls menu to unbind the button";
            _bindigsConflictResolution.Format("Bindings conflict resolution");
            _bindigsConflictResolution.Description =
                "What should happen when you try to assign a button that's already bound:" +
                $"\n{CustomControls.ConflictResolution.Swap} - the two conflicting buttons will swap places" +
                $"\n{CustomControls.ConflictResolution.Unbind} - the other button binding will be removed" +
                $"\n{CustomControls.ConflictResolution.Duplicate} - allow for one button to be bound to many actions";
        }
        override protected void LoadPreset(string presetName)
        {
            switch (presetName)
            {
                case nameof(Preset.Vheos_HardMode):
                    ForceApply();
                    break;
            }
        }
        override protected string Description =>
            "";

        // Defines
        #region LoadoutSettings
        private class LoadoutSettings : PerPlayerSettings<Controls>
        {
            // Settings
            private readonly ModSetting<int> _count;
            private readonly ModSetting<string> _next;
            private readonly Loadout[] _loadouts;
            internal LoadoutSettings(Controls mod, int playerID) : base(mod, playerID)
            {
                _count = _mod.CreateSetting(PlayerPrefix + "Count", 1, _mod.IntRange(1, 4));
                _cachedCount = _count;
                if (!IsEnabled)
                    return;

                _next = CustomControls.AddControlsButton(playerID, $"Next Loadout");
                _loadouts = new Loadout[_count];
                for (int i = 0; i < _loadouts.Length; i++)
                {
                    _loadouts[i] = new Loadout(i);
                    _loadouts[i].Button = CustomControls.AddControlsButton(playerID, $"Loadout {i + 1}");
                    _loadouts[i].Slots[0] = _mod.CreateSetting(PlayerPrefix + _loadouts[i].Prefix + "Slot1", NOTHING_WEAPON_NAME);
                    _loadouts[i].Slots[1] = _mod.CreateSetting(PlayerPrefix + _loadouts[i].Prefix + "Slot2", NOTHING_WEAPON_NAME);
                }
            }
            public override void Format()
            {
                _count.Format($"Player {_playerID + 1}");
                _count.Description =
                    "How many different weapon loadouts you'd like to use" +
                    "\nSet to 1 to disable this feature" +
                    "\n(requires game restart to take effect)";
            }

            // Publics
            internal void DetectInput()
            {
                if (!IsEnabled)
                    return;

                if (ButtonSystem.GetKeyDown(_next.ToKeyCode()))
                    TrySwitchTo(+1);
                else
                    foreach (var loadout in _loadouts)
                        if (ButtonSystem.GetKeyDown(loadout.Button.ToKeyCode()))
                        {
                            SwitchTo(loadout);
                            return;
                        }
            }
            internal void UpdateCurrentSlot(int slotID, string weapon)
            {
                if (!IsEnabled
                || !VerifyCurrentLoadout())
                    return;

                _currentLoadout.Slots[slotID].Value = weapon;
            }
            internal bool IsEnabled
            => _cachedCount > 1;

            // Privates
            private const string NOTHING_WEAPON_NAME = "Null";
            private Loadout _currentLoadout;
            private int _cachedCount;
            private void TrySwitchTo(int offset)
            {
                if (!VerifyCurrentLoadout())
                    return;

                int targetID = _currentLoadout.ID.Add(offset).PosMod(_count);
                SwitchTo(_loadouts[targetID]);
            }
            private void SwitchTo(Loadout loadout)
            {
                if (_currentLoadout == loadout)
                    return;

                _currentLoadout = loadout;
                for (int i = 1; i >= 0; i--)
                {
                    ModSetting<string> targetWeapon = _currentLoadout.Slots[i];
                    if (PseudoSingleton<Helpers>.instance.PlayerHaveItem(targetWeapon) <= 0)
                        targetWeapon.Value = NOTHING_WEAPON_NAME;
                    PseudoSingleton<Helpers>.instance.EquipWeapon(targetWeapon, _playerID, i);
                }

                if (PlayerEquipmentAndLevelScreen.instance != null)
                    PlayerEquipmentAndLevelScreen.instance.OnEnable();

                AudioController.Play(PseudoSingleton<GlobalGameManager>.instance.gameSounds.playerDeathSheen, 1f, 1f);
            }
            private bool VerifyCurrentLoadout()
            {
                // Current
                if (_currentLoadout != null)
                    return true;

                // Find matching
                var globals = PseudoSingleton<GlobalGameData>.instance;
                string[] playerSlots = globals.currentData.playerDataSlots[globals.loadedSlot].playersEquipData[_playerID].weapons;
                foreach (var loadout in _loadouts)
                    if (loadout.HasExactSlots(playerSlots))
                    {
                        _currentLoadout = loadout;
                        return true;
                    }

                // Get first
                if (_loadouts.IsNotEmpty())
                {
                    _currentLoadout = _loadouts[0];
                    return true;
                }

                // Fail
                return false;
            }

            // Defines
            private class Loadout
            {
                // Publics
                public int ID;
                public ModSetting<string> Button;
                public ModSetting<string>[] Slots;
                public string Prefix
                => $"Loadout{ID + 1}_";
                public bool HasExactSlots(string[] slots)
                {
                    for (int i = 0; i < 2; i++)
                        if (Slots[i] != slots[i])
                            return false;
                    return true;
                }

                // Initializers
                public Loadout(int id)
                {
                    ID = id;
                    Slots = new ModSetting<string>[2];
                }
            }
        }
        #endregion

        // Hooks
#pragma warning disable IDE0051, IDE0060, IDE1006

        // Loadouts
        [HarmonyPatch(typeof(BasicCharacterController), nameof(BasicCharacterController.GetInputs)), HarmonyPostfix]
        static private void BasicCharacterController_GetInputs_Post(BasicCharacterController __instance)
        => _loadoutSettingsByPlayerID[__instance.myInfo.playerNum].DetectInput();

        [HarmonyPatch(typeof(PauseMenuPopup), nameof(PauseMenuPopup.Update)), HarmonyPostfix]
        static private void PauseMenuPopup_Update_Post(PauseMenuPopup __instance)
        => _loadoutSettingsByPlayerID[TButtonNavigation.myPlayer].DetectInput();

        [HarmonyPatch(typeof(Helpers), nameof(Helpers.EquipWeapon)), HarmonyPrefix]
        static private void Helpers_EquipWeapon_Pre(Helpers __instance, string weaponName, int playerNum, int weaponSlot)
        => _loadoutSettingsByPlayerID[playerNum].UpdateCurrentSlot(weaponSlot, weaponName);
    }
}