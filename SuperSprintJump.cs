using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace SuperSprintJump
{
    [BepInPlugin("ciallo.SuperSprintJump", "Super Sprint Jump", "1.0.0")]
    public class SpeedControlPlugin : BaseUnityPlugin
    {
        private static ConfigEntry<float> sprintSpeedMultiplier;
        private static ConfigEntry<float> jumpMultiplier;
        private static ConfigEntry<bool> noFallDamage;
        private Harmony harmony;
        private static ManualLogSource logger;

        void Awake()
        {
            logger = Logger;

            sprintSpeedMultiplier = Config.Bind("General", "Sprint Speed Multiplier", 1.0f,
                new ConfigDescription("Only affect yourself",
                new AcceptableValueRange<float>(1.0f, 2.0f)));

            jumpMultiplier = Config.Bind("General", "Jump Height Multiplier", 1.0f,
                new ConfigDescription("If this > 2.0, Sprint Speed will = origin * JumpHeight / 2",
                new AcceptableValueRange<float>(1.0f, 20.0f)));

            noFallDamage = Config.Bind("General", "NoFallDamage", false,
                new ConfigDescription("Fall damage is auto divided by Jump Height coef. This will remove fall damage."));

            harmony = new Harmony("ciallo.SuperSprintJump");
            harmony.PatchAll();
        }

        void OnDestroy()
        {
            harmony?.UnpatchSelf();
        }

        // 跳跃高度补丁
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

        // 摔落伤害补丁
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

        // 本地玩家冲刺时速度倍率
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
                    float sprintMult = SpeedControlPlugin.sprintSpeedMultiplier.Value;
                    float jumpMult = SpeedControlPlugin.jumpMultiplier.Value;

                    float finalMult = sprintMult;

                    if (jumpMult > sprintMult)
                    {
                        if (jumpMult > 2f)
                        {
                            finalMult *= (jumpMult / 2f);
                        }
                    }
                    __result *= finalMult;
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
                    float sprintMult = SpeedControlPlugin.sprintSpeedMultiplier.Value;
                    float jumpMult = SpeedControlPlugin.jumpMultiplier.Value;

                    float finalMult = sprintMult;

                    if (jumpMult > sprintMult)
                    {
                        if (jumpMult > 2f)
                        {
                            finalMult *= (jumpMult / 2f);
                        }
                    }
                    motion *= finalMult;
                }
            }
        }

    }
}
