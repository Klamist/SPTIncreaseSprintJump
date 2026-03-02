using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace SuperSprintJump
{
    [BepInPlugin("ciallo.SuperSprintJump", "Super Sprint Jump", "1.1.0")]
    public class SpeedControlPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> sprintSpeedMultiplier;
        private static ConfigEntry<bool> speedBreakLimit;
        private static ConfigEntry<float> jumpMultiplier;
        private static ConfigEntry<bool> noFallDamage;
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

            harmony = new Harmony("ciallo.SuperSprintJump");
            harmony.PatchAll();
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

                if (player == gameWorld.MainPlayer && __instance.IsSprintEnabled)
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

                if (player == gameWorld.MainPlayer && __instance.MovementContext.IsSprintEnabled)
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

                if (player == gameWorld.MainPlayer && __instance.MovementContext.IsSprintEnabled)
                {
                    float mult = SpeedControlPlugin.sprintSpeedMultiplier.Value;
                    Vector3 current_motion = new Vector3(motion.x, motion.y, motion.z) * (mult - 1f);
                    player.Transform.position += current_motion * deltaTime;
                }
            }
        }
    }
}
