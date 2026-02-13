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
        public float ProgressPercentage { get; private set; } = 0;
        public bool Downloading => _downloadingCount > 0;
        int _downloadingCount = 0;
        int _downloadedCount = 0;
        readonly HashSet<CefPkgVersion> _downloadingVersions = [];
        public IReadOnlyCollection<CefPkgVersion> DownloadingVersions => _downloadingVersions;
        readonly IProgress<float> _progress;
        public DownloadService()
        {
            _progress = new Progress<float>(val => ProgressPercentage = val);
        }
        public bool CheckCefLib(CefPkgVersion version)
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
        public void Delete(CefPkgVersion version)
        {
            var isUsing = CefService.LibLoadStarted && version == CefService.CurrentVersion;
            var isDefault = version == CefService.DefaultVersion;
            if (isUsing || isDefault) return;
            try
            {
                Directory.Delete(CefService.GetCefSharpFolder(version), true);
            }
            catch { }
        }
        public async Task Download(CefPkgVersion cefPkgVersion)
        {
            WebPeeperModule.Logger.Debug($"DownloadService.Download: try download if didnt download.");
            try
            {
                var downloaded = CheckCefLib(cefPkgVersion);
                var downloading = _downloadingVersions.Contains(cefPkgVersion);
                if (downloaded || downloading) return;
                WebPeeperModule.Logger.Debug($"DownloadService.Download: downloading cef {cefPkgVersion}.");
                if (_downloadingCount == 0) _progress.Report(0);
                using var client = new HttpClient();
                // https://learn.microsoft.com/en-us/nuget/api/overview#service-index
                var serviceIndexJson = await client.GetStringAsync("https://api.nuget.org/v3/index.json");
                var serviceIndex = JsonSerializer.Deserialize<NugetIndex>(serviceIndexJson);
                // https://learn.microsoft.com/en-us/nuget/api/package-base-address-resource
                var baseAddressResource = serviceIndex.Resources.First(r => r.Type?.Contains("PackageBaseAddress") == true);
                string nugetHostUrl = baseAddressResource.Id;

                Package[] packages = [
                    new("CefSharp.OffScreen", version: cefPkgVersion.CefSharp, files: ["lib/net462/"]),
                new("CefSharp.Common", version: cefPkgVersion.CefSharp, files:["CefSharp/x64/","lib/net462/"]),
                new("chromiumembeddedframework.runtime.win-x64", version: cefPkgVersion.Cef, files: ["CEF/win-x64/","runtimes/win-x64/native/"])
                    ];
                _downloadingCount += packages.Length;
                _downloadingVersions.Add(cefPkgVersion);
                CefVersionSettingView.UpdateView?.Invoke();
                foreach (var package in packages)
                {
                    var packageLowerID = package.Name.ToLowerInvariant();
                    string nupkgUrl = $"{nugetHostUrl}{packageLowerID}/{package.Version}/{packageLowerID}.{package.Version}.nupkg";
                    using var nupkgUrlResp = await client.GetAsync(nupkgUrl, HttpCompletionOption.ResponseHeadersRead);

                    var nupkgFileSize = (int)(nupkgUrlResp.Content.Headers.ContentLength ?? 0);
                    using var nupkgFileStream = await nupkgUrlResp.Content.ReadAsStreamAsync();

                    using MemoryStream downloadedStream = new();
                    _ = nupkgFileStream.CopyToAsync(downloadedStream);
                    while (downloadedStream.Length < nupkgFileSize)
                    {
                        await Task.Delay(50);
                        var percentage = (((float)downloadedStream.Length / nupkgFileSize) + _downloadedCount) / packages.Length;
                        if (percentage < 1) _progress.Report(percentage); // 100% after extract all files
                    }
                    using var zip = new ZipArchive(downloadedStream, ZipArchiveMode.Read);
                    foreach (var entry in zip.Entries)
                    {
                        if (_exludeExtractExts.Contains(Path.GetExtension(entry.Name).ToLower())) continue;
                        foreach (var path in package.PendingFiles.ToArray())
                        {
                            var isTarget = entry.FullName.IndexOf(path) == 0;
                            if (isTarget)
                            {
                                await Task.Run(() =>
                                {
                                    string destination = Path.Combine(CefService.GetCefSharpFolder(cefPkgVersion), entry.FullName.Replace(path, ""));
                                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                    using var stream = entry.Open();
                                    using var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write);
                                    stream.CopyTo(fileStream);
                                });
                            }
                        }
                    }
                    _downloadedCount += 1;
                }
                if (_downloadedCount == _downloadingCount)
                {
                    _downloadedCount = 0;
                    _downloadingCount = 0;
                    _downloadingVersions.Clear();
                    _progress.Report(1);

                    WebPeeperModule.Logger.Debug($"DownloadService.Download: all end.");
                }
            }
            catch (Exception ex)
            {
                WebPeeperModule.Logger.Error(ex.Message);
                throw;
            }
        }
    }
    internal class Package(string name, Version version, string[] files)
    {
        public readonly string Name = name;
        public readonly Version Version = version;
        public readonly string[] Files = files;
        public readonly List<string> PendingFiles = [.. files];
    }
    internal class NugetIndex
    {
        [JsonPropertyName("version")]
        public string Version { get; set; }
        [JsonPropertyName("resources")]
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
