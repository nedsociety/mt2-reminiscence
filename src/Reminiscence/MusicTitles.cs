using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace Reminiscence;

internal static class MusicTitles
{
    private static bool _reported;

    public static string KeyFor(MusicPiece piece)
    {
        string key = "Reminiscence/" + piece.musicId + "_Name";
        string title = string.IsNullOrEmpty(piece.name) ? piece.musicId : piece.name;
        try
        {
            Insert(key, title);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError($"Could not add the music title for {piece.musicId}: {ex.Message}");
        }
        return key;
    }

    private static void Insert(string key, string title)
    {
        Type util = AccessTools.TypeByName("LocalizationUtil");
        MethodInfo getSource = AccessTools.Method(util, "GetMainLanguageSource");
        if (getSource == null)
            throw new MissingMethodException("LocalizationUtil", "GetMainLanguageSource");

        object source = getSource.Invoke(null, null);
        if (source == null)
            throw new InvalidOperationException("Main language source is missing.");

        IList languages = (IList)AccessTools.Field(source.GetType(), "mLanguages").GetValue(source);
        if (languages == null || languages.Count == 0)
            throw new InvalidOperationException("Main language source has no languages.");
        if (AccessTools.Field(source.GetType(), "translationsData").GetValue(source) == null)
            throw new InvalidOperationException("Main language source has no translation table.");

        MethodInfo addTerm = AccessTools.Method(source.GetType(), "AddTerm", new[] { typeof(string) });
        object term = addTerm.Invoke(source, new object[] { key });
        if (term == null)
            throw new InvalidOperationException("AddTerm returned no term.");

        MethodInfo setTranslation = AccessTools.Method(term.GetType(), "SetTranslation", new[] { typeof(int), typeof(string), typeof(string) });
        for (int i = 0; i < languages.Count; i++)
            setTranslation.Invoke(term, new object?[] { i, title, null });

        if (_reported)
            return;
        _reported = true;
        Plugin.LogSource.LogInfo($"Music titles use the same name in all {languages.Count} languages.");
    }
}
