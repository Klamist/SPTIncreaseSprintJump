using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace SuperSprintJump
{
    [BepInPlugin("ciallo.SuperSprintJump", "Super Sprint Jump", "1.2.1")]
    public class SpeedControlPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> sprintSpeedMultiplier;
        private static ConfigEntry<bool> speedBreakLimit;
        private static ConfigEntry<float> jumpMultiplier;
        private static ConfigEntry<bool> noFallDamage;
        private static ConfigEntry<bool> walkacc;

        private static ConfigEntry<float> sprintStep;
        private static ConfigEntry<KeyboardShortcut> increaseSpeedKey;
        private static ConfigEntry<KeyboardShortcut> decreaseSpeedKey;
        private static ConfigEntry<float> jumpStep;
        private static ConfigEntry<KeyboardShortcut> increaseJumpKey;
        private static ConfigEntry<KeyboardShortcut> decreaseJumpKey;
        private static ConfigEntry<bool> guiEnabled;
        private static ConfigEntry<int> guiPosX;
        private static ConfigEntry<int> guiPosY;

        private Harmony harmony;
        private static ManualLogSource logger;

        void Awake()
        {
            logger = Logger;

            sprintSpeedMultiplier = Config.Bind("General", "Sprint Speed Multiplier", 1.0f,
                new ConfigDescription("Physical effect will limit actual speed to not higher than 1.5~2.0x",
                new AcceptableValueRange<float>(1.0f, 20.0f)));

            speedBreakLimit = Config.Bind("General", "Sprint Speed Overlimit (?)", false,
                new ConfigDescription("Break physical speed limit.\n" +
                "Buggy: Speed > 3x will get uphill obstacled and downhill floated, and >10x might run into ground and die."));

            jumpMultiplier = Config.Bind("General", "Jump Height Multiplier", 1.0f,
                new ConfigDescription("Sprint+Jump distance depends on Sprint Speed coef.\n" +
                "Recommend coef: sprint > jump/2. Sprint overlimit won't affect jump, can be disabled if use high sprint coef.",
                new AcceptableValueRange<float>(1.0f, 20.0f)));

            noFallDamage = Config.Bind("General", "No Fall Damage (?)", false,
                new ConfigDescription("Fall damage is auto divided by Jump Height coef. This is for remove fall damage."));

            walkacc = Config.Bind("General", "Walk Acceleration (?)", false,
                new ConfigDescription("Speed multiplier also works on normal W A S D walking."));

            sprintStep = Config.Bind("Hotkeys", "Sprint Changing Step", 0.25f);
            decreaseSpeedKey = Config.Bind("Hotkeys", "Sprint Speed -", new KeyboardShortcut(KeyCode.KeypadMinus));
            increaseSpeedKey = Config.Bind("Hotkeys", "Sprint Speed +", new KeyboardShortcut(KeyCode.KeypadPlus));
            jumpStep = Config.Bind("Hotkeys", "Jump Changing Step", 0.5f);
            decreaseJumpKey = Config.Bind("Hotkeys", "Jump Height -", new KeyboardShortcut(KeyCode.None));
            increaseJumpKey = Config.Bind("Hotkeys", "Jump Height +", new KeyboardShortcut(KeyCode.None));

            guiEnabled = Config.Bind("HUD", "Ingame HUD", true);
            guiPosX = Config.Bind("HUD", "Text Position X", 100);
            guiPosY = Config.Bind("HUD", "Text Position Y", Screen.height - 150);

            harmony = new Harmony("ciallo.SuperSprintJump");
            harmony.PatchAll();
        }

        void Update()
        {
            if (increaseSpeedKey.Value.MainKey != KeyCode.None && UnityInput.Current.GetKeyDown(increaseSpeedKey.Value.MainKey))
                sprintSpeedMultiplier.Value = Mathf.Min(20f, sprintSpeedMultiplier.Value + sprintStep.Value);
            if (increaseSpeedKey.Value.MainKey != KeyCode.None && UnityInput.Current.GetKeyDown(decreaseSpeedKey.Value.MainKey))
                sprintSpeedMultiplier.Value = Mathf.Max(1.0f, sprintSpeedMultiplier.Value - sprintStep.Value);
            if (increaseSpeedKey.Value.MainKey != KeyCode.None && UnityInput.Current.GetKeyDown(increaseJumpKey.Value.MainKey))
                jumpMultiplier.Value = Mathf.Min(20f, jumpMultiplier.Value + jumpStep.Value);
            if (increaseSpeedKey.Value.MainKey != KeyCode.None && UnityInput.Current.GetKeyDown(decreaseJumpKey.Value.MainKey))
                jumpMultiplier.Value = Mathf.Max(1.0f, jumpMultiplier.Value - jumpStep.Value);
        }

        void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        // 跳跃高度
        [HarmonyPatch(typeof(JumpStateClass), "Enter")]
        class Patch_JumpEnter
        {
            static void Postfix(JumpStateClass __instance)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld?.MainPlayer == null) return;

                var playerField = AccessTools.Field(typeof(MovementContext), "_player");
                Player player = (Player)playerField.GetValue(__instance.MovementContext);

                if (player == gameWorld.MainPlayer)
                {
                    __instance.Vector3_1 *= Mathf.Sqrt(jumpMultiplier.Value);
                }
            }
        }

        // 摔落伤害
        [HarmonyPatch(typeof(EFT.HealthSystem.ActiveHealthController), "HandleFall")]
        class Patch_HandleFall
        {
            static bool Prefix(EFT.HealthSystem.ActiveHealthController __instance, float height, ref float __result)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld?.MainPlayer == null) return true;

                if (__instance.Player == gameWorld.MainPlayer)
                {
                    if (noFallDamage.Value)
                    {
                        __result = 0f;
                        return false;
                    }
                    else if (jumpMultiplier.Value > 1.0f)
                    {
                        float num = height - __instance.FallSafeHeight_1;
                        if (!num.Positive())
                        {
                            __result = 0f;
                            return false;
                        }

                        float damage = num * Mathf.Sqrt(num) *
                                       GClass3009<EFT.HealthSystem.ActiveHealthController.GClass3008>.GClass1728_0.Falling.DamagePerMeter *
                                       __instance.Player.Physical.FallDamageMultiplier;

                        damage /= jumpMultiplier.Value;

                        __instance.ApplyDamage(EBodyPart.LeftLeg, damage, GClass3051.FallDamage);
                        __instance.ApplyDamage(EBodyPart.RightLeg, damage, GClass3051.FallDamage);

                        __result = damage;
                        return false;
                    }
                }
                return true;
            }
        }

        // 冲刺速度增加
        [HarmonyPatch(typeof(MovementContext), "get_ClampedSpeed")]
        class Patch_ClampedSpeed
        {
            static void Postfix(MovementContext __instance, ref float __result)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld?.MainPlayer == null) return;

                var playerField = AccessTools.Field(typeof(MovementContext), "_player");
                Player player = (Player)playerField.GetValue(__instance);

                if (player == gameWorld.MainPlayer && (__instance.IsSprintEnabled || SpeedControlPlugin.walkacc.Value))
                {
                    __result *= SpeedControlPlugin.sprintSpeedMultiplier.Value;
                }
            }
        }

        [HarmonyPatch(typeof(MovementState), "ApplyMotion")]
        class Patch_ApplyMotion
        {
            static void Prefix(ref Vector3 motion, float deltaTime, MovementState __instance)
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld?.MainPlayer == null) return;

                var playerField = AccessTools.Field(typeof(MovementContext), "_player");
                Player player = (Player)playerField.GetValue(__instance.MovementContext);

                if (player == gameWorld.MainPlayer && (__instance.MovementContext.IsSprintEnabled || SpeedControlPlugin.walkacc.Value))
                {
                    motion *= SpeedControlPlugin.sprintSpeedMultiplier.Value;
                }
            }

            static void Postfix(MovementState __instance, Vector3 motion, float deltaTime)
            {
                if (!SpeedControlPlugin.speedBreakLimit.Value)
                    return;

                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld?.MainPlayer == null) return;

                var playerField = AccessTools.Field(typeof(MovementContext), "_player");
                Player player = (Player)playerField.GetValue(__instance.MovementContext);

                if (player == gameWorld.MainPlayer && (__instance.MovementContext.IsSprintEnabled || SpeedControlPlugin.walkacc.Value))
                {
                    float mult = SpeedControlPlugin.sprintSpeedMultiplier.Value;
                    Vector3 current_motion = new Vector3(motion.x, motion.y, motion.z) * (mult - 1f);
                    player.Transform.position += current_motion * deltaTime;
                }
            }
        }

        // GUI 显示
        [HarmonyPatch(typeof(GameWorld), "OnGameStarted")]
        class Patch_OnGameStarted
        {
            static void Postfix(GameWorld __instance)
            {
                __instance.GetOrAddComponent<SpeedJumpHUD>();
            }
        }
        public class SpeedJumpHUD : MonoBehaviour
        {
            private Texture2D backgroundTex;
            private void Awake()
            {
                backgroundTex = new Texture2D(1, 1);
                backgroundTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.3f)); // 半透明黑色
                backgroundTex.Apply();
            }

            private void OnGUI()
            {
                if (!SpeedControlPlugin.guiEnabled.Value) return;
                if (Singleton<GameWorld>.Instance?.MainPlayer == null) return;

                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.fontSize = 16;
                style.normal.textColor = Color.white;

                string text = $"Sprint = {SpeedControlPlugin.sprintSpeedMultiplier.Value:F2}x\n" +
                              $"Jump = {SpeedControlPlugin.jumpMultiplier.Value:F2}x";

                Rect rect = new Rect(
                    SpeedControlPlugin.guiPosX.Value,
                    SpeedControlPlugin.guiPosY.Value,
                    114, 42
                );

                GUI.DrawTexture(rect, backgroundTex);
                GUI.Label(rect, text, style);

            }

            private void OnDestroy()
            {
                if (backgroundTex != null)
                    Destroy(backgroundTex);
            }
        }
    }
}
