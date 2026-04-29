# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, etc.) working in this repository.

## Project at a glance

`SharePointVideoDownloader` is a small Windows-focused C# console app (.NET 9) that helps a logged-in user download Microsoft SharePoint / Stream videos (typically Teams meeting recordings) by:

1. Driving a real Chromium instance with **PuppeteerSharp**.
2. Navigating to the video page and clicking play to trigger media loading.
3. Listening on the network for the hidden `videomanifest?provider=...` request.
4. Truncating that URL up to and including `index&format=dash`.
5. Handing the trimmed URL to **`yt-dlp.exe`** as a child process to perform the actual download (or audio extraction with `-x --audio-format mp3`).

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
- **No secrets, tenants, real URLs, or session data in the repo.** Test URLs in commits and docs must be obviously fake (`https://your-sharepoint.com/...`). The user's real meeting URLs are confidential — never paste them into source files, READMEs, sample configs, or commit messages.
- **Cross-platform caveat**: the tool is effectively Windows-first (`yt-dlp.exe`, `_publishAll.bat`, `%LOCALAPPDATA%`). Don't break the Windows path; if you make it work on Linux/macOS too, that's a bonus.
- **Targeting Microsoft Stream / SharePoint UI**: this is the brittle layer. When playback selectors or the manifest URL marker change, update `possibleSelectors` and the `videomanifest?provider` / `index&format=dash` constants in `Program.cs` together, and document what changed.

## Things that commonly break (and where to look)

- **Play button not found**: `possibleSelectors` array in `Program.cs` — inspect the live page in DevTools, add a new CSS selector to the front of the list.
- **Manifest never captured (60 s timeout)**: Microsoft may have renamed the request. Search for the literal `videomanifest?provider` and the trim marker `index&format=dash`; both live in `Program.cs`.
- **`yt-dlp` exits non-zero**: re-run yt-dlp manually with the printed shortened URL to isolate the failure. Common causes are an outdated `yt-dlp.exe`, a manifest URL that requires browser cookies/auth that yt-dlp cannot replay, or shell quoting issues when the URL contains `&` (always quote URLs on the command line).
- **Re-prompting for login**: `userDataDir` may have been wiped or is being shared across machines. The persisted Chromium profile lives at `%LOCALAPPDATA%\PuppeteerSession`.

## When making changes

- Keep the CLI surface stable: `-u/--url`, `-a/--audio`, `-o/--output`, `-h/--help`. Renaming or removing flags is a breaking change for users running scripts.
- If you touch the manifest-capture or URL-trim logic, also update the troubleshooting section of `README.md` so the public docs match reality.
- After non-trivial changes, do a manual end-to-end smoke test: run the app against a real (or recorded) SharePoint URL, ensure the manifest is captured, and confirm yt-dlp completes. There is no automated test suite to lean on.
- For releases, update the version in `_publishAll.bat` and the file-name examples in `README.md`. Do not commit the resulting ZIPs unless the user explicitly asks (they are typically uploaded to GitHub Releases instead).
