using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;
using RevenantLauncher.Models;

namespace RevenantLauncher.Services
{
    public class SoundService
    {
        private static readonly Lazy<SoundService> _instance = new(() => new SoundService());
        public static SoundService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, byte[]> _cache = new();

        private SoundService()
        {
            PreloadAsync();
        }

        public bool Enabled
        {
            get => ConfigService.Instance.Data.Settings.SoundsEnabled;
            set
            {
                ConfigService.Instance.Data.Settings.SoundsEnabled = value;
                _ = ConfigService.Instance.SaveAsync();
            }
        }

        public double Volume
        {
            get => ConfigService.Instance.Data.Settings.SoundsVolume;
            set
            {
                ConfigService.Instance.Data.Settings.SoundsVolume = Math.Max(0, Math.Min(1, value));
                _ = ConfigService.Instance.SaveAsync();
            }
        }

        public void PlayClick() => Play("click");
        public void PlaySuccess() => Play("success");
        public void PlayError() => Play("error");
        public void PlayNotification(ToastType type)
        {
            switch (type)
            {
                case ToastType.Success: PlaySuccess(); break;
                case ToastType.Error: PlayError(); break;
                default: Play("notification"); break;
            }
        }

        private void PreloadAsync()
        {
            Task.Run(() =>
            {
                try
                {
                    var soundsDir = GetSoundsDirectory();
                    Console.WriteLine($"[SoundService] Sounds directory: {soundsDir}");

                    if (!Directory.Exists(soundsDir))
                    {
                        Console.WriteLine("[SoundService] Sounds directory does NOT exist!");
                        return;
                    }

                    var files = Directory.GetFiles(soundsDir);
                    Console.WriteLine($"[SoundService] Found {files.Length} sound files");

                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file).ToLower();
                        var bytes = File.ReadAllBytes(file);
                        _cache[name] = bytes;
                        Console.WriteLine($"[SoundService] Loaded: {name} ({bytes.Length} bytes)");
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine("[SoundService] Preload error: " + ex.Message);
                }
            });
        }

        private static string GetSoundsDirectory()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Assets", "Sounds");
        }

        private void Play(string name)
        {
            if (!Enabled) return;

            Task.Run(() =>
            {
                try
                {
                    if (!_cache.TryGetValue(name, out var bytes))
                    {
                        // Пробуем загрузить если не в кеше
                        var soundsDir = GetSoundsDirectory();
                        var candidates = new[]
                        {
                            Path.Combine(soundsDir, $"{name}.wav"),
                            Path.Combine(soundsDir, $"{name}.mp3")
                        };

                        foreach (var path in candidates)
                        {
                            if (File.Exists(path))
                            {
                                bytes = File.ReadAllBytes(path);
                                _cache[name] = bytes;
                                Console.WriteLine($"[SoundService] Lazy-loaded: {name}");
                                break;
                            }
                        }

                        if (bytes == null)
                        {
                            Console.WriteLine($"[SoundService] Sound '{name}' not found!");
                            return;
                        }
                    }

                    using var ms = new MemoryStream(bytes);
                    WaveStream reader;

                    if (IsMp3(bytes))
                        reader = new Mp3FileReader(ms);
                    else
                        reader = new WaveFileReader(ms);

                    using (reader)
                    {
                        var output = new WaveOutEvent();
                        output.Volume = (float)Volume;
                        output.Init(reader);

                        // Ждём окончания воспроизведения по событию, а не busy-wait'ом
                        // (цикл с Task.Delay забивал пул потоков на время звука)
                        var finished = new System.Threading.Tasks.TaskCompletionSource<bool>();
                        output.PlaybackStopped += (_, _) => finished.TrySetResult(true);
                        output.Play();
                        finished.Task.Wait(TimeSpan.FromSeconds(30));
                        output.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SoundService] Play '{name}' failed: {ex.Message}");
                }
            });
        }

        private static bool IsMp3(byte[] bytes)
        {
            if (bytes.Length < 3) return false;
            if (bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3') return true;
            if (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0) return true;
            return false;
        }
    }
}