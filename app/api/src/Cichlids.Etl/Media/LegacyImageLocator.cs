namespace Cichlids.Etl.Media;

/// <summary>
/// Resolves a legacy picture's raw <c>user_cichlids_pictures.image</c> path to the physical file
/// under the legacy image tree it names. Almost every legacy path already carries its directory
/// (<c>user_pics/&lt;uid&gt;/&lt;file&gt;</c>); a bare filename with no directory at all only ever
/// meant its upload never recorded one, so that case falls back to a directory search instead of
/// reporting the picture missing outright.
/// </summary>
public static class LegacyImageLocator
{
    /// <summary>
    /// Returns the absolute path to the legacy image file, or null when no file matching
    /// <paramref name="legacyImagePath"/> exists anywhere under <paramref name="legacyImagesRoot"/>.
    /// </summary>
    public static string? Resolve(string legacyImagesRoot, string legacyImagePath)
    {
        if (legacyImagePath.Contains('/'))
        {
            var direct = Path.Combine(legacyImagesRoot, legacyImagePath);
            return File.Exists(direct) ? direct : null;
        }

        var underUserPics = Path.Combine(legacyImagesRoot, "user_pics", legacyImagePath);
        if (File.Exists(underUserPics))
        {
            return underUserPics;
        }

        var underRoot = Path.Combine(legacyImagesRoot, legacyImagePath);
        if (File.Exists(underRoot))
        {
            return underRoot;
        }

        // Last resort for a bare filename: scan every per-user directory under user_pics/ for an
        // exact filename match. Only reached for the rare legacy row whose upload never recorded a
        // directory at all, so paying for a directory walk here is acceptable.
        var userPicsDir = Path.Combine(legacyImagesRoot, "user_pics");
        if (!Directory.Exists(userPicsDir))
        {
            return null;
        }

        foreach (var candidate in Directory.EnumerateFiles(userPicsDir, legacyImagePath, SearchOption.AllDirectories))
        {
            return candidate;
        }

        return null;
    }
}
