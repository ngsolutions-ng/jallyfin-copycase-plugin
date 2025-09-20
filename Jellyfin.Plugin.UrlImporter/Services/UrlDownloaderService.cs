using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.UrlImporter.Services
{
    public class UrlDownloaderService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<UrlDownloaderService> _logger;
        private readonly CopyCaseAuthService _copycaseAuth;

        public UrlDownloaderService(
            IHttpClientFactory httpClientFactory,
            ILogger<UrlDownloaderService> logger,
            CopyCaseAuthService copycaseAuth)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _copycaseAuth = copycaseAuth;
        }

        private async Task<HttpClient> GetClientForAsync(Uri uri, CancellationToken ct)
        {
            if (uri.Host.EndsWith("copycase.com", StringComparison.OrdinalIgnoreCase))
            {
                return await _copycaseAuth.GetAuthenticatedClientAsync(ct).ConfigureAwait(false);
            }
            var client = _httpClientFactory.CreateClient("UrlImporter");
            client.Timeout = TimeSpan.FromMinutes(30);
            return client;
        }

        public async Task<string> DownloadAsync(string url, string destinationFolder, bool overwrite, CancellationToken ct)
        {
            Directory.CreateDirectory(destinationFolder);

            var uri = new Uri(url);
            var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(uri.AbsolutePath) ? uri.LocalPath : uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"download_{DateTime.UtcNow:yyyyMMdd_HHmmssfff}";
            }

            var destinationPath = Path.Combine(destinationFolder, fileName);

            if (File.Exists(destinationPath))
            {
                if (!overwrite)
                {
                    _logger.LogInformation("Plik {Path} już istnieje — pomijam (overwrite=false)", destinationPath);
                    return destinationPath;
                }
                File.Delete(destinationPath);
            }

            var client = await GetClientForAsync(uri, ct).ConfigureAwait(false);

            _logger.LogInformation("Pobieram {Url} -> {Path}", url, destinationPath);

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode && (int)response.StatusCode is 401 or 403 && uri.Host.EndsWith("copycase.com", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Brak dostępu (HTTP {Status}). Odświeżam sesję i ponawiam…", (int)response.StatusCode);
                await _copycaseAuth.ForceLoginAsync(ct).ConfigureAwait(false);
                var retryClient = await _copycaseAuth.GetAuthenticatedClientAsync(ct).ConfigureAwait(false);
                using var retry = await retryClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                retry.EnsureSuccessStatusCode();
                await using var httpStream2 = await retry.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var fileStream2 = File.Create(destinationPath);
                await httpStream2.CopyToAsync(fileStream2, ct).ConfigureAwait(false);
                return destinationPath;
            }

            response.EnsureSuccessStatusCode();

            await using var httpStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = File.Create(destinationPath);
            await httpStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
            return destinationPath;
        }
    }
}