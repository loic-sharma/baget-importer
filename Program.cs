using System;
using System.Collections.Generic;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MoreAsyncLINQ;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Plugins;

namespace BaGet.Import
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
        /// <param name="batchSize">The batch size to use for running in parallel.</param>
        /// <param name="apiKey">The API key to upload packages to the "to" source.</param>
        /// <param name="minBatchInterval">The minimum interval between batches</param>
        /// <param name="cancellationToken"></param>
        public static async Task Main(
            string fromSource,
            string toSource,
            string batchSize = "10",
            string apiKey = null,
            string minBatchInterval = "1000",
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
            var batch = int.Parse(batchSize);

            var pushResource = await toRepository.GetResourceAsync<PackageUpdateResource>(cancellationToken);


            var searchResource = await fromRepository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var packageResource = await fromRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            var packages = DiscoverPackagesAsync(searchResource, logger, cancellationToken).Batch(batch);
            var toPackageFinder = await toRepository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

            await foreach (var packageBatch in packages.WithCancellation(cancellationToken))
            {
                var startTime = DateTime.UtcNow;
                await Parallel.ForEachAsync(packageBatch, cancellationToken, 
                    async (x, token) => 
                        await ProcessPackage(
                            x, 
                            token, 
                            packageResource, 
                            pushResource, 
                            toPackageFinder,
                            cache, 
                            logger, 
                            apiKey));  
                
                var delay = int.Parse(minBatchInterval) - (int) (DateTime.UtcNow - startTime).TotalMilliseconds;
                Console.WriteLine($"Delaying for {delay}ms...");
                if (delay > 0)
                    await Task.Delay(delay, cancellationToken);
            }

            Console.WriteLine("Done!");
        }

        private static async Task ProcessPackage(PackageIdentity package, CancellationToken token,
            FindPackageByIdResource packageResource, PackageUpdateResource pushResource,
            FindPackageByIdResource findPackageByIdResource, SourceCacheContext cache,
            ILogger logger, string apiKey)
        {

            // See: https://github.com/dotnet/corefx/blob/master/src/Common/src/CoreLib/System/IO/Stream.cs#L35
            const int defaultCopyBufferSize = 81920;

            var exists =
                await findPackageByIdResource.DoesPackageExistAsync(package.Id, package.Version, cache, logger, token);
            if(exists)
            {
                Console.WriteLine($"Package already exists, skipping: {package.Id} {package.Version}");
                return;
            }
            Console.WriteLine($"Downloading package {package.Id} {package.Version.ToNormalizedString()}...");
            var packageStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite,
                FileShare.None, defaultCopyBufferSize);

            var path = packageStream.Name;
            await using (packageStream)
            {
                await packageResource.CopyNupkgToStreamAsync(package.Id, package.Version, packageStream, cache,
                    logger, token);
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
            catch (Exception e)
            {
                Console.WriteLine(
                    $"Unable to process package: {package.Id} {package.Version.ToNormalizedString()}\n{e.ToString()}");
            }
            finally
            {
                File.Delete(path);
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
