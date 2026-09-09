using BlogDoFT.Libs.ResultPattern;

namespace Ciir.Indexer.Application.Ports;

/// <summary>
/// Validates a caller-supplied CIIR file path before any file is opened (spec §42). The path
/// comes from an untrusted API request body, so this must normalize it, resolve symlinks, and
/// confirm it falls within a configured allow-list before anything else touches the filesystem.
/// </summary>
public interface IInputResolver
{
    /// <summary>
    /// Validates <paramref name="requestedPath"/> and returns the normalized, safe-to-open path on
    /// success, or a <see cref="Failure"/> describing why the request was rejected.
    /// </summary>
    /// <param name="requestedPath">The raw, untrusted path from the request body.</param>
    Result<string> ResolveAndValidate(string requestedPath);
}
