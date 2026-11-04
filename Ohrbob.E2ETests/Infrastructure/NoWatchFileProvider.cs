using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Ohrbob.E2ETests.Infrastructure;

/// <summary>
/// Delegates file reads but never registers OS file watchers. The E2E run builds dozens of
/// hosts in one process, and the asp-append-version tag helper otherwise exhausts the
/// per-user inotify instance limit.
/// </summary>
public sealed class NoWatchFileProvider(IFileProvider inner) : IFileProvider
{
    public IDirectoryContents GetDirectoryContents(string subpath) => inner.GetDirectoryContents(subpath);

    public IFileInfo GetFileInfo(string subpath) => inner.GetFileInfo(subpath);

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}
