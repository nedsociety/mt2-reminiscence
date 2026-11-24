using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Reminiscence;

internal static class BattleMusicPatches
{
    private static readonly Dictionary<string, string> BossAlternates = new Dictionary<string, string>
    {
        { "BattleMusic_Arkios", "Reminiscence/BattleMusic_Daedalus" },
        { "BattleMusic_Fleshweaver", "Reminiscence/BattleMusic_Talos" },
        { "BattleMusic_Cael", "Reminiscence/BattleMusic_Fel" },
        { "BattleMusic_HallowStryx", "Reminiscence/BattleMusic_Archus" },
        { "BattleMusic_Seraph", "Reminiscence/BattleMusic_Seraph" },
        { "BattleMusic_Titans", "Reminiscence/BattleMusic_TLD" },
    };

    private static readonly System.Random Coin = new System.Random();
    private static readonly HashSet<string> Loaded = new HashSet<string>();

    internal static void Remember(string trackId)
    {
        if (!string.IsNullOrEmpty(trackId))
            Loaded.Add(trackId);
    }

    [HarmonyPatch]
    private static class StartPatch
    {
        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static System.Reflection.MethodBase? TargetMethod()
        {
            Type soundManager = AccessTools.TypeByName("SoundManager");
            return soundManager == null ? null : AccessTools.Method(soundManager, "Start");
        }

        private static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                MusicInstaller.Install(__instance);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Music install failed: {ex}");
            }
        }
    }

    [HarmonyPatch]
    private static class PlayPatch
    {
        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static System.Reflection.MethodBase? TargetMethod()
        {
            Type soundManager = AccessTools.TypeByName("SoundManager");
            return soundManager == null ? null : AccessTools.Method(soundManager, "PlayBattleMusic");
        }

        private static void Prefix(ref string trackNameOverride)
        {
            if (!Plugin.BossMusics.Value)
                return;
            if (string.IsNullOrEmpty(trackNameOverride))
                return;
            if (!BossAlternates.TryGetValue(trackNameOverride, out string alternate))
                return;
            if (!Loaded.Contains(alternate))
                return;
            if (Coin.Next(2) == 0)
                trackNameOverride = alternate;
        }

        private static void Postfix(MonoBehaviour __instance)
        {
            if (!InRestart(__instance))
                return;
            try
            {
                float seconds = HighTransitionSeconds(__instance);
                System.Reflection.MethodInfo? transition = AccessTools.Method(__instance.GetType(), "TransitionToSnapshot");
                transition?.Invoke(__instance, new object[] { "Base", seconds });
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Could not return music to the base layer: {ex.Message}");
            }
        }

        private static float HighTransitionSeconds(MonoBehaviour soundManager)
        {
            Component? audio = soundManager.GetComponent(AccessTools.TypeByName("ShinyShoe.Audio.CoreAudioSystem"));
            if (audio == null)
                return 0f;
            Array? states = AccessTools.Field(audio.GetType(), "_musicState")?.GetValue(audio) as Array;
            object? nextSlot = AccessTools.Field(audio.GetType(), "_musicNextTrackSlot")?.GetValue(audio);
            if (states == null || nextSlot == null || states.Length == 0)
                return 0f;
            object? state = states.GetValue(((int)nextSlot + 1) % states.Length);
            object? definition = state == null ? null : AccessTools.Field(state.GetType(), "MusicDefinition")?.GetValue(state);
            if (definition == null || AccessTools.Field(definition.GetType(), "Snapshots")?.GetValue(definition) is not Array snapshots)
                return 0f;
            foreach (object snapshot in snapshots)
            {
                if (snapshot == null || AccessTools.Field(snapshot.GetType(), "Name")?.GetValue(snapshot) as string != "High")
                    continue;
                if (AccessTools.Field(snapshot.GetType(), "TransitionTimeSeconds")?.GetValue(snapshot) is float time)
                    return time;
            }
            return 0f;
        }

        private static bool InRestart(MonoBehaviour soundManager)
        {
            object save = AccessTools.Field(soundManager.GetType(), "saveManager").GetValue(soundManager);
            if (save == null)
                return false;
            System.Reflection.PropertyInfo? mode = AccessTools.Property(save.GetType(), "IsInRestartBattleMode");
            return mode != null && mode.GetValue(save) is bool restart && restart;
        }
    }

    [HarmonyPatch(typeof(AudioClip), nameof(AudioClip.UnloadAudioData))]
    private static class KeepClipDataPatch
    {
        private static bool Prefix(AudioClip __instance)
        {
            return __instance == null
                || __instance.name == null
                || !__instance.name.StartsWith("Reminiscence/", StringComparison.Ordinal);
        }
    }
}
