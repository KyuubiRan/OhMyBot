using OhMyLib.Attributes;

namespace OhMyLib.Services;

[Component(Scope = ComponentAttribute.LifetimeScope.Singleton)]
public class CacheFileService
{
    public readonly DirectoryInfo CacheDirectory = new(Path.Combine(AppContext.BaseDirectory, "cache"));

    public enum OnConflict
    {
        Throw,
        Overwrite,
        Ignore,
    }

    public FileInfo MakeFileInfo(string? fileName = null, OnConflict conflict = OnConflict.Throw)
    {
        if (!CacheDirectory.Exists)
            CacheDirectory.Create();

        var file = Path.Combine(CacheDirectory.FullName, fileName ?? Guid.NewGuid().ToString("N"));

        if (!File.Exists(file))
            return new FileInfo(file);

        switch (conflict)
        {
            case OnConflict.Throw:
                throw new IOException("Cache file already exists: " + file);
            case OnConflict.Overwrite:
                File.Delete(file);
                return new FileInfo(file);
            case OnConflict.Ignore:
                return new FileInfo(file);
            default:
                throw new ArgumentOutOfRangeException(nameof(conflict), conflict, null);
        }
    }

    public FileInfo GetCachedFileInfo(string fileName)
    {
        var file = Path.Combine(CacheDirectory.FullName, fileName);
        return new FileInfo(file);
    }
}