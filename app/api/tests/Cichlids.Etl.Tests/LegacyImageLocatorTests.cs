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

    [Fact]
    public void ReportsAnAbsolutePathOutsideTheRootAsMissingEvenWhenTheFileExists()
    {
        var root = CreateRoot();
        var outsideRoot = CreateRoot();
        try
        {
            var outsideFile = Path.Combine(outsideRoot, "secret.jpg");
            File.WriteAllBytes(outsideFile, [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, outsideFile);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public void ReportsAParentTraversalLeavingTheRootAsMissingEvenWhenTheFileExists()
    {
        var parent = CreateRoot();
        try
        {
            var root = Path.Combine(parent, "images");
            Directory.CreateDirectory(Path.Combine(root, "user_pics"));
            File.WriteAllBytes(Path.Combine(parent, "outside.jpg"), [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "user_pics/../../outside.jpg");

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ResolvesAPathWithParentSegmentsThatStayInsideTheRoot()
    {
        var root = CreateRoot();
        try
        {
            var directory = Path.Combine(root, "user_pics", "42");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "photo.jpg"), [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "user_pics/42/../42/photo.jpg");

            Assert.NotNull(resolved);
            Assert.Equal(Path.Combine(directory, "photo.jpg"), Path.GetFullPath(resolved));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("*.jpg")]
    [InlineData("photo?.jpg")]
    [InlineData("pho*")]
    public void MatchesABareFilenameExactlyWithoutWildcardSemantics(string bareName)
    {
        var root = CreateRoot();
        try
        {
            var nestedDirectory = Path.Combine(root, "user_pics", "77");
            Directory.CreateDirectory(nestedDirectory);
            File.WriteAllBytes(Path.Combine(nestedDirectory, "photo1.jpg"), [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, bareName);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolvesABareFilenameContainingWildcardCharactersWhenAFileHasExactlyThatName()
    {
        var root = CreateRoot();
        try
        {
            var nestedDirectory = Path.Combine(root, "user_pics", "77");
            Directory.CreateDirectory(nestedDirectory);
            var filePath = Path.Combine(nestedDirectory, "what?.jpg");
            File.WriteAllBytes(filePath, [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(nestedDirectory, "whatx.jpg"), [1, 2, 3]);

            var resolved = LegacyImageLocator.Resolve(root, "what?.jpg");

            Assert.Equal(filePath, resolved);
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
