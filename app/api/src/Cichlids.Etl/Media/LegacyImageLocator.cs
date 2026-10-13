namespace Cichlids.Etl.Media;

/// <summary>
/// Resolves a legacy picture's raw user_cichlids_pictures.image path to the physical file
/// under the legacy image tree it names. Almost every legacy path already carries its directory
/// (user_pics/uid/file); a bare filename with no directory at all only ever
/// meant its upload never recorded one, so that case falls back to a directory search instead of
/// reporting the picture missing outright. A path that leaves the image root after it is combined
/// with the root (absolute path, parent segments) is reported as missing. Bare filenames are
/// compared exactly; characters such as star and question mark have no wildcard meaning.
/// </summary>
public static class LegacyImageLocator
{
    /// <summary>
    /// Returns the absolute path to the legacy image file, or null when no file matching
    /// the given path exists under the given root.
    /// </summary>
    public static string? Resolve(string legacyImagesRoot, string legacyImagePath)
    {
        var fullRoot = Path.GetFullPath(legacyImagesRoot);

        if (legacyImagePath.Contains('/'))
        {
            return ExistingFileInside(fullRoot, Path.Combine(fullRoot, legacyImagePath));
        }

        if (legacyImagePath is "" or "." or "..")
        {
            return null;
        }

        var underUserPics = ExistingFileInside(fullRoot, Path.Combine(fullRoot, "user_pics", legacyImagePath));
        if (underUserPics is not null)
        {
            return underUserPics;
        }

        var underRoot = ExistingFileInside(fullRoot, Path.Combine(fullRoot, legacyImagePath));
        if (underRoot is not null)
        {
            return underRoot;
        }

        // Last resort for a bare filename: look into every per-user directory under user_pics/ for
        // a file with exactly that name. Only reached for the rare legacy row whose upload never
        // recorded a directory at all, so paying for a directory walk here is acceptable.
        var userPicsDir = Path.Combine(fullRoot, "user_pics");
        if (!Directory.Exists(userPicsDir))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(userPicsDir, "*", SearchOption.AllDirectories))
        {
            var candidate = Path.Combine(directory, legacyImagePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string? ExistingFileInside(string fullRoot, string candidate)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        var rootWithSeparator = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullCandidate.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            return null;
        }

        return File.Exists(fullCandidate) ? fullCandidate : null;
    }
}
