using System.IO;
using System.Linq;

namespace Reminiscence;

internal static class TldInstall
{
    public static bool Present(string mt1)
    {
        if (string.IsNullOrWhiteSpace(mt1))
            return false;

        string folder = Path.Combine(
            mt1,
            "MonsterTrain_Data",
            "StreamingAssets",
            "aa",
            "StandaloneWindows64",
            "DLC",
            "DLC_1");
        if (!Directory.Exists(folder))
            return false;

        return Directory.EnumerateFiles(folder, "*.bundle").Any();
    }
}
