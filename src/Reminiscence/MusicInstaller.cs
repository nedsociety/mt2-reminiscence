using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace Reminiscence;

internal static class MusicInstaller
{
    private static bool _installed;
    private static bool _extractDone;
    private static Process? _process;

    internal static bool Installed => _installed;

    internal static bool ReadyToReport { get; private set; }

    internal static bool ExtractionRunning => _process != null && !_process.HasExited;

    internal static string MenuMessage()
    {
        if (_installed)
            return "Reminiscence Activated!";
        if (Mt1Install.Resolve(Plugin.Mt1Path.Value) == null)
            return "Cannot find MT1 installation.";
        return "Reminiscence could not load MT1 music.";
    }

    public static void OnPluginAwake()
    {
        Application.quitting += OnQuit;
        string? mt1 = Mt1Install.Resolve(Plugin.Mt1Path.Value);
        if (mt1 == null)
        {
            Plugin.LogSource.LogError("MT1 not found. Set MT1 InstallPath in the Reminiscence config and restart.");
            ReadyToReport = true;
            StatusNotices.WatchForMainMenu(Plugin.Instance);
            return;
        }

        if (MusicCache.Load(MusicCache.DirectoryForPlugin()) != null)
            return;

        if (!BeginExtract(mt1, MusicCache.DirectoryForPlugin()))
        {
            Plugin.LogSource.LogError($"Music cache is missing at {MusicCache.DirectoryForPlugin()}.");
            ReadyToReport = true;
            StatusNotices.WatchForMainMenu(Plugin.Instance);
            return;
        }

        HoldLoadingScreen();
    }

    public static void Install(MonoBehaviour soundManager)
    {
        if (_installed)
        {
            ReadyToReport = true;
            StatusNotices.WatchForMainMenu(soundManager);
            return;
        }
        if (_process != null && !_process.HasExited)
        {
            soundManager.StartCoroutine(InstallWhenExtracted(soundManager));
            return;
        }

        try
        {
            InstallNow(soundManager);
        }
        finally
        {
            ReadyToReport = true;
            StatusNotices.WatchForMainMenu(soundManager);
        }
    }

    private static void InstallNow(MonoBehaviour soundManager)
    {
        FinishProcess();

        string? mt1 = Mt1Install.Resolve(Plugin.Mt1Path.Value);
        if (mt1 == null)
        {
            Plugin.LogSource.LogError("MT1 not found. Set MT1 InstallPath in the Reminiscence config and restart.");
            return;
        }

        bool tld = TldInstall.Present(mt1);
        string cacheDir = MusicCache.DirectoryForPlugin();
        MusicManifest? manifest = MusicCache.Load(cacheDir);
        if (manifest == null)
        {
            Plugin.LogSource.LogError($"Music cache is missing at {cacheDir}.");
            return;
        }

        Plugin.LogSource.LogInfo($"Music cache has {manifest.pieces.Length} pieces. TLD bundle on disk: {tld}.");

        Component audioSystem = soundManager.GetComponent(AccessTools.TypeByName("ShinyShoe.Audio.CoreAudioSystem"));
        if (audioSystem == null)
        {
            Plugin.LogSource.LogError("CoreAudioSystem is missing from SoundManager.");
            return;
        }

        object audioData = Field(audioSystem, "AudioSystemData");
        object musicData = Field(audioData, "MusicDefData");
        Array tracks = (Array)Field(musicData, "Tracks");
        object? template = FindTemplate(tracks);
        if (template == null)
        {
            Plugin.LogSource.LogError("MT2 battle music definitions were not found.");
            return;
        }

        AudioMixer mixer = (AudioMixer)Field(template, "Mixer");
        AudioMixerGroup? baseGroup = FindGroup(mixer, "Base");
        AudioMixerGroup? highGroup = FindGroup(mixer, "High");
        AudioMixerSnapshot baseSnapshot = mixer.FindSnapshot("Base");
        AudioMixerSnapshot highSnapshot = mixer.FindSnapshot("High Intensity");
        if (baseGroup == null || baseSnapshot == null)
        {
            Plugin.LogSource.LogError("BattleMusic mixer is missing its Base group or snapshot.");
            return;
        }

        Type musicType = tracks.GetType().GetElementType();
        Type clipType = musicType.DeclaringType.GetNestedType("MusicClipDefinition");
        Type snapType = musicType.DeclaringType.GetNestedType("MusicSnapshotDefinition");
        var created = new List<object>();
        foreach (MusicPiece piece in manifest.pieces)
        {
            if (piece == null || string.IsNullOrEmpty(piece.trackId))
                continue;
            if (piece.musicId == "BattleMusic_TLD" && !tld)
            {
                Plugin.LogSource.LogInfo("The Last Divinity bundle is absent. BattleMusic_TLD stays out.");
                continue;
            }

            object? definition = TryCreate(cacheDir, piece, musicType, clipType, snapType, mixer, baseGroup, highGroup, baseSnapshot, highSnapshot);
            if (definition == null)
                continue;
            created.Add(definition);
            AddTrackEntry(soundManager, piece);
            BattleMusicPatches.Remember(piece.trackId);
            Plugin.LogSource.LogInfo($"Registered {piece.name} as {piece.trackId}.");
        }

        if (created.Count == 0)
        {
            Plugin.LogSource.LogError("No Reminiscence tracks were registered.");
            return;
        }

        Array expanded = Array.CreateInstance(musicType, tracks.Length + created.Count);
        Array.Copy(tracks, expanded, tracks.Length);
        for (int i = 0; i < created.Count; i++)
            expanded.SetValue(created[i], tracks.Length + i);
        SetField(musicData, "Tracks", expanded);
        _installed = true;
        Plugin.LogSource.LogInfo($"Registered {created.Count} Reminiscence tracks. TLD bundle: {tld}.");
    }

    private static bool BeginExtract(string mt1, string cacheDir)
    {
        string pluginDir = Path.GetDirectoryName(typeof(MusicInstaller).Assembly.Location);
        string exe = Path.Combine(pluginDir, "ReminiscenceExtract.exe");
        if (!File.Exists(exe))
            return false;

        Plugin.LogSource.LogInfo($"Extracting MT1 music into {cacheDir}.");
        Directory.CreateDirectory(cacheDir);
        var start = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = "--mt1 \"" + mt1 + "\" --out \"" + cacheDir + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        _process = Process.Start(start);
        if (_process != null)
            StatusNotices.ShowExtraction();
        return _process != null;
    }

    private static void HoldLoadingScreen()
    {
        Type? screen = AccessTools.TypeByName("ShinyShoe.Loading.LoadingScreen");
        Type? taskType = AccessTools.TypeByName("ShinyShoe.Loading.LoadWaitPredicateTask");
        Type? loadingTask = AccessTools.TypeByName("ShinyShoe.Loading.LoadingTask");
        Type? style = screen?.GetNestedType("DisplayStyle");
        MethodInfo? add = screen == null || loadingTask == null
            ? null
            : AccessTools.Method(screen, "AddTask", new[] { loadingTask, typeof(bool) });
        if (taskType == null || style == null || add == null)
        {
            Plugin.LogSource.LogError("Loading screen is unavailable. Startup will not wait for music extraction.");
            return;
        }

        object task = Activator.CreateInstance(
            taskType,
            new Func<bool>(ExtractionFinished),
            Enum.Parse(style, "FullScreen"),
            null);
        add.Invoke(null, new object?[] { task, true });
        Plugin.LogSource.LogInfo("Holding the loading screen until MT1 music is extracted.");
    }

    private static bool ExtractionFinished()
    {
        if (_extractDone)
            return true;
        if (_process != null && !_process.HasExited)
        {
            StatusNotices.ShowOnFlame();
            return false;
        }
        _extractDone = true;
        StatusNotices.HideExtraction();
        Plugin.LogSource.LogInfo("Music extraction finished. Releasing the loading screen.");
        return true;
    }

    private static IEnumerator InstallWhenExtracted(MonoBehaviour soundManager)
    {
        while (_process != null && !_process.HasExited)
            yield return null;
        Install(soundManager);
    }

    private static void FinishProcess()
    {
        Process? process = _process;
        _process = null;
        if (process == null)
            return;
        try
        {
            if (process.HasExited && process.ExitCode != 0)
                Plugin.LogSource.LogError($"Music extraction failed ({process.ExitCode}).");
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void OnQuit()
    {
        if (_process == null || _process.HasExited)
            return;
        try
        {
            _process.Kill();
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"Could not stop music extraction on quit: {ex.Message}");
        }
    }

    private static object? TryCreate(
        string cacheDir,
        MusicPiece piece,
        Type musicType,
        Type clipType,
        Type snapType,
        AudioMixer mixer,
        AudioMixerGroup baseGroup,
        AudioMixerGroup? highGroup,
        AudioMixerSnapshot baseSnapshot,
        AudioMixerSnapshot? highSnapshot)
    {
        MusicLayer? baseLayer = FindLayer(piece, "base");
        if (baseLayer == null)
        {
            Plugin.LogSource.LogError($"{piece.trackId} has no base layer.");
            return null;
        }

        AudioClip? baseClip = LoadLayer(cacheDir, baseLayer, piece.trackId + ".base");
        if (baseClip == null)
            return null;

        bool field = piece.role == "field";
        Array clips;
        Array snapshots;
        if (field)
        {
            MusicLayer? highLayer = FindLayer(piece, "high");
            if (highLayer == null || highGroup == null || highSnapshot == null)
            {
                Plugin.LogSource.LogError($"{piece.trackId} is missing its high layer or the High mixer snapshot.");
                return null;
            }
            AudioClip? highClip = LoadLayer(cacheDir, highLayer, piece.trackId + ".high");
            if (highClip == null)
                return null;
            clips = Two(clipType, Clip(clipType, baseClip, baseGroup), Clip(clipType, highClip, highGroup));
            snapshots = Two(
                snapType,
                Snap(snapType, "Base", baseSnapshot, 1f),
                Snap(snapType, "High", highSnapshot, 0f));
        }
        else
        {
            clips = One(clipType, Clip(clipType, baseClip, baseGroup));
            snapshots = One(snapType, Snap(snapType, "Base", baseSnapshot, 1f));
        }

        object? definition = Activator.CreateInstance(musicType);
        if (definition == null)
            return null;
        SetField(definition, "Name", piece.trackId);
        SetField(definition, "Mixer", mixer);
        SetField(definition, "Clips", clips);
        SetField(definition, "Snapshots", snapshots);
        return definition;
    }

    private static AudioClip? LoadLayer(string cacheDir, MusicLayer layer, string clipName)
    {
        string path = Path.Combine(cacheDir, layer.file ?? "");
        if (!File.Exists(path))
        {
            Plugin.LogSource.LogError($"Missing music file {path}.");
            return null;
        }
        return WavPcm.ToClip(path, clipName);
    }

    private static void AddTrackEntry(MonoBehaviour soundManager, MusicPiece piece)
    {
        string fieldName = piece.role == "boss" ? "bossBattleMusicTracks" : "battleMusicTracks";
        IList list = (IList)Field(soundManager, fieldName);
        Type trackType = soundManager.GetType().GetNestedType("BattleMusicTrack");
        object entry = Activator.CreateInstance(trackType);
        trackType.GetField("publicTrackNameKey").SetValue(entry, MusicTitles.KeyFor(piece));
        trackType.GetField("trackNameData").SetValue(entry, piece.trackId);
        list.Add(entry);

        if (piece.role == "field")
        {
            IList bucket = (IList)Field(soundManager, "battleMusicTrackRandomBucket");
            if (bucket.Count > 0)
                bucket.Add(entry);
        }
    }

    private static object? FindTemplate(Array tracks)
    {
        foreach (object track in tracks)
        {
            string? name = Field(track, "Name") as string;
            if (name != null && name.StartsWith("BattleMusic_", StringComparison.Ordinal))
                return track;
        }
        return null;
    }

    private static MusicLayer? FindLayer(MusicPiece piece, string id)
    {
        if (piece.layers == null)
            return null;
        foreach (MusicLayer layer in piece.layers)
        {
            if (layer != null && layer.id == id)
                return layer;
        }
        return null;
    }

    private static AudioMixerGroup? FindGroup(AudioMixer mixer, string name)
    {
        foreach (AudioMixerGroup group in mixer.FindMatchingGroups(name))
        {
            if (group != null && group.name == name)
                return group;
        }
        return null;
    }

    private static object Clip(Type clipType, AudioClip clip, AudioMixerGroup group)
    {
        object definition = Activator.CreateInstance(clipType);
        SetField(definition, "Clip", clip);
        SetField(definition, "MixerGroup", group);
        return definition;
    }

    private static object Snap(Type snapType, string name, AudioMixerSnapshot snapshot, float seconds)
    {
        object definition = Activator.CreateInstance(snapType);
        SetField(definition, "Name", name);
        SetField(definition, "Snapshot", snapshot);
        SetField(definition, "TransitionTimeSeconds", seconds);
        return definition;
    }

    private static Array One(Type element, object value)
    {
        Array array = Array.CreateInstance(element, 1);
        array.SetValue(value, 0);
        return array;
    }

    private static Array Two(Type element, object first, object second)
    {
        Array array = Array.CreateInstance(element, 2);
        array.SetValue(first, 0);
        array.SetValue(second, 1);
        return array;
    }

    private static object Field(object target, string name)
    {
        return TargetField(target, name).GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
        TargetField(target, name).SetValue(target, value);
    }

    private static FieldInfo TargetField(object target, string name)
    {
        FieldInfo field = AccessTools.Field(target.GetType(), name);
        if (field == null)
            throw new MissingFieldException(target.GetType().FullName, name);
        return field;
    }
}
