using Cichlids.Etl.Media;

namespace Cichlids.Etl.Tests;

public sealed class LegacyImageLocatorTests
{
    [Fact]
    public void ResolvesADirectPathThatExists()
    {
        var root = CreateRoot();
        try
        {
            var directory = Path.Combine(root, "user_pics", "42");
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, "photo.jpg");
            File.WriteAllBytes(filePath, [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "user_pics/42/photo.jpg");

            Assert.Equal(filePath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReturnsNullForADirectPathThatDoesNotExist()
    {
        var root = CreateRoot();
        try
        {
            var resolved = LegacyImageLocator.Resolve(root, "user_pics/42/missing.jpg");

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesABareFilenameDirectlyUnderUserPics()
    {
        var root = CreateRoot();
        try
        {
            var userPicsDirectory = Path.Combine(root, "user_pics");
            Directory.CreateDirectory(userPicsDirectory);
            var filePath = Path.Combine(userPicsDirectory, "bare.jpg");
            File.WriteAllBytes(filePath, [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "bare.jpg");

            Assert.Equal(filePath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesABareFilenameOnlyReachableThroughTheRecursiveFallbackSearch()
    {
        var root = CreateRoot();
        try
        {
            var nestedDirectory = Path.Combine(root, "user_pics", "77");
            Directory.CreateDirectory(nestedDirectory);
            var filePath = Path.Combine(nestedDirectory, "nested.jpg");
            File.WriteAllBytes(filePath, [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "nested.jpg");

            Assert.Equal(filePath, resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReturnsNullForABareFilenameThatExistsNowhere()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "user_pics"));

            var resolved = LegacyImageLocator.Resolve(root, "nowhere.jpg");

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "legacy-image-locator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
