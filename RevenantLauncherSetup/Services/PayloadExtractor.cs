using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;

namespace RevenantLauncherSetup.Services
{
    public static class PayloadExtractor
    {
        private const string PayloadResourceName = "payload.zip";

        /// <summary>Извлекает payload.zip из embedded resources и распаковывает в targetDir</summary>
        public static async Task ExtractAsync(string targetDir, Action<double, string>? progress = null)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = GetPayloadStream(assembly);

            if (resourceStream == null)
                throw new InvalidOperationException(
                    "Файл payload.zip не найден в ресурсах. " +
                    "Убедитесь что перед сборкой установщика вы поместили payload.zip в папку Assets/.");

            Directory.CreateDirectory(targetDir);

            // Копируем resource stream в memory для быстрого доступа
            using var memStream = new MemoryStream();
            await resourceStream.CopyToAsync(memStream);
            memStream.Position = 0;

            using var zip = new ZipArchive(memStream, ZipArchiveMode.Read);

            var entries = zip.Entries;
            int total = entries.Count;
            int done = 0;

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    // Это папка — создаём
                    var dirPath = Path.Combine(targetDir, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                }
                else
                {
                    var filePath = Path.Combine(targetDir, entry.FullName);
                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir))
                        Directory.CreateDirectory(fileDir);

                    // Извлекаем файл
                    await Task.Run(() => entry.ExtractToFile(filePath, overwrite: true));
                }

                done++;
                double percent = (double)done / total * 100;
                progress?.Invoke(percent, $"Распаковка: {entry.Name ?? entry.FullName}");
            }
        }

        private static Stream? GetPayloadStream(Assembly assembly)
        {
            // Пробуем разные варианты имени ресурса
            var candidates = new[]
            {
                PayloadResourceName,
                $"RevenantLauncherSetup.{PayloadResourceName}",
                $"RevenantLauncherSetup.Assets.{PayloadResourceName}"
            };

            foreach (var name in candidates)
            {
                var stream = assembly.GetManifestResourceStream(name);
                if (stream != null) return stream;
            }

            // Ищем любой ресурс содержащий "payload"
            var allResources = assembly.GetManifestResourceNames();
            foreach (var resName in allResources)
            {
                if (resName.Contains("payload", StringComparison.OrdinalIgnoreCase))
                {
                    return assembly.GetManifestResourceStream(resName);
                }
            }

            return null;
        }

        /// <summary>Проверяет что payload.zip доступен в ресурсах</summary>
        public static bool IsPayloadAvailable()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = GetPayloadStream(assembly);
            return stream != null;
        }
    }
}