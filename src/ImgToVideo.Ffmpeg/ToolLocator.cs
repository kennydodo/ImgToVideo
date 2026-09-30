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

    /// <summary>
    /// Every existing binary this configuration could plausibly mean, in the
    /// same priority order <see cref="Resolve"/> would search, starting with
    /// what <see cref="Resolve"/> itself would return. Used where a single
    /// "first match wins" answer isn't good enough - e.g. auto-selecting a
    /// hardware-encoder-capable ffmpeg when the configured one turns out not
    /// to support hardware encoding on this machine (see
    /// PreviewRenderPlanFactory.ResolveEffectiveFfmpegPath) - without
    /// requiring the configured path itself to be changed.
    /// </summary>
    public static IReadOnlyList<string> ResolveAllCandidates(string toolName, string configured)
    {
        var configuredValue = string.IsNullOrWhiteSpace(configured) ? toolName : configured.Trim();
        var executableName = configuredValue.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? configuredValue
            : configuredValue + ".exe";

        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (seen.Add(path))
            {
                results.Add(path);
            }
        }

        if (Path.IsPathRooted(configuredValue) && File.Exists(configuredValue))
        {
            Add(configuredValue);
        }

        foreach (var directory in PathDirectories(executableName))
        {
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                Add(candidate);
            }
        }

        return results;
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

        // A common convention for hand-installed portable tools (e.g. an
        // alternate ffmpeg build kept alongside the PATH one for compatibility
        // reasons). Each immediate subfolder of C:\Tools is checked, not just
        // one fixed name, so this keeps working if that subfolder gets
        // renamed or a different alternate build is dropped in later.
        var toolsRoot = Path.Combine("C:", "Tools");
        if (Directory.Exists(toolsRoot))
        {
            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(toolsRoot);
            }
            catch (IOException)
            {
                subdirectories = [];
            }
            catch (UnauthorizedAccessException)
            {
                subdirectories = [];
            }

            foreach (var subdirectory in subdirectories)
            {
                yield return subdirectory;
                var binSubdirectory = Path.Combine(subdirectory, "bin");
                if (Directory.Exists(binSubdirectory))
                {
                    yield return binSubdirectory;
                }
            }
        }
    }
}
