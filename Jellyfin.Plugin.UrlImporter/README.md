# URL Importer (Jellyfin plugin) — CopyCase auth
Wtyczka do pobierania plików z URL i dodawania ich do biblioteki Jellyfin. Obsługuje logowanie do **copycase.com** i utrzymanie sesji (cookies).

## Budowa (Linux/Debian)
1. Zainstaluj .NET SDK 8.0 (repo Microsoftu).
2. Dopasuj wersje Jellyfin w `.csproj`/`manifest.json` do swojej instancji (domyślnie ABI 10.9.x).
3. Build:
   ```bash
   dotnet restore
   dotnet build -c Release
   ```
4. Skopiuj `bin/Release/net8.0/` do katalogu pluginów Jellyfin:
   - Linux: `/var/lib/jellyfin/plugins/UrlImporter/`

## Konfiguracja CopyCase
- **CopyCaseEnabled**, **CopyCaseUsername**, **CopyCasePassword**, **CopyCaseLoginUrl**, **CopyCaseApiLoginUrl**.
- Ciasteczka sesji zapisują się w katalogu konfiguracyjnym pluginu (plik `copycase_cookies.json`).

Wygenerowano 2025-09-20T13:31:43.156322Z