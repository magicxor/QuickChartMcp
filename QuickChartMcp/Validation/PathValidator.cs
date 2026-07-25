namespace QuickChartMcp.Validation;

/// <summary>
/// Pure (no-IO) validation helpers for output paths. Every failure throws a
/// <see cref="PathValidationException"/> whose message explains exactly what is wrong so
/// the calling agent can correct the request. Adapted from the Crawl4AiMcp path validator.
/// <para>
/// The rules are deliberately <b>platform-independent</b>: the server may run on Linux or
/// Windows, and a written chart's name may travel onward to machines with a different OS.
/// The validation therefore does not depend on the current OS
/// (unlike <see cref="Path.GetInvalidFileNameChars"/> / <see cref="Path.IsPathFullyQualified"/>,
/// which behave differently on Linux and Windows) so it yields the same verdict everywhere,
/// including on CI.
/// </para>
/// </summary>
public static class PathValidator
{
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>
    /// Characters that are unsafe in a file name (or a path segment) on any platform this tool
    /// touches. It is the Windows-invalid set — control characters plus
    /// <c>&lt; &gt; : " / \ | ? *</c> — which is a superset of the POSIX one (just <c>/</c>),
    /// so a name that passes here stays valid on both Linux and Windows.
    /// Shared with <see cref="IO.ArtifactWriter"/> so derived names are sanitised the same way.
    /// </summary>
    internal static readonly char[] InvalidNameChars =
        [.. Enumerable.Range(0, 32).Select(static i => (char)i), '<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    /// <summary>
    /// Names reserved by Windows regardless of extension (e.g. "CON", "CON.txt"). Rejected so a
    /// file created on Linux does not become un-saveable once it reaches a Windows machine.
    /// </summary>
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Validates a bare file name: non-empty, not made up solely of dots, free of characters that
    /// are unsafe on any target platform (which also rules out directory separators), not a
    /// reserved Windows device name, and not ending in a space or dot (which Windows silently
    /// strips), and equal to its own leaf name (no directory component smuggled in).
    /// </summary>
    public static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new PathValidationException("fileName must not be empty.");
        }

        if (fileName.Trim('.').Length == 0)
        {
            throw new PathValidationException(
                $"fileName '{fileName}' must be a real file name, not '.', '..' or a string of only dots.");
        }

        if (fileName.IndexOfAny(InvalidNameChars) >= 0)
        {
            throw new PathValidationException(
                $"fileName '{fileName}' contains a path separator or a character that is not allowed in a file " +
                "name on Windows (any of < > : \\\" / \\ | ? * or a control character); it must be a " +
                "single portable file name (for example 'report').");
        }

        if (fileName[^1] is '.' or ' ')
        {
            throw new PathValidationException(
                $"fileName '{fileName}' must not end with a space or a dot; Windows silently trims those, which " +
                "would rename the file or cause a collision.");
        }

        var stem = fileName.Split('.', 2)[0];
        if (ReservedDeviceNames.Contains(stem))
        {
            throw new PathValidationException(
                $"fileName '{fileName}' uses a name reserved by Windows ('{stem}'); choose a different name.");
        }
    }

    /// <summary>
    /// Validates a directory argument: it must be an absolute (rooted) path — in POSIX form
    /// ('/srv/out'), Windows drive form ('C:\data\out') or UNC form ('\\server\share') — contain
    /// no unsafe characters, and contain no empty, "."/".." or all-dots segment. The raw string is
    /// validated as-is; it is intentionally never normalized with <see cref="Path.GetFullPath"/>,
    /// because normalization would silently collapse ".." instead of rejecting it. The check is
    /// OS-independent, so the same string is accepted or rejected identically on Linux and Windows.
    /// </summary>
    /// <param name="path">The directory path to validate.</param>
    /// <param name="argName">Argument name used in error messages (e.g. "outputDirectory").</param>
    public static void ValidateDirectory(string path, string argName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PathValidationException($"{argName} must not be empty.");
        }

        var body = StripAbsoluteRoot(path);
        if (body is null)
        {
            throw new PathValidationException(
                $"{argName} must be an absolute, rooted path (for example '/srv/out' or 'C:\\data\\out'). " +
                $"'{path}' is not rooted.");
        }

        // A single trailing separator is benign ('/srv/out/'); drop exactly one. A doubled trailing
        // separator survives as an empty segment below and is rejected.
        if (body.Length > 0 && Array.IndexOf(Separators, body[^1]) >= 0)
        {
            body = body[..^1];
        }

        if (body.Length == 0)
        {
            return; // Path is just the root, e.g. '/' or 'C:\'.
        }

        // Do NOT use RemoveEmptyEntries: an empty segment (a double separator) is trash we reject.
        foreach (var segment in body.Split(Separators))
        {
            if (segment.Length == 0)
            {
                throw new PathValidationException(
                    $"{argName} '{path}' must not contain empty path segments (e.g. from a double separator).");
            }

            if (segment.Trim('.').Length == 0)
            {
                throw new PathValidationException(
                    $"{argName} '{path}' must not contain '.', '..' or all-dots path segments.");
            }

            if (segment.IndexOfAny(InvalidNameChars) >= 0)
            {
                throw new PathValidationException(
                    $"{argName} '{path}' contains an invalid character in segment '{segment}'.");
            }
        }
    }

    /// <summary>
    /// If <paramref name="path"/> is an absolute path in a recognised form, returns the portion
    /// after its root (which may be empty for a bare root such as '/' or 'C:\'). Returns
    /// <see langword="null"/> for a relative or drive-relative path ('out', 'C:', 'C:rel', '\rel').
    /// </summary>
    private static string? StripAbsoluteRoot(string path)
    {
        // Windows drive-absolute: 'X:\...' or 'X:/...'. A bare 'X:' or 'X:rel' is drive-relative.
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            && Array.IndexOf(Separators, path[2]) >= 0)
        {
            return path[3..];
        }

        // UNC: '\\server\share\...'. Strip the leading '\\'; server and share validate as segments.
        if (path.Length >= 2 && path[0] == '\\' && path[1] == '\\')
        {
            return path[2..];
        }

        // POSIX-absolute: '/srv/out'. A single leading '\' is deliberately NOT accepted — on Windows
        // that is a drive-relative path, so treating it as absolute would be misleading.
        if (path[0] == '/')
        {
            return path[1..];
        }

        return null;
    }
}
