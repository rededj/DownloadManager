using System;
using System.Drawing.Design;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MaterialSkin;
using MaterialSkin.Controls;

namespace DownloadManager
{
    public partial class MainForm : MaterialForm
    {
        private const string STATE_FILE = "download_state.json";
        private const string TEMP_EXTENSION = ".tmpdownload";
        private const string HISTORY_FILE = "history.json";
        private const string SETTINGS_FILE = "settings.json";
        private string downloadDirectory;
        private FileStream? _fileStream; 
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isDownloading = false;
        private bool _isPaused = false;
        private string _currentFilePath;
        private long _totalBytes;
        private long _downloadedBytes;
        private bool _fileWasCreatedInThisSession = false; 
        private DownloadState _currentDownloadState;
        private List<string> downloadHistory = new List<string>();
        private long _lastBytesCheck = 0;
        private DateTime _lastSpeedTime;
        private string _lastSpeedText = "0 KB/s";

        private class DownloadState
        {
            public string Url { get; set; }
            public string FilePath { get; set; }
            public long TotalBytes { get; set; }
            public long DownloadedBytes { get; set; }
            public DateTime StartTime { get; set; }
        }
        private async void DownloadButton_Click(object? sender, EventArgs e)
        {
            string url = urlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Пожалуйста, введите URL файла.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                _isDownloading = true;
                _isPaused = false;
                _downloadedBytes = 0;
                _fileWasCreatedInThisSession = false; 
                UpdateButtonStates();

                _cancellationTokenSource = new CancellationTokenSource();
                progressBar.Value = 0;
                progressBar.Style = ProgressBarStyle.Continuous;
                
                statusLabel.Text = "Проверка ссылки...";
                
                using var httpClient = new HttpClient();
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentType?.MediaType == "text/html")
                {
                    statusLabel.Text = "Ошибка: ссылка на веб-страницу";
                    MessageBox.Show(
                        "Ссылка ведёт на веб-страницу (например, YouTube, Google Drive), а не на файл.\n\n" +
                        "Пожалуйста, укажите прямую ссылку на файл (например, .pdf, .zip, .mp4).",
                        "Неверная ссылка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return; 
                }
                statusLabel.Text = "Начало загрузки...";
                AddToHistory(url);
                try
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    return;
                }

                SaveDownloadState();
                await StartDownloadAsync(url, _cancellationTokenSource.Token);
            }
            catch (TaskCanceledException)
            {
                progressBar.Value = 0;
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Ошибка загрузки";
                progressBar.Value = 0; 
                MessageBox.Show($"Не удалось скачать файл:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isDownloading = false;
                _isPaused = false;
                UpdateButtonStates();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        private void PauseButton_Click(object? sender, EventArgs e)
        {
            if (_isDownloading && !_isPaused)
            {
                _isPaused = true;
                UpdateButtonStates();
                
                if (_totalBytes > 0)
                {
                    int progress = (int)((_downloadedBytes * 100) / _totalBytes);
                    progress = Math.Max(0, Math.Min(100, progress));
                    statusLabel.Text = $"На паузе: {progress}% ({FormatBytes(_downloadedBytes)} / {FormatBytes(_totalBytes)})";
                }
                else
                {
                    statusLabel.Text = $"На паузе: {FormatBytes(_downloadedBytes)}";
                }
            }
        }
        private void ResumeButton_Click(object? sender, EventArgs e)
        {
            if (_isDownloading && _isPaused)
            {
                _isPaused = false;
                UpdateButtonStates();
                statusLabel.Text = "Возобновление загрузки...";
            }
        }
        private void CancelButton_Click(object? sender, EventArgs e)
        {
            if (_isDownloading && _cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                statusLabel.Text = "Отмена загрузки...";
                if (_fileStream != null)
                {
                    try
                    {
                        _fileStream.Close();
                        _fileStream.Dispose();
                    }
                    catch { }
                    finally
                    {
                        _fileStream = null;
                    }
                }
                if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                {
                    try
                    {
                        File.Delete(_currentFilePath);
                    }
                    catch { }
                }

                progressBar.Value = 0;
                statusLabel.Text = "Загрузка отменена.";

                _isDownloading = false;
                _isPaused = false;
                UpdateButtonStates();
            }
        }
        private void ChooseFolderButton_Click(object sender, EventArgs e)
        {
        using var dialog = new FolderBrowserDialog();
        dialog.SelectedPath = downloadDirectory;
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                downloadDirectory = dialog.SelectedPath;
                SaveSettings();
            }
        }
        private void DeleteIncompleteFile()
        {
            try
                {
                    if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                    {
                        File.Delete(_currentFilePath);
                    }
                    ClearDownloadState();
                }
                catch{}
        }
        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
        }
        private void ResetDownloadState()
        {
            _isDownloading = false;
            _isPaused = false;
            UpdateButtonStates();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            ClearDownloadState();
        }
        private void AddToHistory(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            if (downloadHistory.Contains(url))
                downloadHistory.Remove(url);

            downloadHistory.Insert(0, url);

            if (downloadHistory.Count > 10)
                downloadHistory.RemoveAt(downloadHistory.Count - 1);

            SaveHistory();
            LoadHistory(); 
        }
        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HISTORY_FILE))
                {
                    var json = File.ReadAllText(HISTORY_FILE);
                    downloadHistory = JsonSerializer.Deserialize<List<string>>(json)
                                        ?? new List<string>();
                }
            }
            catch { }
        }
        private void SaveHistory()
        {
            try
            {
                var json = JsonSerializer.Serialize(downloadHistory, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HISTORY_FILE, json);
            }
            catch { }
        }
        private async Task StartDownloadAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                statusLabel.Text = "Подготовка к загрузке...";

                string downloadsFolder = downloadDirectory;
                Directory.CreateDirectory(downloadsFolder);

                using var httpClientInfo = CreateHttpClient();
                using var headResponse = await httpClientInfo.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                headResponse.EnsureSuccessStatusCode();

                string fileName = ExtractFileName(url, headResponse);
                string finalPath = GetUniqueFilePath(downloadsFolder, fileName);
                _currentFilePath = finalPath + TEMP_EXTENSION;

                long existingBytes = 0;
                bool resumeMode = false;
                if (File.Exists(_currentFilePath))
                {
                    existingBytes = new FileInfo(_currentFilePath).Length;
                    resumeMode = existingBytes > 0;
                    _downloadedBytes = existingBytes;
                }
                else
                {
                    _downloadedBytes = 0;
                }

                _totalBytes = headResponse.Content.Headers.ContentLength ?? 0;

                progressBar.Style = _totalBytes > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (_totalBytes > 0)
                {
                    progressBar.Value = (int)Math.Clamp((_downloadedBytes * 100) / _totalBytes, 0, 100);
                }

                using var httpClient = CreateHttpClient();
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (resumeMode)
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);

                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

                _fileStream = new FileStream(
                    _currentFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.None
                );

                byte[] buffer = new byte[8192];
                DateTime lastSave = DateTime.Now;
                long lastSavedBytes = _downloadedBytes;

                int bytesRead;

                _lastBytesCheck = _downloadedBytes;
                _lastSpeedTime = DateTime.Now;
                _lastSpeedText = "0 KB/s";

                while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    while (_isPaused && !cancellationToken.IsCancellationRequested)
                        await Task.Delay(100, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    await _fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    await _fileStream.FlushAsync(cancellationToken);
                    _downloadedBytes += bytesRead;

                    {
                        var now = DateTime.Now;
                        double seconds = (now - _lastSpeedTime).TotalSeconds;
                        if (seconds >= 1)
                        {
                            long delta = _downloadedBytes - _lastBytesCheck;
                            double speed = delta / seconds;
                            _lastSpeedText = $"{FormatBytes((long)speed)}/s";
                            _lastBytesCheck = _downloadedBytes;
                            _lastSpeedTime = now;
                        }
                    }

                    if ((DateTime.Now - lastSave).TotalSeconds >= 5 || _downloadedBytes - lastSavedBytes >= 1024 * 1024)
                    {
                        SaveDownloadState();
                        lastSave = DateTime.Now;
                        lastSavedBytes = _downloadedBytes;
                    }

                    progressBar.Invoke((MethodInvoker)(() =>
                    {
                        if (_totalBytes > 0)
                        {
                            int progress = (int)Math.Clamp((_downloadedBytes * 100) / _totalBytes, 0, 100);
                            progressBar.Value = progress;
                            if (!_isPaused)
                            {
                                statusLabel.Text =
                                    $"Загружено: {progress}% ({FormatBytes(_downloadedBytes)} / {FormatBytes(_totalBytes)}) — {_lastSpeedText}";
                            }
                        }
                        else
                        {
                            if (!_isPaused)
                            {
                                statusLabel.Text = $"Загружено: {FormatBytes(_downloadedBytes)} — {_lastSpeedText}";
                            }
                        }
                    }));
                }

                _fileStream.Close();
                _fileStream.Dispose();
                _fileStream = null;

                string finalFullPath = _currentFilePath.Replace(TEMP_EXTENSION, "");
                if (File.Exists(finalFullPath))
                    finalFullPath = GetUniqueFilePath(downloadsFolder, Path.GetFileName(finalFullPath));

                File.Move(_currentFilePath, finalFullPath, true);
                _currentFilePath = finalFullPath;

                ClearDownloadState();

                trayIcon?.ShowBalloonTip(
                    400,
                    "Загрузка завершена",
                    $"Файл успешно скачан:\n{Path.GetFileName(finalFullPath)}",
                    ToolTipIcon.Info
                );

                progressBar.Invoke((MethodInvoker)(() =>
                {
                    progressBar.Value = 100;
                    statusLabel.Text = $@"Файл сохранен: {Path.GetDirectoryName(finalFullPath)}\{Path.GetFileName(finalFullPath)}";
                }));
            }
            catch (OperationCanceledException)
            {
                if (_fileStream != null)
                {
                    try
                    {
                        _fileStream.Close();
                        _fileStream.Dispose();
                        _fileStream = null;
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
                {
                    try { File.Delete(_currentFilePath); } catch { }
                }

                progressBar.Invoke((MethodInvoker)(() => progressBar.Value = 0));
                statusLabel.Invoke((MethodInvoker)(() => statusLabel.Text = "Загрузка отменена."));
            }
            catch (Exception ex)
            {
                _fileStream?.Close();
                _fileStream?.Dispose();
                _fileStream = null;

                DeleteIncompleteFile();
                progressBar.Invoke((MethodInvoker)(() => progressBar.Value = 0));
                statusLabel.Invoke((MethodInvoker)(() => statusLabel.Text = "Ошибка загрузки"));

                MessageBox.Show($"Не удалось скачать файл:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isDownloading = false;
                _isPaused = false;
                UpdateButtonStates();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }
        private void CheckForRecovery()
        {
            try
            {
                if (File.Exists(STATE_FILE))
                {
                    var stateJson = File.ReadAllText(STATE_FILE);
                    var state = JsonSerializer.Deserialize<DownloadState>(stateJson);
                    
                    if (state != null && File.Exists(state.FilePath))
                    {
                        var result = MessageBox.Show(
                            "Обнаружена незавершенная загрузка. Хотите возобновить?",
                            "Восстановление загрузки",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question
                        );

                        if (result == DialogResult.Yes)
                        {
                            urlTextBox.Text = state.Url;
                            _currentDownloadState = state;
                            _currentFilePath = state.FilePath;
                            _totalBytes = state.TotalBytes;
                            _downloadedBytes = state.DownloadedBytes;
                            ResumeInterruptedDownload();
                        }
                        else
                        {
                            CleanupInterruptedDownload();
                        }
                    }
                    else
                    {
                        CleanupInterruptedDownload();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при восстановлении загрузки: {ex.Message}");
                CleanupInterruptedDownload();
            }
        }
        private void CleanupInterruptedDownload()
        {
            try
            {
                if (File.Exists(STATE_FILE))
                {
                    try
                    {
                        var stateJson = File.ReadAllText(STATE_FILE);
                        var state = JsonSerializer.Deserialize<DownloadState>(stateJson);

                        if (state != null && !string.IsNullOrEmpty(state.FilePath))
                        {
                            try
                            {
                                if (File.Exists(state.FilePath))
                                    File.Delete(state.FilePath);
                            }
                            catch {}
                        }
                    }
                    catch {}
                    try
                    {
                        File.Delete(STATE_FILE);
                    }
                    catch {}
                }
            }
            catch{}
        }
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SETTINGS_FILE))
                {
                    var json = File.ReadAllText(SETTINGS_FILE);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    
                    if (settings != null && !string.IsNullOrWhiteSpace(settings.DownloadDirectory))
                    {
                        downloadDirectory = settings.DownloadDirectory;
                    }
                    else
                    {
                        downloadDirectory = new AppSettings().DownloadDirectory;
                    }
                }
                else
                {
                    downloadDirectory = new AppSettings().DownloadDirectory;
                }
                if (!Directory.Exists(downloadDirectory))
                {
                    Directory.CreateDirectory(downloadDirectory);
                }
            }
            catch
            { 
                downloadDirectory = new AppSettings().DownloadDirectory;
                if (!Directory.Exists(downloadDirectory))
                    Directory.CreateDirectory(downloadDirectory);
            }
        }
        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings { DownloadDirectory = downloadDirectory };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SETTINGS_FILE, json);
            }
            catch { }
        }
        private async void ResumeInterruptedDownload()
        {
            try
            {
                _isDownloading = true;
                _isPaused = false;
                _fileWasCreatedInThisSession = false;
                UpdateButtonStates();

                _cancellationTokenSource = new CancellationTokenSource();
                
                progressBar.Value = _totalBytes > 0 ? 
                    (int)((_downloadedBytes * 100) / _totalBytes) : 0;
                progressBar.Style = _totalBytes > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;

                statusLabel.Text = "Возобновление загрузки...";
                using var httpClient = CreateHttpClient();
                using var response = await httpClient.GetAsync(_currentDownloadState.Url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await StartDownloadAsync(_currentDownloadState.Url, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при возобновлении загрузки: {ex.Message}");
                ResetDownloadState();
            }
        }
        private void SaveDownloadState()
        {
            try
            {
                if (_isDownloading && !string.IsNullOrEmpty(_currentFilePath))
                {
                    var state = new DownloadState
                    {
                        Url = urlTextBox.Text,
                        FilePath = _currentFilePath,
                        TotalBytes = _totalBytes,
                        DownloadedBytes = _downloadedBytes,
                        StartTime = DateTime.Now
                    };

                    var stateJson = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(STATE_FILE, stateJson);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сохранения состояния: {ex.Message}");
            }
        }
        private void ClearDownloadState()
        {
            try
            {
                if (File.Exists(STATE_FILE))
                    File.Delete(STATE_FILE);
            }
            catch { }
        }
        private string ExtractFileName(string url, HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentDisposition?.FileNameStar != null)
            {
                return response.Content.Headers.ContentDisposition.FileNameStar;
            }
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                return response.Content.Headers.ContentDisposition.FileName.Trim('"');
            }

            try
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(fileName) && fileName.Contains("."))
                    return fileName;
            }
            catch { }
            string extension = "";
            if (response.Content.Headers.ContentType?.MediaType != null)
            {
                var mime = response.Content.Headers.ContentType.MediaType;
                extension = mime.ToLowerInvariant() switch
                {
                    "application/pdf" => ".pdf",
                    "text/plain" => ".txt",
                    "text/html" => ".html",
                    "application/msword" => ".doc",
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
                    "application/vnd.ms-excel" => ".xls",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
                    "application/vnd.ms-powerpoint" => ".ppt",
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
                    "application/zip" => ".zip",
                    "application/x-zip-compressed" => ".zip",
                    "application/x-7z-compressed" => ".7z",
                    "application/gzip" => ".gz",
                    "application/x-tar" => ".tar",
                    "application/x-rar-compressed" => ".rar",
                    "image/jpeg" => ".jpg",
                    "image/jpg" => ".jpg",
                    "image/png" => ".png",
                    "image/gif" => ".gif",
                    "image/bmp" => ".bmp",
                    "image/webp" => ".webp",
                    "image/svg+xml" => ".svg",
                    "audio/mpeg" => ".mp3",
                    "audio/wav" => ".wav",
                    "audio/ogg" => ".ogg",
                    "video/mp4" => ".mp4",
                    "video/quicktime" => ".mov",
                    "video/x-msvideo" => ".avi",
                    "video/x-matroska" => ".mkv",
                    "video/webm" => ".webm",
                    "application/x-msdownload" => ".exe",
                    "application/octet-stream" => ".bin",
                    "application/x-sh" => ".sh",
                    _ => ".dat"
                };
            }
            return $"download_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
        }
        private string GetUniqueFilePath(string directory, string fileName)
        {
            if (!File.Exists(Path.Combine(directory, fileName)))
                return Path.Combine(directory, fileName);
                
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);
            int counter = 1;

            while (true)
            {
                string newName = $"{fileNameWithoutExt} ({counter}){extension}";
                string newPath = Path.Combine(directory, newName);
                if (!File.Exists(newPath))
                    return newPath;
                counter++;
            }
        }
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "Б", "КБ", "МБ", "ГБ" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (_isDownloading)
            {
                SaveDownloadState();
                try
                {
                    _fileStream?.Flush();
                    _fileStream?.Close();
                    _fileStream?.Dispose();
                    _fileStream = null;
                }
                catch { }
                _isDownloading = false;
                _isPaused = true;
                UpdateButtonStates();
            }
        }
    }
}