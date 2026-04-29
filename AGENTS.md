# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, etc.) working in this repository.

## Project at a glance

`SharePointVideoDownloader` is a small Windows-focused C# console app (.NET 9) that helps a logged-in user download Microsoft SharePoint / Stream videos (typically Teams meeting recordings).

The app drives a real Chromium instance with **PuppeteerSharp** and tries two strategies, in order:

**Primary — direct file download (works on DRM-protected stream pages).**
1. Navigate to the SharePoint `stream.aspx?id=<path>` URL.
2. Parse the `id` query parameter to derive the underlying file path inside the user's OneDrive / SharePoint site.
3. Use `Page.setDownloadBehavior` over CDP to redirect Chromium downloads into the desired output directory.
4. Trigger the download by injecting an `<a download>` click pointing at `https://<host>/<path>?download=1`. Chromium handles the auth, redirects, and `Content-Disposition` natively. The file that lands is the original non-DRM mp4.
5. Poll for `<file>.crdownload` (in-progress) and the final file to detect completion.

**Fallback — manifest interception + `yt-dlp.exe` (legacy path).** Used only when the direct download cannot run (e.g., URL has no `id` parameter):
1. Click play to trigger media loading.
2. Listen for `videomanifest?provider=...` and capture both the URL and the *non-OPTIONS* request headers the browser actually sent (including the SharePoint `X-SPOPacToken` proof-of-possession token).
3. Export every cookie via raw CDP `Network.getAllCookies` (the per-URL API misses cross-domain auth-flow cookies) into a temporary Netscape `cookies.txt`.
4. Truncate the manifest URL up to and including `index&format=dash`.
5. Hand the URL to `yt-dlp.exe` along with `--cookies <file>` and `--add-header "<name>:<value>"` for each replayed header. Authorization-style header values are redacted in the echoed command line; the cookies file is deleted in a `finally` block.

Microsoft now serves DRM-protected DASH manifests for many Stream / Teams recordings, so the legacy `yt-dlp` path will often surface `ERROR: This video is DRM protected`. That is expected; the primary direct-download path is what makes the tool useful for current content.

Authentication is delegated to the browser session — the app never types passwords. The first run is intended to be non-headless so the user can sign in to Microsoft 365, and the session is persisted via PuppeteerSharp's `UserDataDir` (defaults to `%LOCALAPPDATA%\PuppeteerSession`).

## Repository layout

| Path | Purpose |
| --- | --- |
| `Program.cs` | The entire application — argument parsing, browser orchestration, manifest capture, yt-dlp invocation. Single-file by design. |
| `SharePointVideoDownloader.csproj` | .NET 9, references `PuppeteerSharp` 20.1.3 only. |
| `SharePointVideoDownloader.sln` | Visual Studio solution. |
| `Dependencies/yt-dlp.exe` | Bundled yt-dlp binary copied into the `*-DotNet-Dependencies` release ZIP. |
| `Releases/` | Pre-built ZIPs published on GitHub Releases (DotNet, x64/x86/ARM64 self-contained). |
| `_publishAll.bat` | Builds and zips every release flavour. Hard-codes the version string (currently `v01.02`). |
| `README.md` | End-user documentation. |
| `LICENSE` | MIT. |

There is intentionally no `src/`, no test project, no service abstractions — adding any of those would be over-engineering for a tool this size.

## How the core flow is implemented (`Program.cs`)

Anchor points if you need to edit:

- **CLI parsing**: `Main` walks `args` manually with a `switch` on `-u/--url`, `-a/--audio`, `-o/--output`, `-h/--help`. Falls back to interactive prompts when arguments are absent or invalid. See `ShowHelp()` for the canonical surface.
- **Output filename normalisation**: defaults to `downloaded_<video|audio>_<timestamp>.<mp4|mp3>`. Warns when the extension does not match the chosen mode but still passes it through to yt-dlp.
- **Browser launch**: `Puppeteer.LaunchAsync` with `Headless = RunHeadless` (default `false`), `--no-sandbox`, and `UserDataDir = userDataDir` for session persistence. `BrowserFetcher.DownloadAsync()` ensures Chromium is present on first run.
- **Manifest capture**: `page.Response += ...` watches every response for a URL containing `videomanifest?provider` (case-insensitive) and resolves a `TaskCompletionSource<string>` with the first match. There is a 60-second `Task.WhenAny` timeout in `Main`.
- **Play trigger**: `possibleSelectors` array is tried in order with `WaitForSelectorAsync` (20 s each). If none match the app still waits for the manifest in case playback already started.
- **URL trimming**: searches for the literal string `index&format=dash` and keeps only the prefix up to and including that token, then passes it to yt-dlp.
- **yt-dlp invocation**: `RunYtDlp` builds either `"<url>" -o "<file>"` or `"<url>" -x --extract-audio --audio-format mp3 --audio-quality 0 -o "<file>"` and starts `yt-dlp.exe` with redirected stdout/stderr. Output is prefixed `[yt-dlp]` / `[yt-dlp ERR]` in the console.

`YtDlpPath` is the constant that controls how yt-dlp is located; it defaults to `"yt-dlp.exe"` (relative — found via the working directory or `PATH`).

## Build, run, publish

```bash
# Restore + build
dotnet build

# Run from source (interactive)
dotnet run

# Run from source with arguments — note the -- separator
dotnet run -- -u "https://..." -o "meeting.mp4"

# Run a built binary
./bin/Debug/net9.0/SharePointVideoDownloader.exe -u "https://..."
```

Publish all release artefacts via `_publishAll.bat` from the repo root (Windows only — it uses `del`, `rd`, and `powershell Compress-Archive`). When bumping the version, update the hard-coded `v01.02` strings in that script and update the wording in `README.md` if the wording changes.

## Conventions and ground rules

- **Language**: code, identifiers, comments, commit messages, console output, and documentation are **English only**. The project is public on GitHub. The user may write to you in French — translate before committing anything.
- **Single-file philosophy**: keep new logic inside `Program.cs` unless splitting genuinely earns its keep. No premature abstractions, no helper projects.
- **Dependencies**: stay minimal. Currently only `PuppeteerSharp`. Adding a NuGet package needs a real reason — call it out explicitly in the PR/commit body.
- **No telemetry, no network calls beyond what the browser already does.** Privacy of the user's SharePoint sessions matters.
- **STRICTLY FORBIDDEN: do not commit access keys, secrets, tokens, or any private / personally identifiable information to git.** This includes (non-exhaustive): API keys, OAuth client secrets, Bearer / SPOPacToken / refresh tokens, session cookies (FedAuth, rtFa, EdgeAccessCookie), connection strings, private URLs, real tenant or user identifiers (email addresses, UPNs, employee IDs), real SharePoint / OneDrive paths, real meeting recording filenames, captured request/response payloads from a live session, screenshots showing any of the above. Test data in commits and docs must be obviously fictional (`https://your-sharepoint.com/...`, `user@example.com`). If you ever generate a `cookies.txt`, log file, sample HAR, or test artefact, **delete it before staging** — never `git add` it. If you suspect a secret has already been committed, stop and tell the user immediately so it can be rotated and rewritten out of history.
- **Cross-platform caveat**: the tool is effectively Windows-first (`yt-dlp.exe`, `_publishAll.bat`, `%LOCALAPPDATA%`). Don't break the Windows path; if you make it work on Linux/macOS too, that's a bonus.
- **Targeting Microsoft Stream / SharePoint UI**: this is the brittle layer. When playback selectors or the manifest URL marker change, update `possibleSelectors` and the `videomanifest?provider` / `index&format=dash` constants in `Program.cs` together, and document what changed.

## Things that commonly break (and where to look)

- **Direct download starts but never finishes**: poll loop is in `TryDirectDownloadAsync` in `Program.cs`. Watch for the `<file>.crdownload` partial file. If Chromium saved it under a different filename (Content-Disposition mismatch), update the `<a download>` JS injection to honor the server filename instead of forcing a name.
- **No `id` parameter, fallback fired**: the user passed a URL that is not a `stream.aspx?id=…` page (e.g., a direct video URL or a custom share link). Either accept the fallback or tell the user to use the SharePoint "Copy direct link" action.
- **Play button not found (legacy path only)**: `possibleSelectors` array in `Program.cs` — inspect the live page in DevTools, add a new CSS selector to the front of the list.
- **Manifest never captured (legacy path only)**: Microsoft may have renamed the request. Search for the literal `videomanifest?provider` and the trim marker `index&format=dash`; both live in `Program.cs`. Remember the OPTIONS preflight is now filtered out by checking `Request.Method` so we only capture headers from the real GET.
- **`yt-dlp` exits with `This video is DRM protected`**: that is the expected behaviour for current Microsoft Stream content and is why the primary path is the direct file download. There is no in-process workaround in the legacy yt-dlp path — yt-dlp has no CDM.
- **`yt-dlp` exits with HTTP 401 (legacy path only)**: the captured headers may have come from the OPTIONS preflight rather than the GET (look for `Access-Control-Request-*` in the forwarded list — that is the bug). The Response handler must be checking `Request.Method != HttpMethod.Options` before capturing.
- **Re-prompting for login**: `userDataDir` may have been wiped or is being shared across machines. The persisted Chromium profile lives at `%LOCALAPPDATA%\PuppeteerSession`.

## When making changes

- Keep the CLI surface stable: `-u/--url`, `-a/--audio`, `-o/--output`, `-h/--help`. Renaming or removing flags is a breaking change for users running scripts.
- If you touch the manifest-capture or URL-trim logic, also update the troubleshooting section of `README.md` so the public docs match reality.
- After non-trivial changes, do a manual end-to-end smoke test: run the app against a real (or recorded) SharePoint URL, ensure the manifest is captured, and confirm yt-dlp completes. There is no automated test suite to lean on.
- For releases, update the version in `_publishAll.bat` and the file-name examples in `README.md`. Do not commit the resulting ZIPs unless the user explicitly asks (they are typically uploaded to GitHub Releases instead).
