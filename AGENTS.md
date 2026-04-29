# AGENTS.md

Guidance for AI coding agents (Claude Code, Codex, etc.) working in this repository.

## Project at a glance

`SharePointVideoDownloader` is a small Windows-focused C# console app (.NET 9) that helps a logged-in user download Microsoft SharePoint / Stream videos (typically Teams meeting recordings).

The app drives a real Chromium instance with **PuppeteerSharp** and supports two strategies:

**Default — direct file download.** Used unless `-c / --capture` is passed.
1. Navigate to the SharePoint `stream.aspx?id=<path>` URL.
2. Parse the `id` query parameter to derive the underlying file path inside the user's OneDrive / SharePoint site.
3. Use `Page.setDownloadBehavior` over CDP to redirect Chromium downloads into the desired output directory.
4. Trigger the download by injecting an `<a download>` click pointing at `https://<host>/<path>?download=1`. Chromium handles auth, redirects, and `Content-Disposition` natively. The file that lands is the original non-DRM mp4.
5. Poll for `<file>.crdownload` (in-progress) and the final file to detect completion.
6. If the user passed `-a / --audio`, post-process via ffmpeg to extract an mp3 and delete the source mp4.

**`--capture` — in-browser screen + audio re-recording.** Used when direct download fails (403) or when the recording is view-only / DRM-protected.
1. Launch Chromium non-headless (DRM playback requires a real surface) but with `--window-position=-2400,-2400` so the window is invisible. `--use-fake-ui-for-media-stream` and `--auto-accept-this-tab-capture` suppress the getDisplayMedia picker. **Do NOT pass `--mute-audio`** — it mutes the captured audio as well, not just the speakers.
2. Navigate to the page and trigger play (mouse click on `<video>`, then Space, then synthetic JS click as fallbacks).
3. Inject a script that:
   - Hooks the `<video>` element's audio via `AudioContext.createMediaElementSource(video) → MediaStreamDestination` and never connects to `audioContext.destination`. Web Audio takes over routing entirely, so speakers stay silent. Audio capture is full-volume.
   - Probes `canvas.drawImage(video)` on a center 8×8 patch. If pixels come back non-zero, runs a `requestVideoFrameCallback` loop to draw frames at native `videoWidth × videoHeight` and exposes them via `canvas.captureStream()` — full source resolution, no UI chrome. If the probe is all-zero or throws (EME taint), falls back to `getDisplayMedia` tab capture cropped to the `<video>` element's bounds.
   - Combines the canvas video track + Web Audio audio track into a `MediaRecorder` (VP9 + Opus).
   - On `<video>.ended` (or after `--capture-seconds`) stops the recorder, builds a Blob, and triggers an `<a download>` click so the file lands in the directory configured via `Page.setDownloadBehavior`.
4. Wait for the file to land (`<file>.crdownload` → final).
5. ffmpeg post-processes:
   - `-c copy` remux to add a Cues element so the webm is seekable;
   - `libx264 + aac + faststart` transcode if the user requested `.mp4`;
   - `-vn libmp3lame` audio extraction if `-a / --audio`.
   ffmpeg is a soft dependency: the program prints a warning at startup if it cannot find ffmpeg, but capture still produces a raw webm.

Authentication is delegated to the browser session — the app never types passwords. The first run is non-headless so the user can sign in to Microsoft 365, and the session is persisted via PuppeteerSharp's `UserDataDir` (defaults to `%LOCALAPPDATA%\PuppeteerSession`).

## Repository layout

| Path | Purpose |
| --- | --- |
| `Program.cs` | The entire application — argument parsing, browser orchestration, direct download, capture, ffmpeg post-processing. Single-file by design. |
| `SharePointVideoDownloader.csproj` | .NET 9, references `PuppeteerSharp` only. |
| `SharePointVideoDownloader.sln` | Visual Studio solution. |
| `Dependencies/` | Folder copied verbatim into `*-DotNet-Dependencies` release ZIPs. Currently empty (used to hold a bundled `yt-dlp.exe`; we now rely on user-installed ffmpeg via PATH). |
| `Releases/` | Pre-built ZIPs published on GitHub Releases (DotNet, x64/x86/ARM64 self-contained). |
| `_publishAll.bat` | Windows build/zip. Hard-codes the version string. |
| `_publishAll.sh` | macOS / Linux build/zip. Same release flavours plus `osx-x64`, `osx-arm64`, `linux-x64`. |
| `README.md` | End-user documentation. |
| `LICENSE` | MIT. |

There is intentionally no `src/`, no test project, no service abstractions — adding any of those would be over-engineering for a tool this size.

## How the core flow is implemented (`Program.cs`)

Anchor points if you need to edit:

- **CLI parsing**: `Main` walks `args` manually with a `switch` on `-u/--url`, `-a/--audio`, `-o/--output`, `-c/--capture`, `--capture-seconds`, `-v/--visible`, `-h/--help`. Falls back to interactive prompts when arguments are absent or invalid. See `ShowHelp()` for the canonical surface.
- **ffmpeg startup check**: `WarnIfFfmpegMissing` (called from `Main` after argument parsing) prints a yellow heads-up if ffmpeg is not on PATH and not next to the exe. The program continues regardless; users get a webm without seek index, and `-a` becomes a no-op.
- **Output filename normalisation**: warns when the extension does not match the chosen mode (e.g., requested mp4 with `--audio`).
- **Smart headless** (`ShouldRunHeadless()`): if `userDataDir/Default/Cookies` or `userDataDir/Default/Network` exists, the user has already logged in once and we run headless. Otherwise visible. `-v / --visible` overrides to force visible. **Capture mode always runs non-headless** (DRM CDM requires a real surface) but the window is pushed off-screen so it stays invisible.
- **Browser launch**: `Puppeteer.LaunchAsync` with `Headless = runHeadless` (computed per above), `--no-sandbox`, and `UserDataDir = userDataDir` for session persistence. Capture mode appends `--use-fake-ui-for-media-stream`, `--auto-accept-this-tab-capture`, `--auto-select-tab-capture-source-by-title=spvd-capture`, and `--window-position=-2400,-2400`. `BrowserFetcher.DownloadAsync()` ensures Chromium is present on first run.
- **`TryDirectDownloadAsync`**: see "Default" strategy above. Starts the file via injected `<a download>`, polls for `.crdownload` to detect progress and final file presence. If `-a` was set, calls `ExtractAudioMp3Async` after the video lands.
- **`TryCaptureViaPlaybackAsync`**: see "--capture" strategy above. State machine in JS exposes `window.__spvd_status()` (returns JSON snapshot), polled by C# every 2 s. Includes diagnostic fields (`audioPathway`, `videoSource`, `canvasProbeNonZero`, dim, recorder state, video paused flag and currentTime).
- **`PostProcessCaptureAsync`**: ffmpeg remux (`-c copy`) → seekable webm; conditional H.264/AAC transcode if `-o foo.mp4` was requested. Soft-fails (warns and keeps raw webm) if ffmpeg is missing or returns non-zero.
- **`ExtractAudioMp3Async`**: `-vn libmp3lame -q:a 2` from any video file the previous step produced; deletes source on success.
- **`LocateFfmpeg` / `RunFfmpegAsync`**: shared helpers used by all post-processing. `LocateFfmpeg` is OS-aware (probes both `ffmpeg.exe` and `ffmpeg`) and checks `Environment.ProcessPath`'s directory first, then PATH, then macOS Homebrew defaults (`/opt/homebrew/bin`, `/usr/local/bin`).

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
- **Cross-platform**: code paths assume Windows + Unix work (the smart-headless detection, `LocateFfmpeg`, OS-aware Homebrew probe, `_publishAll.sh`). The `userDataDir` resolves correctly on all three OSes via `Environment.SpecialFolder.LocalApplicationData`. Do not break the Windows path when adding macOS/Linux affordances.
- **Targeting Microsoft Stream / SharePoint UI**: this is the brittle layer. The two literals you most likely need to touch are `stream.aspx`'s `id=` query parameter (consumed by `TryDirectDownloadAsync`) and the `<video>` selector / SharePoint click handler used by `TryCaptureViaPlaybackAsync`.

## Things that commonly break (and where to look)

- **Direct download starts but never finishes**: poll loop is in `TryDirectDownloadAsync`. Watch for the `<file>.crdownload` partial file. If Chromium saved it under a different filename (Content-Disposition mismatch), update the `<a download>` JS injection to honor the server filename instead of forcing a name.
- **No `id` parameter — direct download bails early**: the user passed a URL that is not a `stream.aspx?id=…` page (e.g., a custom share link). Tell them to use the URL from SharePoint's address bar with `Copy Link → View only`, which always carries `id=`.
- **Capture stays at `paused=True`**: the click on `<video>` did not start playback. The current code tries mouse click → Space key → synthetic JS click in that order. If all three fail, inspect the live page in DevTools (you can run capture mode without `--window-position` to keep the window on-screen). The SharePoint player wraps `<video>` in React handlers; the actual play trigger may be on a sibling/overlay div in some layouts.
- **Capture audio is silent (mean ~ -inf dB) but the script ran**: Web Audio hook may have been blocked by EME on some content. The `audioPathway` field in the diagnostic line tells you whether the hook was set up. Fallbacks beyond Web Audio (which we have not needed yet on Microsoft content) are: VB-Cable virtual audio device, Windows 11 per-process loopback. Both require user-side setup; document accordingly if you wire them in.
- **Capture video is all-black or stays at one frame**: the canvas-direct probe came back zero (EME taint). The code automatically falls back to `getDisplayMedia` tab capture cropped to `<video>` bounds; check `videoSource` in the diagnostic line. Tab capture loses some resolution (CSS px of the page viewport instead of source `videoWidth × videoHeight`) but is otherwise reliable.
- **ffmpeg-related warnings**: `WarnIfFfmpegMissing` prints them at startup. If a build pipeline complains about missing post-processing, install ffmpeg or set up a sibling `ffmpeg.exe`. The tool itself never errors out because of missing ffmpeg.
- **Re-prompting for login**: `userDataDir` may have been wiped or is being shared across machines. The persisted Chromium profile lives at `%LOCALAPPDATA%\PuppeteerSession`.

## When making changes

- Keep the CLI surface stable: `-u/--url`, `-a/--audio`, `-o/--output`, `-c/--capture`, `--capture-seconds`, `-v/--visible`, `-h/--help`. Renaming or removing flags is a breaking change for users running scripts.
- If you touch direct download, capture, or ffmpeg logic, update the troubleshooting section of `README.md` so the public docs match reality.
- After non-trivial changes, do a manual end-to-end smoke test against a real SharePoint URL with the operator's persisted session. Verify both `directOk=true` (downloaded mp4 plays) and `--capture` (raw webm exists, ffmpeg-remuxed webm seeks, mp4 transcode plays). There is no automated test suite.
- Always delete real-content test artefacts before staging — they fall under the secrets rule.
- For releases, update the version in `_publishAll.bat` and the file-name examples in `README.md`. Do not commit the resulting ZIPs unless the user explicitly asks (they are typically uploaded to GitHub Releases instead).
