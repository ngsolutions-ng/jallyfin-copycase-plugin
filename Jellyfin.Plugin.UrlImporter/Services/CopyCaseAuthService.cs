using HtmlAgilityPack;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.UrlImporter.Services
{
    public class CopyCaseAuthService
    {
        private readonly ILogger<CopyCaseAuthService> _logger;
        private readonly IApplicationPaths _paths;

        private readonly CookieContainer _cookies = new CookieContainer();
        private readonly HttpClientHandler _handler;
        private HttpClient? _client;

        public CopyCaseAuthService(ILogger<CopyCaseAuthService> logger, IApplicationPaths paths)
        {
            _logger = logger;
            _paths = paths;
            _handler = new HttpClientHandler
            {
                CookieContainer = _cookies,
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                UseCookies = true
            };
            LoadCookiesFromDisk();
        }

        private string GetCookiePath()
        {
            var cfg = Plugin.Instance!.Configuration;
            var dir = Path.Combine(_paths.PluginConfigurationsPath, "UrlImporter");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, cfg.CookieStoreFile);
        }

        private void LoadCookiesFromDisk()
        {
            try
            {
                var path = GetCookiePath();
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var stored = JsonSerializer.Deserialize<SerializableCookies>(json);
                if (stored == null) return;
                foreach (var c in stored.Cookies)
                {
                    _cookies.Add(new Cookie(c.Name, c.Value, c.Path, c.Domain)
                    {
                        Expires = c.Expires ?? DateTime.MinValue
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się wczytać ciasteczek sesji z pliku.");
            }
        }

        private void SaveCookiesToDisk()
        {
            try
            {
                var all = _cookies.GetAllCookies();
                var serial = new SerializableCookies
                {
                    Cookies = all.Select(c => new SerializableCookie
                    {
                        Name = c.Name,
                        Value = c.Value,
                        Domain = c.Domain,
                        Path = c.Path,
                        Expires = c.Expires == DateTime.MinValue ? null : c.Expires
                    }).ToList()
                };
                var json = JsonSerializer.Serialize(serial);
                File.WriteAllText(GetCookiePath(), json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nie udało się zapisać ciasteczek sesji do pliku.");
            }
        }

        public async Task<HttpClient> GetAuthenticatedClientAsync(CancellationToken ct)
        {
            var cfg = Plugin.Instance!.Configuration;
            if (_client == null)
            {
                _client = new HttpClient(_handler, disposeHandler: false)
                {
                    Timeout = TimeSpan.FromMinutes(30)
                };
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-UrlImporter/1.0");
            }

            if (!cfg.CopyCaseEnabled)
                return _client; // bez logowania

            // spróbuj szybkie API login (jeśli istnieje)
            if (!string.IsNullOrWhiteSpace(cfg.CopyCaseApiLoginUrl) &&
                !string.IsNullOrWhiteSpace(cfg.CopyCaseUsername) &&
                !string.IsNullOrWhiteSpace(cfg.CopyCasePassword))
            {
                try
                {
                    var payload = new { username = cfg.CopyCaseUsername, password = cfg.CopyCasePassword };
                    using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var apiResp = await _client.PostAsync(cfg.CopyCaseApiLoginUrl, content, ct).ConfigureAwait(false);
                    if (apiResp.IsSuccessStatusCode)
                    {
                        _logger.LogInformation("CopyCase API login: OK");
                        SaveCookiesToDisk();
                        return _client;
                    }
                    else
                    {
                        _logger.LogWarning("CopyCase API login: HTTP {Status}", (int)apiResp.StatusCode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "CopyCase API login nie powiódł się – spróbuję loginu formularzowego.");
                }
            }

            // formularz: pobierz CSRF, wyślij POST
            await LoginViaFormAsync(ct).ConfigureAwait(false);
            return _client;
        }

        public async Task ForceLoginAsync(CancellationToken ct)
        {
            await LoginViaFormAsync(ct).ConfigureAwait(false);
        }

        private async Task LoginViaFormAsync(CancellationToken ct)
        {
            var cfg = Plugin.Instance!.Configuration;
            if (string.IsNullOrWhiteSpace(cfg.CopyCaseUsername) || string.IsNullOrWhiteSpace(cfg.CopyCasePassword))
                throw new InvalidOperationException("Brak danych logowania do CopyCase w konfiguracji.");

            if (_client == null)
            {
                _client = new HttpClient(_handler, disposeHandler: false)
                {
                    Timeout = TimeSpan.FromMinutes(30)
                };
                _client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-UrlImporter/1.0");
            }

            _logger.LogInformation("Logowanie do CopyCase jako {User}…", cfg.CopyCaseUsername);

            var loginGet = await _client.GetAsync(cfg.CopyCaseLoginUrl, ct).ConfigureAwait(false);
            loginGet.EnsureSuccessStatusCode();
            var html = await loginGet.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Spróbuj znaleźć nazwę i wartość CSRF
            string? csrfName = null, csrfValue = null;
            var csrf = doc.DocumentNode.SelectSingleNode("//input[@type='hidden' and (contains(@name,'csrf') or contains(@name,'_token'))]");
            if (csrf != null)
            {
                csrfName = csrf.GetAttributeValue("name", null);
                csrfValue = csrf.GetAttributeValue("value", null);
            }

            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("username", cfg.CopyCaseUsername!),
                new KeyValuePair<string,string>("password", cfg.CopyCasePassword!),
                new KeyValuePair<string,string>(csrfName ?? string.Empty, csrfValue ?? string.Empty)
            }.Where(kv => !string.IsNullOrEmpty(kv.Key)));

            var resp = await _client.PostAsync(cfg.CopyCaseLoginUrl, form, ct).ConfigureAwait(false);
            if ((int)resp.StatusCode == 302 || resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("Zalogowano do CopyCase (formularz).");
                SaveCookiesToDisk();
                return;
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError("Logowanie do CopyCase nie powiodło się (HTTP {Status}).", (int)resp.StatusCode);
            throw new InvalidOperationException($"Logowanie do CopyCase nie powiodło się (HTTP {(int)resp.StatusCode}). Body: {body?.Substring(0, Math.Min(body?.Length ?? 0, 300))}");
        }
    }

    internal static class CookieContainerExtensions
    {
        public static System.Collections.Generic.List<Cookie> GetAllCookies(this CookieContainer c)
        {
            var cookies = new System.Collections.Generic.List<Cookie>();
            var table = (System.Collections.IDictionary)typeof(CookieContainer)
                .GetField("m_domainTable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(c)!;
            foreach (var key in table.Keys)
            {
                var pathList = table[key];
                var lst = pathList.GetType().GetProperty("Values")!.GetValue(pathList) as System.Collections.ICollection;
                if (lst == null) continue;
                foreach (var col in lst)
                {
                    if (col is CookieCollection cc)
                        foreach (Cookie cookie in cc) cookies.Add(cookie);
                }
            }
            return cookies;
        }
    }

    internal class SerializableCookies
    {
        public System.Collections.Generic.List<SerializableCookie> Cookies { get; set; } = new();
    }

    internal class SerializableCookie
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public DateTime? Expires { get; set; }
    }
}