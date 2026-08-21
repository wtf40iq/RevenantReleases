using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class ConfigService
    {
        private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
        public static ConfigService Instance => _instance.Value;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private bool _isLoaded = false;

        public LauncherData Data { get; private set; } = new();

        public bool IsLoaded => _isLoaded;

        private ConfigService() { }

        public async Task LoadAsync()
        {
            try
            {
                PathService.EnsureDirectoriesExist();

                if (!File.Exists(PathService.ConfigFile))
                {
                    Data = CreateDefault();
                    _isLoaded = true;
                    await SaveAsync();
                    return;
                }

                // Читаем с буфером — быстрее чем ReadAllTextAsync
                await using var fs = new FileStream(PathService.ConfigFile,
                    FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

                var data = await JsonSerializer.DeserializeAsync<LauncherData>(fs, _jsonOptions);
                Data = data ?? CreateDefault();

                if (string.IsNullOrWhiteSpace(Data.Settings.GameDirectory))
                    Data.Settings.GameDirectory = PathService.MinecraftDirectory;

                _isLoaded = true;
                Console.WriteLine($"[Config] Loaded: {Data.Accounts.Count} accounts, {Data.PlaySessions.Count} sessions");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Ошибка загрузки: {ex.Message}");
                BackupCorruptConfig();
                Data = CreateDefault();
                _isLoaded = true;
            }
        }

        public async Task SaveAsync()
        {
            if (!_isLoaded)
            {
                Console.WriteLine("[Config] Skipped save (not loaded yet)");
                return;
            }

            await _saveLock.WaitAsync();
            try
            {
                PathService.EnsureDirectoriesExist();

                await using var fs = new FileStream(PathService.ConfigFile,
                    FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);

                await JsonSerializer.SerializeAsync(fs, Data, _jsonOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Ошибка сохранения: {ex.Message}");
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>Перед сбросом настроек сохраняет копию повреждённого файла,
        /// чтобы пользователь не потерял данные (аккаунты, сессии) навсегда</summary>
        private static void BackupCorruptConfig()
        {
            try
            {
                if (!File.Exists(PathService.ConfigFile)) return;
                var backup = PathService.ConfigFile + ".corrupt";
                File.Copy(PathService.ConfigFile, backup, overwrite: true);
                Console.WriteLine($"[Config] Повреждённый конфиг скопирован в {backup}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Не удалось сделать копию повреждённого конфига: {ex.Message}");
            }
        }

        private LauncherData CreateDefault()
        {
            return new LauncherData
            {
                Settings = new LauncherSettings
                {
                    GameDirectory = PathService.MinecraftDirectory,
                    MaxRamMb = GetRecommendedRam()
                }
            };
        }

        private int GetRecommendedRam()
        {
            try
            {
                var totalRamGb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024L * 1024 * 1024));
                var recommended = Math.Max(2, Math.Min(8, totalRamGb / 2));
                return recommended * 1024;
            }
            catch
            {
                return 4096;
            }
        }
    }
}