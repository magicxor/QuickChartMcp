using QuickChartMcp.Configuration;
using QuickChartMcp.Validation;

namespace QuickChartMcp.IO;

/// <summary>
/// Writes rendered charts to an agent-supplied output directory. This is the single
/// choke point for every file write: it enforces path validation (absolute/rooted, no
/// '.'/'..'/empty/all-dots segments, no invalid characters) and the configured output
/// allow-list before touching the filesystem, then handles filename derivation and
/// collision-safe naming. Adapted from the Crawl4AiMcp artifact writer.
/// </summary>
public sealed class ArtifactWriter
{
    private readonly PathPolicy _policy;

    public ArtifactWriter(PathPolicy policy) => _policy = policy;

    public readonly record struct WriteResult(string Path, long Bytes);

    /// <summary>
    /// Validates an output directory (syntax + allow-list) without writing anything.
    /// Tools call this up front so a bad/blocked directory fails before any network call.
    /// Throws <see cref="PathValidationException"/> on rejection.
    /// </summary>
    public void EnsureOutputDirectoryAllowed(string outputDirectory)
    {
        PathValidator.ValidateDirectory(outputDirectory, "outputDirectory");

        if (!_policy.IsOutputAllowed(outputDirectory))
        {
            throw new PathValidationException(_policy.HasOutputPatterns
                ? $"outputDirectory '{outputDirectory}' is not allowed: it does not match any configured " +
                  "QuickChart:AllowedOutputPatterns."
                : "outputDirectory is not allowed: no QuickChart:AllowedOutputPatterns are configured, so every " +
                  "output directory is blocked. Configure QuickChart:AllowedOutputPatterns to permit specific paths.");
        }
    }

    public async Task<WriteResult> WriteBytesAsync(
        string outputDirectory, string? fileName, string defaultBaseName, string extension, byte[] data,
        CancellationToken ct)
    {
        var path = ResolveOutputPath(outputDirectory, fileName, defaultBaseName, extension);
        await File.WriteAllBytesAsync(path, data, ct);
        return new WriteResult(path, data.LongLength);
    }

    /// <summary>
    /// Resolves an absolute, collision-free file path inside <paramref name="outputDirectory"/>,
    /// creating the directory if needed. Enforces the full path policy (validation + allow-list)
    /// and validates any supplied <paramref name="fileName"/> as a bare leaf name; otherwise
    /// <paramref name="defaultBaseName"/> is used. Existing files are never overwritten: on
    /// collision a numeric suffix (-1, -2, ...) is appended until a free name is found.
    /// Throws <see cref="PathValidationException"/> on rejection.
    /// </summary>
    public string ResolveOutputPath(string outputDirectory, string? fileName, string defaultBaseName, string extension)
    {
        // Guaranteed guard for every write, even if a caller forgot the up-front check.
        EnsureOutputDirectoryAllowed(outputDirectory);

        string baseName;
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            PathValidator.ValidateFileName(fileName);
            baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = defaultBaseName;
        }
        else
        {
            baseName = defaultBaseName;
        }

        baseName = Sanitize(baseName);

        Directory.CreateDirectory(outputDirectory);

        var candidate = Path.Combine(outputDirectory, baseName + extension);
        var counter = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(outputDirectory, $"{baseName}-{counter++}{extension}");

        return candidate;
    }

    private static string Sanitize(string name)
    {
        // Use the same portable invalid-char set as PathValidator (not the OS-specific
        // Path.GetInvalidFileNameChars(), which is permissive on Linux) so a derived name stays
        // safe on any machine the chart file ends up on.
        foreach (var invalid in PathValidator.InvalidNameChars)
            name = name.Replace(invalid, '-');

        name = name.Trim().Trim('.');
        return string.IsNullOrWhiteSpace(name) ? "chart" : name;
    }
}
