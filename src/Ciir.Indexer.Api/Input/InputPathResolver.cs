using BlogDoFT.Libs.ResultPattern;
using Ciir.Indexer.Application.Ports;

namespace Ciir.Indexer.Api.Input;

/// <inheritdoc cref="IInputResolver" />
public sealed class InputPathResolver : IInputResolver
{
    private readonly IndexerPathOptions _options;

    /// <summary>Initializes a new instance of the <see cref="InputPathResolver"/> class.</summary>
    /// <param name="options">The server's allow-list configuration (spec §42).</param>
    public InputPathResolver(IndexerPathOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Normalizes <paramref name="requestedPath"/>, resolves any symlink to its final target, and
    /// verifies the result is inside a configured allowed root, exists, and has the expected
    /// extension - in that order, so a symlink can never be used to read outside the allow-list.
    /// </summary>
    /// <param name="requestedPath">The raw <c>path</c> value from a <c>POST /api/indexations</c> request body.</param>
    /// <returns>
    /// The resolved absolute path on success; on failure, a <see cref="Failure"/> whose <c>Code</c>
    /// encodes the HTTP status to report (<c>400</c> for a missing/invalid/wrong-extension path,
    /// <c>403</c> for a path outside every allowed root, <c>404</c> for a path that doesn't exist).
    /// </returns>
    public Result<string> ResolveAndValidate(string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            return Result<string>.FromFailure(new Failure("400-path-required", "The 'path' field is required."));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(requestedPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<string>.FromFailure(new Failure("400-invalid-path", $"The path is not valid: {ex.Message}"));
        }

        // Resolve symlinks to their final real target before checking allowed roots - a symlink
        // inside an allowed root pointing outside it is a classic bypass (spec §42).
        var resolvedPath = ResolveSymlinkTarget(fullPath);

        if (!IsWithinAnyAllowedRoot(resolvedPath))
        {
            return Result<string>.FromFailure(
                new Failure("403-path-not-allowed", "The path is outside the configured allowed input roots."));
        }

        if (!File.Exists(resolvedPath))
        {
            return Result<string>.FromFailure(new Failure("404-path-not-found", "The file does not exist."));
        }

        if (!string.Equals(Path.GetExtension(resolvedPath), _options.ExpectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.FromFailure(
                new Failure("400-invalid-extension", $"Expected a '{_options.ExpectedExtension}' file."));
        }

        return Result<string>.FromSuccess(resolvedPath);
    }

    private static string ResolveSymlinkTarget(string path)
    {
        try
        {
            var linkTarget = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return linkTarget?.FullName ?? path;
        }
        catch (FileNotFoundException)
        {
            // The path doesn't exist at all - not this method's concern. The subsequent
            // File.Exists check reports that with its own dedicated "404-path-not-found" failure.
            return path;
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.Ordinal) || string.Equals(path, root, StringComparison.Ordinal);
    }

    private bool IsWithinAnyAllowedRoot(string path) =>
        _options.AllowedInputRoots.Any(root => IsWithinRoot(path, Path.GetFullPath(root)));
}
