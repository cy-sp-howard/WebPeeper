using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BhModule.WebPeeper
{
    internal class DownloadService
    {
        static CefService CefService => WebPeeperModule.Instance.CefService;
        static readonly string[] _exludeExtractExts = [".pdb", ".xml"];
        static public float ProgressPercentage { get; private set; } = 0;
        static public int DownloadingCount { get; private set; } = 0;
        static public int DownloadedCount { get; private set; } = 0;
        readonly IProgress<float> progress = new Progress<float>(val => ProgressPercentage = val);
        public void Load()
        {
            var downloaded = CheckCefLib(CefService.CurrentVersion);
            if (!downloaded) _ = Download();
        }
        public void Unload()
        {

        }
        bool CheckCefLib(CefVersion version)
        {
            if (version == CefService.DefaultVersion) return true;
            string[] checkList = [
                "CefSharp.OffScreen.dll",
                "CefSharp.dll",
                "CefSharp.Core.dll",
                "CefSharp.Core.Runtime.dll",
                "CefSharp.BrowserSubprocess.exe",
                "CefSharp.BrowserSubprocess.Core.dll",
                "libcef.dll"
                ];

            foreach (var fileName in checkList)
            {
                var exist = File.Exists(Path.Combine(CefService.GetCefSharpFolder(version), fileName));
                if (!exist) return false;
            }
            return true;
        }
        public async Task Download()
        {
            if (DownloadingCount == 0) ProgressPercentage = 0;
            using var client = new HttpClient();
            // https://learn.microsoft.com/en-us/nuget/api/overview#service-index
            var serviceIndexJson = await client.GetStringAsync("https://api.nuget.org/v3/index.json");
            var serviceIndex = JsonSerializer.Deserialize<NugetIndex>(serviceIndexJson);
            // https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource
            var baseAddressResource = serviceIndex.Resources.First(r => r.Type?.Contains("PackageBaseAddress") == true);
            string nugetHostUrl = baseAddressResource.Id;


            Package[] packages = [
                new("CefSharp.OffScreen", CefService.CurrentVersion.CefSharp.ToString(), ["lib/net462/"]),
                new("CefSharp.Common", CefService.CurrentVersion.CefSharp.ToString(), ["CefSharp/x64/","lib/net462/"]),
                new("chromiumembeddedframework.runtime.win-x64", CefService.CurrentVersion.Cef.ToString(), ["CEF/win-x64/","runtimes/win-x64/native/"])
                ];
            DownloadingCount += packages.Length;
            foreach (var package in packages)
            {
                var packageLowerID = package.Name.ToLowerInvariant();
                string nupkgUrl = $"{nugetHostUrl}{packageLowerID}/{package.Version}/{packageLowerID}.{package.Version}.nupkg";
                using var nupkgUrlResp = await client.GetAsync(nupkgUrl, HttpCompletionOption.ResponseHeadersRead);

                var nupkgFileSize = (int)(nupkgUrlResp.Content.Headers.ContentLength ?? 0);
                using var nupkgFileStream = await nupkgUrlResp.Content.ReadAsStreamAsync();

                using MemoryStream downloaded = new MemoryStream();
                _ = nupkgFileStream.CopyToAsync(downloaded);
                while (downloaded.Length != nupkgFileSize)
                {
                    await Task.Delay(50);
                    var a = (((float)downloaded.Length / nupkgFileSize) + DownloadedCount) / packages.Length;
                    progress.Report(a);
                    //Console.WriteLine($"normal: {a}");
                }

                using var zip = new ZipArchive(downloaded, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    if (_exludeExtractExts.Contains(Path.GetExtension(entry.Name).ToLower())) continue;
                    foreach (var path in package.PendingFiles.ToArray())
                    {
                        var isTarget = entry.FullName.IndexOf(path) == 0;
                        if (isTarget)
                        {
                            string dest = Path.Combine(CefService._cefSharpFolder, Path.GetRelativePath(path, entry.FullName));

                            Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? "");
                            entry.ExtractToFile(dest, true);
                        }
                    }
                }
                DownloadedCount += 1;
            }
            if (DownloadedCount == DownloadingCount)
            {
                DownloadedCount = 0;
                DownloadingCount = 0;
            }
        }
    }
    internal class Package(string name, string version, string[] files)
    {
        public readonly string Name = name;
        public readonly string Version = version;
        public readonly string[] Files = files;
        public readonly List<string> PendingFiles = [.. files];
    }
    internal class NugetIndex
    {
        public string Version { get; set; }
        public List<NugetResource> Resources { get; set; }
    }
    internal class NugetResource
    {
        [JsonPropertyName("@id")]
        public string Id { get; set; }
        [JsonPropertyName("@type")]
        public string Type { get; set; }
        public string Comment { get; set; }
    }
}
