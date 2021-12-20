﻿namespace Vheos.Mods.UNSIGHTED
{
    using System.Linq;
    using System.Collections.Generic;
    using UnityEngine;
    using HarmonyLib;
    using Tools.Extensions.General;
    using Tools.Extensions.UnityObjects;
    using Tools.ModdingCore;
    using Tools.Extensions.Math;
    using System;
    using Vheos.Tools.Extensions.Reflection;
    using Vheos.Tools.Extensions.Collections;
    using UnityEngine.UI;

    static internal class CustomControls
    {

        // Publics
        static internal void Initialize()
        {
            _settingsByButtonGUID = new Dictionary<string, ModSetting<string>[]>();
            Harmony.CreateAndPatchAll(typeof(CustomControls));
        }
        static internal ModSetting<string> AddControlsButton(int playerID, string name)
        {
            // setting
            string settingGUID = GetSettingGUID(playerID, name);
            var setting = new ModSetting<string>("", settingGUID, KeyCode.None.ToString());
            setting.IsVisible = false;

            string buttonGUID = GetButtonGUID(name);
            if (!_settingsByButtonGUID.ContainsKey(buttonGUID))
            {
                _settingsByButtonGUID.Add(buttonGUID, new ModSetting<string>[2]);

                // initialize button
                var newButton = GameObject.Instantiate(_buttonPrefab).GetComponent<ChangeInputButton>();
                newButton.BecomeSiblingOf(_buttonPrefab);
                newButton.inputName = buttonGUID;
                newButton.currentKey = setting.ToKeyCode();
                newButton.buttonName.originalText = setting.Value.ToUpper();

                var actionName = newButton.GetComponentInChildren<FText>();
                actionName.originalText = name;
                actionName.text = name;
                actionName.GetComponent<UITranslateText>().enabled = false;
            }

            _settingsByButtonGUID[buttonGUID][playerID] = setting;
            return setting;
        }
        static internal void UpdateButtonsTable()
        {
            // table size and position
            RectTransform tableRect = _buttonPrefab.transform.parent as RectTransform;
            tableRect.sizeDelta = TABLE_SIZE;
            tableRect.anchoredPosition = new Vector2(0, -32);

            // buttons size and position
            Vector2Int tableCounts = GetTableCounts(tableRect.childCount);
            var buttonsTable = tableCounts.ToArray2D<GameObject>();

            Vector3 textScale = Vector3.one;
            if (tableCounts.x >= COMPACT_TEXT_SCALE_THRESHOLD.x)
                textScale.x = COMPACT_TEXT_SCALE.x;
            if (tableCounts.y >= COMPACT_TEXT_SCALE_THRESHOLD.y)
                textScale.y = COMPACT_TEXT_SCALE.y;

            for (int i = 0; i < tableRect.childCount; i++)
            {
                var buttonRect = tableRect.GetChild(i) as RectTransform;
                int ix = i / tableCounts.y;
                int iy = i % tableCounts.y;
                buttonsTable[ix, iy] = buttonRect.gameObject;

                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0, 1);
                Vector2 buttonTotalSize = tableRect.sizeDelta / tableCounts;
                buttonRect.anchoredPosition = buttonTotalSize * new Vector2(ix + 0.5f, -(iy + 0.5f));
                buttonRect.sizeDelta = buttonTotalSize - 2 * BUTTON_PADDING;

                foreach (var text in buttonRect.GetComponentsInChildren<FText>())
                    text.transform.localScale = text.originalScale = textScale;
            }

            // navigation
            for (int ix = 0; ix < tableCounts.x; ix++)
                for (int iy = 0; iy < tableCounts.y; iy++)
                    if (buttonsTable[ix, iy].TryNonNull(out var button)
                    && button.TryGetComponent(out TButtonNavigation buttonNavigation))
                    {
                        buttonNavigation.onUp = buttonsTable[ix, iy.Add(-1).PosMod(tableCounts.y)];
                        buttonNavigation.onDown = buttonsTable[ix, iy.Add(+1).PosMod(tableCounts.y)];
                        buttonNavigation.onLeft = buttonsTable[ix.Add(-1).PosMod(tableCounts.x), iy];
                        buttonNavigation.onRight = buttonsTable[ix.Add(+1).PosMod(tableCounts.x), iy];
                    }

            // controls manager
            _controlsManager.inputButtons = _controlsManager.GetAllComponentsInHierarchy<ChangeInputButton>(3).GetGameObjects().ToArray();
        }
        static internal bool IsFullyInitialized
        { get; private set; }
        static internal Getter<KeyCode> UnbindButton
        { get; } = new Getter<KeyCode>();
        static internal Getter<BindingConflictResolution> BindingsConflictResolution
        { get; } = new Getter<BindingConflictResolution>();

        // Privates
        private const int MAX_COLUMNS = 3;
        private const int MIN_ROWS = 7;
        static private readonly Vector2 COMPACT_TEXT_SCALE = new Vector2(0.75f, 0.75f);
        static private readonly Vector2Int COMPACT_TEXT_SCALE_THRESHOLD = new Vector2Int(3, 10);
        static private readonly Color CUSTOM_BUTTON_COLOR = new Color(1 / 3f, 1 / 10f, 1 / 20f, 1 / 2f);
        static private readonly Vector2 BUTTON_PADDING = new Vector2(2, 1);
        static private readonly Vector2Int TABLE_SIZE = new Vector2Int(296, 126);
        #region VANILLA_BUTTON_NAMES
        static private readonly (string RefName, string InputName)[] VANILLA_BUTTON_NAMES = new[]
{
            ("interact", "interact"),
            ("aimLock", "aimlock"),
            ("guard", "guard"),
            ("weapon0Input", "sword"),
            ("dash", "dash"),
            ("heal", "heal"),
            ("weapon1Input", "gun"),
            ("pause", "pause"),
            ("reload", "reload"),
            ("map", "map"),
            ("run", "run"),
            ("up", "up"),
            ("left", "left"),
            ("right", "right"),
            ("down", "down"),
        };
        #endregion
        static private PlayerInputWindowsManager _controlsManager;
        static private GameObject _buttonPrefab;
        static private Dictionary<string, ModSetting<string>[]> _settingsByButtonGUID;
        static private bool TryFindButtonPrefab()
        {
            if (Resources.FindObjectsOfTypeAll<PlayerInputWindowsManager>().TryGetAny(out _controlsManager)
            && _controlsManager.inputButtons.TryGetAny(out _buttonPrefab))
                return true;

            return false;
        }
        static private Vector2Int GetTableCounts(int buttonsCount)
        {
            int sizeX = buttonsCount.Div(MIN_ROWS).RoundUp().ClampMax(MAX_COLUMNS);
            int sizeY = buttonsCount.Div(MAX_COLUMNS).RoundUp().ClampMin(MIN_ROWS);
            return new Vector2Int(sizeX, sizeY);
        }
        static private string GetSettingGUID(int playerID, string name)
        => $"{typeof(CustomControls).Name}_Player{playerID + 1}_{name}";
        static private string GetButtonGUID(string name)
        => $"{typeof(CustomControls).Name}_{name}";

        // Hooks
#pragma warning disable IDE0051, IDE0060, IDE1006

        [HarmonyPatch(typeof(TitleScreenScene), nameof(TitleScreenScene.Start)), HarmonyPostfix]
        static private void TitleScreenScene_Start_Post(TitleScreenScene __instance)
        {
            if (!TryFindButtonPrefab())
            {
                Log.Debug($"Failed to fully initialize {typeof(CustomControls).Name}!");
                return;
            }

            GameObject.DontDestroyOnLoad(_buttonPrefab.GetRootAncestor());
            IsFullyInitialized = true;
        }

        [HarmonyPatch(typeof(ChangeInputButton), nameof(ChangeInputButton.GetCurrentKey)), HarmonyPostfix]
        static private void ChangeInputButton_GetCurrentKey_Pre(ChangeInputButton __instance, ref KeyCode __result)
        {
            if (_settingsByButtonGUID.TryGetValue(__instance.inputName, out var settings)
            && settings.TryGetNonNull(__instance.playerNum, out var playerSetting))
                __result = playerSetting.ToKeyCode();
        }

        [HarmonyPatch(typeof(ChangeInputButton), nameof(ChangeInputButton.InputDetected)), HarmonyPrefix]
        static private bool ChangeInputButton_InputDetected_Pre(ChangeInputButton __instance, KeyCode targetKey)
        {
            // cache
            var inputManager = PseudoSingleton<GlobalInputManager>.instance;
            var playerInput = inputManager.inputData.playersInputList[__instance.playerNum];
            var inputReceiver = playerInput.inputType[(int)playerInput.currentInputType];

            // update
            __instance.currentKey = __instance.GetCurrentKey();
            KeyCode swapKey = BindingsConflictResolution == BindingConflictResolution.Swap
                ? __instance.currentKey : KeyCode.None;
            if (targetKey == UnbindButton)
                targetKey = KeyCode.None;

            __instance.buttonName.text = targetKey.ToString();
            __instance.buttonName.color = Color.grey;
            __instance.buttonName.ApplyText(false, true, "", true);
            inputManager.SetControllerId(__instance.playerNum, playerInput.joystickNumber);

            // swap
            if (targetKey != KeyCode.None
            && BindingsConflictResolution != BindingConflictResolution.Duplicate)
            {
                // vanilla
                foreach (var (RefName, _) in VANILLA_BUTTON_NAMES)
                    if (inputReceiver.TryGetField<KeyCode>(RefName, out var previousKeyCode)
                    && previousKeyCode == targetKey)
                    {
                        inputReceiver.SetField(RefName, swapKey);
                        break;
                    }
                // custom
                foreach (var settings in _settingsByButtonGUID)
                    if (settings.Value.TryGetNonNull(__instance.playerNum, out var playerSetting)
                    && playerSetting.ToKeyCode() == targetKey)
                    {
                        _settingsByButtonGUID[settings.Key][__instance.playerNum].Value = swapKey.ToString();
                        break;
                    }
            }

            // assign
            // vanilla
            foreach (var (RefName, InputName) in VANILLA_BUTTON_NAMES)
                if (InputName == __instance.inputName.ToLower())
                {
                    inputReceiver.SetField(RefName, targetKey);
                    break;
                }
            // custom
            foreach (var setting in _settingsByButtonGUID)
                if (setting.Key == __instance.inputName)
                {
                    _settingsByButtonGUID[setting.Key][__instance.playerNum].Value = targetKey.ToString();
                    break;
                }

            PseudoSingleton<TEventSystem>.instance.SelectObject(__instance.gameObject);
            __instance.myDeviceButton.updateButtonNames();
            return false;
        }

        [HarmonyPatch(typeof(TButtonNavigation), nameof(TButtonNavigation.Awake)), HarmonyPostfix]
        static private void TButtonNavigation_Awake_Post(TButtonNavigation __instance)
        {
            if (__instance.TryGetComponent(out ChangeInputButton changeInputButton)
            && _settingsByButtonGUID.ContainsKey(changeInputButton.inputName))
            {
                __instance.normalColor = __instance.targetImage.color = CUSTOM_BUTTON_COLOR;
                __instance.reafirmNeighboors = false;
            }
        }

        [HarmonyPatch(typeof(TranslationSystem), nameof(TranslationSystem.FindTerm)), HarmonyPostfix]
        static private void TranslationSystem_FindTerm_Post(TranslationSystem __instance, ref string __result, string element)
        {
            string typePrefix = typeof(CustomControls).Name + "_";
            if (element.Contains(typePrefix))
                __result = element.Replace(typePrefix, null);
        }

        [HarmonyPatch(typeof(GlobalInputManager), nameof(GlobalInputManager.GetKeyCodeName),
            new[] { typeof(int), typeof(string), typeof(string), typeof(Sprite) },
            new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Ref, ArgumentType.Ref }),
            HarmonyPrefix]
        static private void GlobalInputManager_GetKeyCodeName_Pre(GlobalInputManager __instance, int playerNum, string inputName, ref string buttonName, ref Sprite buttonIcon)
        {
            if (_settingsByButtonGUID.TryGetValue(inputName, out var settings)
            && settings.TryGetNonNull(playerNum, out var playerSetting))
                buttonName = playerSetting;
        }

        [HarmonyPatch(typeof(DetectDeviceWindow), nameof(DetectDeviceWindow.PressedKey)), HarmonyPrefix]
        static private void DetectDeviceWindow_PressedKey_Pre(DetectDeviceWindow __instance, ref KeyCode targetKey)
        {
            if (targetKey == UnbindButton)
                DetectDeviceWindow.useInputFilter = false;
        }

        [HarmonyPatch(typeof(ChangeInputButton), nameof(ChangeInputButton.OnEnable)), HarmonyPostfix]
        static private void ChangeInputButton_OnEnable_Post(ChangeInputButton __instance)
        {
            if (_settingsByButtonGUID.TryGetValue(__instance.inputName, out var settings))
                __instance.gameObject.SetActive(settings[__instance.playerNum] != null);
        }
    }
}