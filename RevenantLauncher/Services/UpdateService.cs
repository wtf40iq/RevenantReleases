using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace RevenantLauncher.Services
{
    public class UpdateInfo
    {
        public Version Version { get; set; } = new();
        public string DownloadUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    /// <summary>
    /// Автообновление лаунчера через GitHub Releases.
    /// В репозитории лежат ТОЛЬКО скомпилированные установщики (исходников там нет),
    /// поэтому код остаётся закрытым. Публичный репозиторий не требует токенов
    /// для скачивания (лимит GitHub API ~60 запросов/час с IP).
    /// </summary>
    public static class UpdateService
    {
        // Репозиторий с релизами (только exe, без кода). Замени ник при необходимости
        private const string UpdatesRepo = "wtf40iq/RevenantReleases";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

        static UpdateService()
        {
            // GitHub API требует User-Agent
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("RevenantLauncher");
        }

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 2, 1);

        /// <summary>Проверяет последнюю версию в релизах. null — обновлений нет или ошибка.</summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"https://api.github.com/repos/{UpdatesRepo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var ver = ParseVersion(tag);
                if (ver == null || ver <= CurrentVersion) return null;

                var url = "";
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            url = a.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(url)) return null;

                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new UpdateInfo { Version = ver, DownloadUrl = url, Notes = notes };
            }
            catch (Exception ex)
            {
                // Нет сети / нет релизов / 404 — молча пропускаем
                Console.WriteLine($"[Update] check failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Скачивает установщик во временную папку, возвращает путь к файлу.</summary>
        public static async Task<string?> DownloadInstallerAsync(UpdateInfo info, Action<double>? progress = null)
        {
            try
            {
                var dest = Path.Combine(Path.GetTempPath(), $"RevenantLauncher-{info.Version}-Setup.exe");

                using var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1;
                using var stream = await response.Content.ReadAsStreamAsync();
                await using var fs = File.Create(dest);

                var buffer = new byte[81920];
                long written = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, n);
                    written += n;
                    if (total > 0) progress?.Invoke((double)written / total);
                }

                return dest;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] download failed: {ex.Message}");
                return null;
            }
        }

        private static Version? ParseVersion(string tag)
        {
            var s = tag.Trim().TrimStart('v', 'V');
            var idx = s.IndexOf('-');
            if (idx > 0) s = s.Substring(0, idx);
            return Version.TryParse(s, out var v) ? v : null;
        }
    }
}
