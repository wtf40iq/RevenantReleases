using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class ModrinthService
    {
        private static readonly Lazy<ModrinthService> _instance = new(() => new ModrinthService());
        public static ModrinthService Instance => _instance.Value;

        private const string ApiBase = "https://api.modrinth.com/v2";
        private readonly HttpClient _http;

        // Кеш результатов поиска (5 минут)
        private readonly ConcurrentDictionary<string, (List<ModrinthProject> results, int total, DateTime cached)> _searchCache = new();
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

        // Кеш проектов
        private readonly ConcurrentDictionary<string, (ModrinthProjectDetails details, DateTime cached)> _projectCache = new();

        // Кеш версий проектов
        private readonly ConcurrentDictionary<string, (List<ModrinthVersion> versions, DateTime cached)> _versionsCache = new();

        private ModrinthService()
        {
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RevenantLauncher", "1.0"));
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<ModrinthProject>> SearchModsAsync(
            string query, string? mcVersion = null, string? loader = null, int limit = 30)
        {
            var (results, _) = await SearchModsWithPaginationAsync(query, mcVersion, loader, limit, 0);
            return results;
        }

        public async Task<(List<ModrinthProject> results, int totalHits)> SearchModsWithPaginationAsync(
            string query, string? mcVersion = null, string? loader = null,
            int limit = 30, int offset = 0, string projectType = "mod")
        {
            var cacheKey = $"{projectType}|{query}|{mcVersion}|{loader}|{limit}|{offset}";

            // Проверяем кеш
            if (_searchCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.cached < CacheLifetime)
                    return (cached.results, cached.total);
            }

            var facets = new List<string> { $"[\"project_type:{projectType}\"]" };
            if (!string.IsNullOrWhiteSpace(mcVersion))
                facets.Add($"[\"versions:{mcVersion}\"]");
            if (!string.IsNullOrWhiteSpace(loader) && projectType == "mod")
                facets.Add($"[\"categories:{loader.ToLower()}\"]");

            var facetsStr = "[" + string.Join(",", facets) + "]";
            var url = $"{ApiBase}/search?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}&facets={Uri.EscapeDataString(facetsStr)}";

            var result = new List<ModrinthProject>();
            int totalHits = 0;

            try
            {
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("total_hits", out var th))
                    totalHits = th.GetInt32();

                if (!doc.RootElement.TryGetProperty("hits", out var hits))
                    return (result, totalHits);

                foreach (var item in hits.EnumerateArray())
                {
                    var project = new ModrinthProject
                    {
                        ProjectId = GetString(item, "project_id"),
                        Slug = GetString(item, "slug"),
                        Title = GetString(item, "title"),
                        Description = GetString(item, "description"),
                        IconUrl = GetString(item, "icon_url"),
                        Author = GetString(item, "author"),
                        Downloads = GetInt(item, "downloads"),
                        Followers = GetInt(item, "follows"),
                        LatestVersion = GetString(item, "latest_version")
                    };

                    if (item.TryGetProperty("categories", out var cats))
                        foreach (var c in cats.EnumerateArray())
                            project.Categories.Add(c.GetString() ?? "");

                    if (item.TryGetProperty("display_categories", out var dcats))
                        foreach (var c in dcats.EnumerateArray())
                            project.Loaders.Add(c.GetString() ?? "");

                    result.Add(project);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] Search failed: {ex.Message}");
            }

            _searchCache[cacheKey] = (result, totalHits, DateTime.UtcNow);
            return (result, totalHits);
        }

        public async Task<ModrinthProjectDetails?> GetProjectDetailsAsync(string projectIdOrSlug)
        {
            // Проверяем кеш
            if (_projectCache.TryGetValue(projectIdOrSlug, out var cached))
            {
                if (DateTime.UtcNow - cached.cached < CacheLifetime)
                    return cached.details;
            }

            try
            {
                var url = $"{ApiBase}/project/{projectIdOrSlug}";
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var details = new ModrinthProjectDetails
                {
                    ProjectId = GetString(root, "id"),
                    Slug = GetString(root, "slug"),
                    Title = GetString(root, "title"),
                    Description = GetString(root, "description"),
                    Body = GetString(root, "body"),
                    IconUrl = GetString(root, "icon_url"),
                    Downloads = GetInt(root, "downloads"),
                    Followers = GetInt(root, "followers"),
                    ClientSide = GetString(root, "client_side"),
                    ServerSide = GetString(root, "server_side"),
                    ProjectType = GetString(root, "project_type"),
                    License = ParseLicense(root),
                    IssuesUrl = GetString(root, "issues_url"),
                    SourceUrl = GetString(root, "source_url"),
                    WikiUrl = GetString(root, "wiki_url"),
                    DiscordUrl = GetString(root, "discord_url")
                };

                if (root.TryGetProperty("categories", out var cats))
                    foreach (var c in cats.EnumerateArray())
                        details.Categories.Add(c.GetString() ?? "");

                if (root.TryGetProperty("loaders", out var loaders))
                    foreach (var l in loaders.EnumerateArray())
                        details.Loaders.Add(l.GetString() ?? "");

                if (root.TryGetProperty("gallery", out var gallery))
                    foreach (var g in gallery.EnumerateArray())
                        details.Gallery.Add(new ModrinthGalleryImage
                        {
                            Url = GetString(g, "url"),
                            Title = GetString(g, "title"),
                            Description = GetString(g, "description")
                        });


                _projectCache[projectIdOrSlug] = (details, DateTime.UtcNow);
                return details;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] GetProjectDetails failed: {ex.Message}");
                return null;
            }
        }

        public async Task<List<ModrinthProject>> GetProjectsBatchAsync(IEnumerable<string> projectIds)
        {
            var ids = projectIds.Distinct().ToList();
            if (ids.Count == 0) return new();

            var result = new List<ModrinthProject>();
            try
            {
                var idsJson = "[" + string.Join(",", ids.Select(id => $"\"{id}\"")) + "]";
                var url = $"{ApiBase}/projects?ids={Uri.EscapeDataString(idsJson)}";
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                foreach (var item in doc.RootElement.EnumerateArray())
                    result.Add(new ModrinthProject
                    {
                        ProjectId = GetString(item, "id"),
                        Slug = GetString(item, "slug"),
                        Title = GetString(item, "title"),
                        Description = GetString(item, "description"),
                        IconUrl = GetString(item, "icon_url")
                    });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] GetProjectsBatch failed: {ex.Message}");
            }
            return result;
        }

        public async Task<List<ModrinthVersion>> GetProjectVersionsAsync(
            string projectId, string? mcVersion = null, string? loader = null)
        {
            var cacheKey = $"{projectId}|{mcVersion}|{loader}";

            if (_versionsCache.TryGetValue(cacheKey, out var cached))
            {
                if (DateTime.UtcNow - cached.cached < CacheLifetime)
                    return cached.versions;
            }

            var url = $"{ApiBase}/project/{projectId}/version";

            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(mcVersion))
                queryParams.Add($"game_versions=[\"{mcVersion}\"]");
            if (!string.IsNullOrEmpty(loader))
                queryParams.Add($"loaders=[\"{loader.ToLower()}\"]");

            if (queryParams.Count > 0)
                url += "?" + string.Join("&", queryParams.Select(Uri.EscapeDataString));

            var result = new List<ModrinthVersion>();
            try
            {
                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                    result.Add(ParseVersion(item));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] Versions failed: {ex.Message}");
            }

            _versionsCache[cacheKey] = (result, DateTime.UtcNow);
            return result;
        }

        public async Task<ModrinthVersion?> GetVersionAsync(string versionId)
        {
            try
            {
                var json = await _http.GetStringAsync($"{ApiBase}/version/{versionId}");
                using var doc = JsonDocument.Parse(json);
                return ParseVersion(doc.RootElement);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] GetVersion failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>Пакетный поиск версий по SHA1-хэшам файлов (POST /version_files)</summary>
        public async Task<Dictionary<string, ModrinthVersion>> GetVersionsByHashesAsync(IEnumerable<string> hashes)
        {
            var result = new Dictionary<string, ModrinthVersion>(StringComparer.OrdinalIgnoreCase);
            var list = hashes.Distinct().ToList();
            if (list.Count == 0) return result;

            try
            {
                var body = JsonSerializer.Serialize(new { hashes = list, algorithm = "sha1" });
                var json = await PostJsonAsync($"{ApiBase}/version_files", body);
                if (json == null) return result;

                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = ParseVersion(prop.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] GetVersionsByHashes failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>Пакетная проверка обновлений по SHA1-хэшам (POST /version_files/update).
        /// Возвращает только те хэши, для которых доступна более новая версия.
        /// Пустые фильтры (loaders/gameVersions) не отправляются.</summary>
        public async Task<Dictionary<string, ModrinthVersion>> GetUpdatesByHashesAsync(
            IEnumerable<string> hashes, IEnumerable<string>? loaders, IEnumerable<string>? gameVersions)
        {
            var result = new Dictionary<string, ModrinthVersion>(StringComparer.OrdinalIgnoreCase);
            var list = hashes.Distinct().ToList();
            if (list.Count == 0) return result;

            try
            {
                var body = new Dictionary<string, object>
                {
                    ["hashes"] = list,
                    ["algorithm"] = "sha1"
                };

                var loaderList = loaders?.Where(l => !string.IsNullOrEmpty(l)).ToList();
                if (loaderList != null && loaderList.Count > 0)
                    body["loaders"] = loaderList;

                var gameList = gameVersions?.Where(g => !string.IsNullOrEmpty(g)).ToList();
                if (gameList != null && gameList.Count > 0)
                    body["game_versions"] = gameList;

                var json = await PostJsonAsync($"{ApiBase}/version_files/update",
                    JsonSerializer.Serialize(body));
                if (json == null) return result;

                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                    result[prop.Name] = ParseVersion(prop.Value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Modrinth] GetUpdatesByHashes failed: {ex.Message}");
            }
            return result;
        }

        private async Task<string?> PostJsonAsync(string url, string jsonBody)
        {
            using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(url, content);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[Modrinth] POST {url} failed: {(int)response.StatusCode}");
                return null;
            }
            return await response.Content.ReadAsStringAsync();
        }

        private static ModrinthVersion ParseVersion(JsonElement item)
        {
            var version = new ModrinthVersion
            {
                VersionId = GetString(item, "id"),
                ProjectId = GetString(item, "project_id"),
                VersionNumber = GetString(item, "version_number"),
                Name = GetString(item, "name"),
                VersionType = GetString(item, "version_type"),
                Downloads = GetInt(item, "downloads"),
                DatePublished = FormatDate(GetString(item, "date_published"))
            };

            if (item.TryGetProperty("game_versions", out var gv))
                foreach (var g in gv.EnumerateArray())
                    version.GameVersions.Add(g.GetString() ?? "");

            if (item.TryGetProperty("loaders", out var lo))
                foreach (var l in lo.EnumerateArray())
                    version.Loaders.Add(l.GetString() ?? "");

            if (item.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
            {
                var file = files[0];
                foreach (var f in files.EnumerateArray())
                    if (f.TryGetProperty("primary", out var primary) && primary.GetBoolean())
                    { file = f; break; }

                version.DownloadUrl = GetString(file, "url");
                version.FileName = GetString(file, "filename");
                version.FileSize = GetLong(file, "size");
            }

            if (item.TryGetProperty("dependencies", out var deps))
                foreach (var d in deps.EnumerateArray())
                    version.Dependencies.Add(new ModrinthDependency
                    {
                        VersionId = GetStringOrNull(d, "version_id"),
                        ProjectId = GetStringOrNull(d, "project_id"),
                        FileName = GetStringOrNull(d, "file_name"),
                        DependencyType = ParseDependencyType(GetString(d, "dependency_type"))
                    });

            return version;
        }

        private static ModDependencyType ParseDependencyType(string s) => s?.ToLower() switch
        {
            "required" => ModDependencyType.Required,
            "optional" => ModDependencyType.Optional,
            "incompatible" => ModDependencyType.Incompatible,
            "embedded" => ModDependencyType.Embedded,
            _ => ModDependencyType.Required
        };

        public async Task<System.IO.Stream> DownloadModAsync(string downloadUrl)
            => await _http.GetStreamAsync(downloadUrl);

        private static string ParseLicense(JsonElement root)
        {
            if (root.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object)
            {
                if (lic.TryGetProperty("name", out var name)) return name.GetString() ?? "";
                if (lic.TryGetProperty("id", out var id)) return id.GetString() ?? "";
            }
            return "";
        }

        private static string FormatDate(string iso)
        {
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            return iso;
        }

        private static string GetString(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        private static string? GetStringOrNull(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static int GetInt(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        private static long GetLong(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
    }
}