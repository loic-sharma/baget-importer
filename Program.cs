using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace BaGet.Importer
{
    /// <summary>
    /// Import packages from a NuGet server.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Import packages from a NuGet server.
        /// </summary>
        /// <param name="fromSource">The package source whose packages should be exported.</param>
        /// <param name="toSource">The package source that should import packages.</param>
        /// <param name="apiKey">The API key to upload packages to the "to" source.</param>
        /// <param name="cancellationToken"></param>
        public static async Task Main(
            string fromSource,
            string toSource,
            string apiKey = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(fromSource))
            {
                Console.WriteLine("The --from-source option is required.");
                return;
            }

            if (string.IsNullOrEmpty(toSource))
            {
                Console.WriteLine("The --to-source option is required.");
                return;
            }

            var logger = NullLogger.Instance;
            var cache = new SourceCacheContext();

            var fromRepository = Repository.Factory.GetCoreV3(fromSource);
            var toRepository = Repository.Factory.GetCoreV3(toSource);

            var pushResource = await toRepository.GetResourceAsync<PackageUpdateResource>();

            await foreach (var packageStream in FindPackagesAsync(fromRepository, cache, logger, cancellationToken))
            {
                string path;
                using (packageStream)
                {
                    path = packageStream.Name;
                }

                try
                {
                    Console.WriteLine($"Pushing package at path {path}...");

                    await pushResource.Push(
                        new[] { path },
                        symbolSource: null,
                        timeoutInSecond: 5 * 60,
                        disableBuffering: false,
                        getApiKey: packageSource => apiKey,
                        getSymbolApiKey: packageSource => null,
                        noServiceEndpoint: false,
                        skipDuplicate: true,
                        symbolPackageUpdateResource: null,
                        logger);

                }
                finally
                {
                    File.Delete(path);
                }
            }

            Console.WriteLine("Done!");
        }

        private static async IAsyncEnumerable<FileStream> FindPackagesAsync(
            SourceRepository repository,
            SourceCacheContext cache,
            ILogger logger,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            var searchResource = await repository.GetResourceAsync<PackageSearchResource>();
            var packageResource = await repository.GetResourceAsync<FindPackageByIdResource>();

            await foreach (var package in DiscoverPackagesAsync(searchResource, logger, cancellationToken))
            {
                Console.WriteLine($"Downloading package {package.Id} {package.Version.ToNormalizedString()}...");

                // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
                const int defaultCopyBufferSize = 81920;

                var packageStream = new FileStream(
                    Path.GetTempFileName(),
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    defaultCopyBufferSize);

                await packageResource.CopyNupkgToStreamAsync(
                    package.Id,
                    package.Version,
                    packageStream,
                    cache,
                    logger,
                    cancellationToken);

                yield return packageStream;
            }
        }

        private static async IAsyncEnumerable<PackageIdentity> DiscoverPackagesAsync(
            PackageSearchResource searchResource,
            ILogger logger,
            [EnumeratorCancellation]
            CancellationToken cancellationToken)
        {
            var skip = 0;
            var searchFilter = new SearchFilter(includePrerelease: true);

            bool done;
            do
            {
                var results = await searchResource.SearchAsync(
                    "",
                    searchFilter,
                    skip,
                    take: 100,
                    logger,
                    cancellationToken);

                var packages = results.ToList();

                done = !packages.Any();
                skip += packages.Count();

                foreach (var package in packages)
                {
                    var versions = await package.GetVersionsAsync();

                    foreach (var version in versions)
                    {
                        yield return new PackageIdentity(package.Identity.Id, version.Version);
                    }
                }
            }
            while (!done);
        }
    }
}
