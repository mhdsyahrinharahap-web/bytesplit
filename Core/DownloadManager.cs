using System.Collections.Concurrent;
using System.Text.Json;
using ByteSplit.Models;

namespace ByteSplit.Core;

public sealed class DownloadManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _dataDir;
    private readonly string _tasksFile;
    private readonly string _settingsFile;
    private readonly ConcurrentDictionary<string, DownloadTaskItem> _tasks = new();
    private readonly ConcurrentDictionary<string, ActiveRunner> _active = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    public SpeedLimiter Limiter { get; } = new(0);
    public AppSettings Settings { get; private set; } = new();

    public event Action<DownloadTaskItem>? TaskUpdated;
    public event Action<DownloadTaskItem>? TaskCompleted;
    public event Action? ListChanged;

    public DownloadManager(string dataDir)
    {
        _dataDir = dataDir;
        _tasksFile = Path.Combine(dataDir, "tasks.json");
        _settingsFile = Path.Combine(dataDir, "settings.json");
    }

    public async Task InitAsync()
    {
        Directory.CreateDirectory(_dataDir);
        await LoadSettingsAsync().ConfigureAwait(false);
        await LoadTasksAsync().ConfigureAwait(false);
    }

    private async Task LoadSettingsAsync()
    {
        string fallbackDownloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        Settings = new AppSettings { DefaultOutputDir = fallbackDownloads };
        bool needsSave = false;

        if (File.Exists(_settingsFile))
        {
            try
            {
                string raw = await File.ReadAllTextAsync(_settingsFile).ConfigureAwait(false);
                var loaded = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
                if (loaded != null)
                {
                    Settings = loaded;
                    if (string.IsNullOrWhiteSpace(Settings.DefaultOutputDir) || !CanEnsureDir(Settings.DefaultOutputDir))
                    {
                        Settings.DefaultOutputDir = fallbackDownloads;
                        needsSave = true;
                    }
                }
            }
            catch
            {
                needsSave = true;
            }
        }
        else
        {
            needsSave = true;
        }

        Directory.CreateDirectory(Settings.DefaultOutputDir);
        Limiter.SetLimit(Settings.SpeedLimitBps);

        if (needsSave)
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }
    }

    private static bool CanEnsureDir(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SaveSettingsAsync()
    {
        await _persistLock.WaitAsync().ConfigureAwait(false);
        try
        {
            string json = JsonSerializer.Serialize(Settings, JsonOptions);
            string tmp = $"{_settingsFile}.tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _settingsFile, overwrite: true);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private async Task LoadTasksAsync()
    {
        if (!File.Exists(_tasksFile))
            return;

        try
        {
            string raw = await File.ReadAllTextAsync(_tasksFile).ConfigureAwait(false);
            var list = JsonSerializer.Deserialize<List<DownloadTaskItem>>(raw, JsonOptions);
            if (list == null)
                return;

            foreach (var item in list)
            {
                item.Category = CategoryHelper.NormalizeCategory(item.Category);
                if (item.Status is "downloading" or "probing" or "queued")
                {
                    item.Status = "paused";
                    item.SpeedBps = 0;
                    item.EtaSec = null;
                }
                _tasks[item.Id] = item;
            }
        }
        catch { }
    }

    public async Task PersistTasksAsync()
    {
        await _persistLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            string tmp = $"{_tasksFile}.tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, _tasksFile, overwrite: true);
        }
        catch { }
        finally
        {
            _persistLock.Release();
        }
    }

    public List<DownloadTaskItem> GetTasks()
    {
        return _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public DownloadTaskItem? GetTask(string id)
    {
        return _tasks.TryGetValue(id, out var task) ? task : null;
    }

    public DownloadTaskItem AddDownload(
        string url,
        string? customFileName,
        string? outputDir,
        int connections,
        bool autoCategorize)
    {
        Uri validUri = HttpProber.ValidateHttpUrl(url);
        string chosenBase = !string.IsNullOrWhiteSpace(outputDir) && CanEnsureDir(outputDir.Trim())
            ? Path.GetFullPath(outputDir.Trim())
            : Settings.DefaultOutputDir;

        string? safeCustom = !string.IsNullOrWhiteSpace(customFileName)
            ? HttpProber.SanitizeFileName(customFileName)
            : null;

        var task = new DownloadTaskItem
        {
            Url = validUri.ToString(),
            FileName = safeCustom,
            CustomFileName = safeCustom,
            Category = CategoryHelper.DetectCategory(safeCustom ?? validUri.ToString()),
            OutputDir = chosenBase,
            BaseOutputDir = chosenBase,
            Connections = Math.Clamp(connections, 1, 16),
            AutoCategorize = autoCategorize,
            Status = "queued"
        };

        _tasks[task.Id] = task;
        Launch(task);
        ListChanged?.Invoke();
        return task;
    }

    public void RefreshLink(string id, string newUrl)
    {
        if (!_tasks.TryGetValue(id, out var task))
            throw new InvalidOperationException("Tugas unduhan tidak ditemukan.");
        if (task.Status == "completed")
            throw new InvalidOperationException("Unduhan sudah selesai.");
        if (_active.ContainsKey(id))
            throw new InvalidOperationException("Jeda unduhan terlebih dahulu sebelum memperbarui link.");

        Uri validUri = HttpProber.ValidateHttpUrl(newUrl);
        task.Url = validUri.ToString();
        task.FinalUrl = null;
        task.Error = null;
        task.SpeedBps = 0;
        task.EtaSec = null;
        task.Status = "queued";

        _ = PersistTasksAsync();
        Launch(task);
        TaskUpdated?.Invoke(task);
    }

    private void Launch(DownloadTaskItem task)
    {
        var cts = new CancellationTokenSource();
        var runner = new ActiveRunner(cts);
        if (!_active.TryAdd(task.Id, runner))
        {
            cts.Dispose();
            return;
        }

        runner.WorkTask = Task.Run(async () =>
        {
            try
            {
                await DownloadEngine.RunTaskAsync(
                    task,
                    Limiter,
                    t => TaskUpdated?.Invoke(t),
                    () => _ = PersistTasksAsync(),
                    () => runner.IsCanceled,
                    cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _active.TryRemove(task.Id, out _);
                cts.Dispose();
                await PersistTasksAsync().ConfigureAwait(false);
                TaskUpdated?.Invoke(task);
                if (task.Status == "completed")
                {
                    TaskCompleted?.Invoke(task);
                }
            }
        });
    }

    public async Task PauseDownloadAsync(string id)
    {
        if (_active.TryGetValue(id, out var runner))
        {
            runner.Cts.Cancel();
            if (runner.WorkTask != null)
                await runner.WorkTask.ConfigureAwait(false);
        }
    }

    public void ResumeDownload(string id)
    {
        if (!_tasks.TryGetValue(id, out var task))
            return;
        if (task.Status == "completed" || _active.ContainsKey(id))
            return;

        Launch(task);
    }

    public async Task RemoveDownloadAsync(string id, bool deleteCompletedFile = false)
    {
        if (!_tasks.TryGetValue(id, out var task))
            return;

        if (_active.TryGetValue(id, out var runner))
        {
            runner.IsCanceled = true;
            runner.Cts.Cancel();
            if (runner.WorkTask != null)
                await runner.WorkTask.ConfigureAwait(false);
        }
        else if (task.Status != "completed" && !string.IsNullOrEmpty(task.TempPath))
        {
            try { if (File.Exists(task.TempPath)) File.Delete(task.TempPath); } catch { }
        }

        if (deleteCompletedFile && task.Status == "completed" && !string.IsNullOrEmpty(task.DestPath))
        {
            try { if (File.Exists(task.DestPath)) File.Delete(task.DestPath); } catch { }
        }

        _tasks.TryRemove(id, out _);
        await PersistTasksAsync().ConfigureAwait(false);
        ListChanged?.Invoke();
    }

    public async Task PauseAllAsync()
    {
        var runners = _active.Values.ToList();
        foreach (var r in runners)
        {
            r.Cts.Cancel();
        }
        await Task.WhenAll(runners.Where(r => r.WorkTask != null).Select(r => r.WorkTask!)).ConfigureAwait(false);
        await PersistTasksAsync().ConfigureAwait(false);
    }

    private sealed class ActiveRunner(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public Task? WorkTask { get; set; }
        public volatile bool IsCanceled;
    }
}