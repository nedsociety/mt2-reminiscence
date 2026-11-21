using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Reminiscence;

internal static class Mt1Install
{
    public static string? Resolve(string configured)
    {
        foreach (string candidate in Candidates(configured))
        {
            if (IsInstall(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    public static bool IsInstall(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "MonsterTrain.exe"));
    }

    private static IEnumerable<string> Candidates(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured;

        string env = Environment.GetEnvironmentVariable("MT1_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            yield return env;

        string data = Application.dataPath;
        if (string.IsNullOrEmpty(data))
            yield break;

        string mt2 = Path.GetDirectoryName(data);
        string? common = string.IsNullOrEmpty(mt2) ? null : Path.GetDirectoryName(mt2);
        if (!string.IsNullOrEmpty(common))
            yield return Path.Combine(common, "Monster Train");
    }
}
