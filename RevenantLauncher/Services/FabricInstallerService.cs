using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RevenantLauncher.Services
{
    /// <summary>
    /// Установщик Fabric через официальный API meta.fabricmc.net
    /// </summary>
    public class FabricInstallerService
    {
        private const string FabricMetaBase = "https://meta.fabricmc.net/v2";
        private static readonly HttpClient _http = new();

        /// <summary>
        /// Устанавливает Fabric для указанной версии Minecraft
        /// </summary>
        /// <param name="mcVersion">Например "1.20.1"</param>
        /// <param name="loaderVersion">Версия загрузчика или null для последней</param>
        /// <param name="versionsDirectory">Путь к папке versions</param>
        /// <returns>ID установленной версии, например "fabric-loader-0.15.11-1.20.1"</returns>
        public async Task<string> InstallAsync(string mcVersion, string? loaderVersion, string versionsDirectory)
        {
            if (string.IsNullOrEmpty(loaderVersion))
                loaderVersion = await GetLatestLoaderVersionAsync();

            var versionId = $"fabric-loader-{loaderVersion}-{mcVersion}";
            var versionDir = Path.Combine(versionsDirectory, versionId);
            var versionJsonPath = Path.Combine(versionDir, $"{versionId}.json");

            if (File.Exists(versionJsonPath))
                return versionId;

            Directory.CreateDirectory(versionDir);

            var url = $"{FabricMetaBase}/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
            var json = await _http.GetStringAsync(url);

            await File.WriteAllTextAsync(versionJsonPath, json);

            return versionId;
        }

        /// <summary>Получить последнюю стабильную версию Fabric Loader</summary>
        public async Task<string> GetLatestLoaderVersionAsync()
        {
            var json = await _http.GetStringAsync($"{FabricMetaBase}/versions/loader");
            using var doc = JsonDocument.Parse(json);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("stable", out var stable) && stable.GetBoolean())
                    return item.GetProperty("version").GetString()!;
            }

            return doc.RootElement[0].GetProperty("version").GetString()!;
        }
    }
}