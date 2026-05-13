using System.IO;

namespace PDFAgent.App.Services;

public static class AppArguments
{
    private static string[]? _args;

    public static void Initialize(string[] args)
    {
        _args = args;
    }

    public static bool TryGetFilePath(out string? filePath)
    {
        if (_args is { Length: > 0 } && File.Exists(_args[0]))
        {
            filePath = _args[0];
            return true;
        }

        filePath = null;
        return false;
    }
}
