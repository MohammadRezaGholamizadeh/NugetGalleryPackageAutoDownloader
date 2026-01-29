using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {
        string rootFolder = @"D:\Downloaded Packages";
        Directory.CreateDirectory(rootFolder);

        //await DeleteHugePackages(rootFolder);

        await DownloadPackages(rootFolder);
    }


    private static async Task DeleteHugePackages(string rootFolder)
    {
        var allDirectories = Directory.GetDirectories(rootFolder);
        var clearedPackages = "";
        var failedDeletionPackages = "";

        foreach (var directory in allDirectories)
        {
            var allFiles =
                Directory.GetFiles(directory)
                         .Select(_ => new FileInfo(_))
                         .OrderByDescending(_ => _.Name);

            if (allFiles.Any(_ => _.Length > 20 * 1024 * 1024))
            {
                var filesPathThatMustDelete = allFiles.Skip(5);
                foreach (var file in filesPathThatMustDelete)
                {
                    try
                    {
                        File.Delete(file.FullName);
                        clearedPackages += directory + Environment.NewLine;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"File Deleted => {file.Name} .");
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    catch
                    {
                        failedDeletionPackages += directory + Environment.NewLine;
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Deletion Failed => {file.Name} .");
                        Console.ForegroundColor = ConsoleColor.White;
                    }

                }
            }
        }

        File.WriteAllText(Path.Combine(rootFolder, "ClearedPackages.txt"), clearedPackages);
        File.WriteAllText(Path.Combine(rootFolder, "FailedDeletionPackages.txt"), failedDeletionPackages);
    }


    private static async Task DownloadPackages(string rootFolder)
    {
        var sourceRepository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>();

        HttpClient client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("nuget-mirror/1.0");

        string[] searchTerms = new string[]
        {
            //"microsoft",
            //"system",
            //"extensions",
            //"aspnet",
            //"entityframework",
            //"json",
            //"log",
            //"http",
            //"security",
            //"identity",
            //"newtonsoft",
            //"serilog",
            //"dapper",
            //"automapper",
            //"mediatr",
            //"polly",
            //"blazor",
            //"xunit"
            "a"
        };

        string[] excludeList = new string[]
        {
            "cef.redist.x86",
            "cef.redist.x64",

        };

        int maxResults = 1000000;
        long minDownloads = 8000000;
        int batchSize = 100; // برای جلوگیری از JSON بزرگ

        foreach (var term in searchTerms)
        {
            Console.WriteLine($"\n=== Searching '{term}' ===");

            int skip = 0;
            int totalFetched = 0;

            while (totalFetched < maxResults)
            {
                string url = $"https://api-v2v3search-0.nuget.org/query?q={term}&frameworks=net%2Cnetcoreapp%2Cnetstandard&includeComputedFrameworks=true&frameworkFilterMode=all&prerelease=true&take={batchSize}&skip={skip}&sortBy=downloads";

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);

                var packages = doc.RootElement.GetProperty("data").EnumerateArray();

                int fetchedInThisBatch = 0;

                foreach (var pkg in packages)
                {
                    string packageId = pkg.GetProperty("id").GetString();
                    long downloadCount = pkg.GetProperty("totalDownloads").GetInt64();

                    if (excludeList.Any(_ => _.Contains(packageId)))
                        continue;
                    if (downloadCount < minDownloads)
                        continue;

                    string packageFolder = Path.Combine(rootFolder, packageId.ToLowerInvariant());
                    if (!Directory.Exists(packageFolder))
                    {
                        Directory.CreateDirectory(packageFolder);

                        var versionsJson = pkg.GetProperty("versions");
                        var selectedVersions = versionsJson.EnumerateArray()
                            .GroupBy(v => v.GetProperty("version").GetString().Split('.')[0])
                            .OrderByDescending(_ => _.First().GetProperty("version").GetString().Split('.')[0])
                            .Select(g => g.OrderByDescending(v => NuGetVersion.Parse(v.GetProperty("version").GetString())).First())
                            .Take(10)
                            .ToList();
                        var isbigsize = false;
                        var downloadedCount = 0;
                        foreach (var v in selectedVersions)
                        {
                            if (isbigsize && downloadedCount == 3)
                                continue;
                            var greatestDownloadedPackage = Directory.GetFiles(packageFolder).Select(_ => new FileInfo(_)).OrderByDescending(_ => _.Length).FirstOrDefault();
                            if (greatestDownloadedPackage != null)
                            {
                                if (greatestDownloadedPackage.Length > 20 * 1024 * 1024)
                                {
                                    isbigsize = true;
                                    downloadedCount += 1;
                                }
                            }

                            string versionStr = v.GetProperty("version").GetString();
                            try
                            {
                                Console.WriteLine($"Downloading {packageId} - {versionStr} - Downloads: {downloadCount}");

                                var identity = new PackageIdentity(packageId, NuGetVersion.Parse(versionStr));

                                var result = await downloadResource.GetDownloadResourceResultAsync(
                                    identity,
                                    new PackageDownloadContext(new SourceCacheContext()),
                                    SettingsUtility.GetGlobalPackagesFolder(Settings.LoadDefaultSettings(null)),
                                    NullLogger.Instance,
                                    CancellationToken.None);

                                if (result.Status == DownloadResourceResultStatus.Available)
                                {
                                    string fileName = $"{identity.Id}.{identity.Version}.nupkg";
                                    string filePath = Path.Combine(packageFolder, fileName);

                                    using var fs = File.Create(filePath);
                                    await result.PackageStream.CopyToAsync(fs);

                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"Downloaded {fileName}");
                                    Console.ResetColor();
                                }
                            }
                            catch
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"Failed: {packageId} - {versionStr}");
                                Console.ResetColor();
                            }
                        }
                    }
                    fetchedInThisBatch++;
                    totalFetched++;
                    if (totalFetched >= maxResults)
                        break;
                }

                if (fetchedInThisBatch == 0)
                    break; // دیگر بسته‌ای برای دریافت نیست

                skip += batchSize;
            }

            Console.WriteLine($"\nFinished search term '{term}'");
        }

        Console.WriteLine("\nAll done!");
    }
}
