using Jellyfin.Plugin.UrlImporter.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.UrlImporter.Tasks
{
    public class DownloadAndImportTask : IScheduledTask
    {
        private readonly UrlDownloaderService _downloader;
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly ILogger<DownloadAndImportTask> _logger;

        public DownloadAndImportTask(
            UrlDownloaderService downloader,
            ILibraryMonitor libraryMonitor,
            ILogger<DownloadAndImportTask> logger)
        {
            _downloader = downloader;
            _libraryMonitor = libraryMonitor;
            _logger = logger;
        }

        public string Name => "URL Importer – pobierz i zaimportuj";
        public string Key => "UrlImporter.DownloadAndImport";
        public string Description => "Pobiera pliki z konfiguracji (w tym z copycase.com po zalogowaniu) i dodaje do biblioteki";
        public string Category => "Library";

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance!.Configuration;

            if (string.IsNullOrWhiteSpace(config.DestinationFolder))
                throw new InvalidOperationException("Nie ustawiono DestinationFolder w konfiguracji wtyczki.");

            var urls = config.Urls ?? new List<string>();
            if (urls.Count == 0)
            {
                _logger.LogInformation("Brak URL w konfiguracji – nic do zrobienia.");
                return;
            }

            var step = 100.0 / (urls.Count + 1);
            double current = 0;

            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await _downloader.DownloadAsync(url, config.DestinationFolder, config.OverwriteIfExists, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Błąd pobierania {Url}", url);
                }
                finally
                {
                    current += step;
                    progress.Report(current);
                }
            }

            if (config.TriggerLibraryScan)
            {
                try
                {
                    _logger.LogInformation("Uruchamiam skan biblioteki dla folderu: {Folder}", config.DestinationFolder);
                    _libraryMonitor.ReportFileSystemChange(config.DestinationFolder);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Nie udało się zainicjować skanu przez ILibraryMonitor. Jellyfin może wykryć zmiany automatycznie, jeśli włączone jest monitorowanie w czasie rzeczywistym.");
                }
            }

            progress.Report(100);
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return Array.Empty<TaskTriggerInfo>();
        }
    }
}