using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GaleWorkTimer.Packaging;

public sealed record PackageBuildResult(string Version, int FileCount, string Sha256);

public static class PackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static PackageBuildResult CreatePackage(string publishDirectory, string packagePath, string version)
    {
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Build < 0)
            throw new ArgumentException("Version must be a numeric version with at least three components.", nameof(version));

        var root = Path.GetFullPath(publishDirectory);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The publish directory does not exist.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The publish directory cannot be a symbolic or reparse link.");

        var files = EnumerateFilesWithoutLinks(root)
            .Select(path => new SourceFile(
                path,
                Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packageFiles = new List<ManifestFile>(files.Length);
        foreach (var file in files)
        {
            if (!InstallManager.IsSafePackagePath(file.RelativePath) ||
                file.RelativePath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                file.RelativePath.Equals(".gwt-package-manifest.json", StringComparison.OrdinalIgnoreCase) ||
                !names.Add(file.RelativePath))
                throw new InvalidDataException("The publish directory contains an unsafe or duplicate path.");
            using var stream = File.OpenRead(file.FullPath);
            packageFiles.Add(new ManifestFile(file.RelativePath,
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()));
        }

        var manifest = new PackageManifest(version, packageFiles);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var expectedHashes = packageFiles.ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal);
        var output = Path.GetFullPath(packagePath);
        var outputDirectory = Path.GetDirectoryName(output)!;
        if (output.StartsWith(root + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("The package output must be outside the publish directory.");
        Directory.CreateDirectory(outputDirectory);
        var temporary = output + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(archive, "manifest.json", manifestBytes);
                foreach (var source in files)
                {
                    var entry = archive.CreateEntry(source.RelativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                    entry.ExternalAttributes = 0;
                    using var input = File.OpenRead(source.FullPath);
                    using var destination = entry.Open();
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[81920];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        destination.Write(buffer, 0, count);
                        hash.AppendData(buffer, 0, count);
                    }
                    var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                    var expected = expectedHashes[source.RelativePath];
                    if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual),
                            Convert.FromHexString(expected)))
                        throw new IOException("A publish file changed while the package was being created.");
                }
            }
            using (var file = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                file.Flush(flushToDisk: true);

            _ = InstallManager.VerifyPackageArchive(temporary);
            File.Move(temporary, output, overwrite: true);
            return VerifyPackage(output);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static PackageBuildResult VerifyPackage(string packagePath) =>
        InstallManager.VerifyPackageArchive(packagePath);

    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        entry.ExternalAttributes = 0;
        using var stream = entry.Open();
        stream.Write(contents);
    }

    private static IEnumerable<string> EnumerateFilesWithoutLinks(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The publish directory contains a symbolic or reparse link.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(path);
                else
                    yield return path;
            }
        }
    }

    private sealed record SourceFile(string FullPath, string RelativePath);
    private sealed record PackageManifest(string Version, List<ManifestFile> Files);
    private sealed record ManifestFile(string Path, string Sha256);
}
