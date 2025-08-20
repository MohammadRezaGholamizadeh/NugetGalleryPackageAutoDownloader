using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

class Program
{
    static async Task Main(string[] args)
    {
        string rootFolder = "DownloadedPackages";
        Directory.CreateDirectory(rootFolder);

        var sourceRepository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var searchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>();
        var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>();

        string searchTerm = "microsoft";
        int maxResults = 1000;

        var searchResults = await searchResource.SearchAsync(
            searchTerm,
            new SearchFilter(includePrerelease: true),
            skip: 0,
            take: maxResults,
            NullLogger.Instance,
            CancellationToken.None);

        foreach (var package in searchResults)
        {
            var existingDirectory =
                Directory.GetDirectories("D:\\Neximus.DownloadNugetPackages\\Neximus.DownloadNugetAllPackages\\Neximus.DownloadNugetAllPackages\\bin\\Debug\\net8.0\\DownloadedPackages");

            if (existingDirectory.Contains(package.Identity.Id.ToLower()))
                return;

            var versions = await package.GetVersionsAsync();

            var supportedVersions = versions
                .ToList()
                .GroupBy(_ => _.Version.Major)
                .Select(_ => _.Last());

            if (supportedVersions.Any())
            {
                string packageFolder = Path.Combine(rootFolder, package.Identity.Id.ToLower());
                Directory.CreateDirectory(packageFolder);

                foreach (var version in supportedVersions)
                {
                    Console.WriteLine($"Downloading {package.Identity.Id} - Version: {version.Version}");

                    var packageIdentity = new NuGet.Packaging.Core.PackageIdentity(package.Identity.Id, version.Version);

                    var downloadResult = await downloadResource.GetDownloadResourceResultAsync(
                        packageIdentity,
                        new PackageDownloadContext(new SourceCacheContext()),
                        SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(null)),
                        NullLogger.Instance,
                        CancellationToken.None);

                    if (downloadResult.Status == DownloadResourceResultStatus.Available)
                    {
                        string packageFileName = $"{package.Identity.Id}.{version.Version}.nupkg";
                        string packagePath = Path.Combine(packageFolder, packageFileName);

                        using (var fileStream = File.Create(packagePath))
                        {
                            await downloadResult.PackageStream.CopyToAsync(fileStream);
                        }

                        Console.WriteLine($"Downloaded {packageFileName} to {packageFolder}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to download {package.Identity.Id} - Version: {version.Version}");
                    }
                }
            }
        }

        Console.WriteLine("Done!");
    }
}
