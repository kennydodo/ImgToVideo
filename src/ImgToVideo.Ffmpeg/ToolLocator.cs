namespace ImgToVideo.Ffmpeg;

public static class ToolLocator
{
    public static string Resolve(string toolName, string configured)
    {
        var configuredValue = string.IsNullOrWhiteSpace(configured) ? toolName : configured.Trim();

        if (Path.IsPathRooted(configuredValue))
        {
            return configuredValue;
        }

        var executableName = configuredValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? configuredValue
            : configuredValue + ".exe";

        foreach (var directory in PathDirectories(executableName))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return configuredValue;
    }

    private static IEnumerable<string> PathDirectories(string executableName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in new[]
                 {
                     Environment.GetEnvironmentVariable("PATH"),
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
                     Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                 })
        {
            if (string.IsNullOrEmpty(rawPath))
            {
                continue;
            }

            foreach (var dir in rawPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(dir))
                {
                    yield return dir;
                }
            }
        }

        foreach (var directory in FallbackDirectories(executableName))
        {
            if (seen.Add(directory))
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> FallbackDirectories(string executableName)
    {
        // Tools shipped with the app (portable / installer builds): next to the exe,
        // or in an "ffmpeg" subfolder of the install directory.
        var appBase = AppContext.BaseDirectory;
        if (appBase.Length > 0)
        {
            yield return appBase;
            yield return Path.Combine(appBase, "ffmpeg");
            yield return Path.Combine(appBase, "tools", "ffmpeg");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0)
        {
            yield return Path.Combine(localAppData, "Microsoft", "WinGet", "Links");

            var packagesDirectory = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(packagesDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(
                             packagesDirectory, executableName, SearchOption.AllDirectories))
                {
                    yield return Path.GetDirectoryName(file)!;
                }
            }
        }

        yield return Path.Combine("C:", "ProgramData", "chocolatey", "bin");
        yield return Path.Combine("C:", "ffmpeg", "bin");
    }
}
