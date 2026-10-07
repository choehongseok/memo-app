namespace MemoApp.Core.Transfer;
internal static class LocalFilePath
{
    internal static string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.Any(char.IsControl)) throw new IOException("Only ordinary local paths are supported");
        int colon = path.IndexOf(':');
        if (colon >= 0 && (!OperatingSystem.IsWindows() || colon != 1 || !char.IsAsciiLetter(path[0]) || path.IndexOf(':', colon + 1) >= 0)) throw new IOException("Device/stream/URI paths refused");
        path = Path.GetFullPath(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) throw new IOException("Normalized network/device paths refused");
        if (OperatingSystem.IsWindows())
        {
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) throw new IOException("Network drives refused");
            foreach (var segment in path[Path.GetPathRoot(path)!.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                var stem = segment.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
                if (segment.EndsWith(' ') || segment.EndsWith('.') || stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3])) throw new IOException("Reserved Windows file name refused");
            }
        }
        return path;
    }
    internal static void CheckAncestors(string path, bool leaf)
    {
        var ancestors = new List<string>();
        for (var current = new DirectoryInfo(Path.GetDirectoryName(path)!); current is not null; current = current.Parent) ancestors.Add(current.FullName);
        // Audit root to leaf so a linked ancestor is refused before looking up any of its children.
        foreach (string ancestor in ancestors.AsEnumerable().Reverse())
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(ancestor); }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) throw new IOException("Linked/non-directory parent refused");
        }
        if (leaf && (File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) throw new IOException("Linked/non-file input refused");
        // This preflight is not a handle-based defense against hostile concurrent path replacement.
    }
    internal static void CheckDataRoot(string path)
    {
        CheckAncestors(path,false);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return; }
        if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0) throw new IOException("Linked/non-directory data root refused");
    }
}
