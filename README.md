# SharePoint/Stream Video Downloader

This C# console application automates the process of saving videos hosted on Microsoft SharePoint / Stream (typically Teams meeting recordings) to a local file — including recordings shared with you in **view-only** mode where the SharePoint UI does not offer a Download button.

It drives a Chromium instance via Puppeteer Sharp and tries two strategies:

1.  **Direct file download (default, fast).** Parses the `id` query parameter from the `stream.aspx` URL, derives the underlying SharePoint file path, and asks Chromium to fetch the original mp4 from `https://<host>/<path>?download=1`. This works whenever you have the Download permission on the file (your own recordings, or shares that grant Download). The original mp4 is non-DRM and matches what the SharePoint "Download" button would give you.
2.  **In-browser capture (`-c` / `--capture`, real-time).** Re-records the playing video by routing the page's `<video>` element through Web Audio for audio (so the speakers stay silent) and through a canvas + `MediaRecorder` for video (full source resolution). Use this when the meeting is shared with you in view-only mode and is DRM-protected — the only situation where direct download is not possible. Capture runs in real time: a one-hour meeting takes one hour to record. The Chromium window is positioned off-screen so it is not visually disruptive.

**Disclaimer:** Only use this tool to download videos you have legitimate access rights and permissions to view and download. Respect privacy and organizational policies.

## Features

*   Two extraction strategies (direct download, capture) chosen automatically or by flag.
*   Persistent Chromium profile: log into Microsoft 365 once, the session is reused.
*   Browser-side capture stays silent (no audio in your speakers) and invisible (window kept off-screen).
*   Optional ffmpeg post-processing: webm → seekable webm, transcode to .mp4, audio-only mp3.
*   Single-file C# console app, no service, no telemetry.

## Prerequisites

1.  **.NET Desktop Runtime 9** (for the framework-dependent release ZIPs). Self-Contained release ZIPs bundle the runtime and need nothing extra. ([Download .NET](https://dotnet.microsoft.com/download))
2.  **ffmpeg** *(optional but recommended)*. The application calls `ffmpeg` to:
    *   add a seek index to the raw `.webm` produced by capture mode (without it the file plays in VLC but most other players cannot scrub through it);
    *   transcode `.webm` → `.mp4` when you ask for `-o foo.mp4`;
    *   extract audio to `.mp3` when you pass `--audio`.
    Install via `winget install Gyan.FFmpeg` or `choco install ffmpeg`, or unzip a static build from <https://ffmpeg.org/download.html> and either add it to PATH or drop `ffmpeg.exe` into the same folder as `SharePointVideoDownloader.exe`. The startup will warn you if ffmpeg is missing.
3.  **Web Browser:** Puppeteer Sharp downloads a compatible version of Chromium on the first run.
4.  **Microsoft 365 / SharePoint Authentication:** **You must be logged into your Microsoft account in the Chromium window Puppeteer launches.** The first run is non-headless on purpose — sign in there once, and the session persists in `%LOCALAPPDATA%\PuppeteerSession` for subsequent runs.

## Installation & Setup

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/PatBQc/SharePointVideoDownloader
    cd SharePointVideoDownloader
    ```
2.  *(Optional)* Install ffmpeg as described in Prerequisites if you want seekable webm, .mp4 output, or .mp3 audio extraction.
3.  **Build the Project:**
    ```bash
    dotnet build
    ```
    (This will restore NuGet packages, including Puppeteer Sharp.)

## Downloading Pre-compiled Releases

For users who prefer not to build the project from source, pre-compiled versions are available for download from the [Releases page](https://github.com/PatBQc/SharePointVideoDownloader/releases).

Here's a brief explanation of the different versions available (replace `vXX.XX` with the actual latest version number):

*   **`SharePointVideoDownloader-vXX.XX-DotNet.zip`**:
    *   **This is likely the simplest option for most users on Windows.**
    *   It requires the **.NET 9 Desktop Runtime to be installed**.
    *   This version is a "framework-dependent deployment": small download, but relies on a globally installed .NET runtime.

*   **`SharePointVideoDownloader-vXX.XX-ARM64-Self-Contained.zip`**:
    *   For computers with **ARM64 processors** (some newer Windows laptops, Apple Silicon Macs running Windows via Parallels).
    *   "Self-contained": includes the .NET runtime, no separate install needed.
    *   Larger file size.

*   **`SharePointVideoDownloader-vXX.XX-x64-Self-Contained.zip`**:
    *   For computers with standard **64-bit Intel/AMD processors (x64)**, the most common modern PC.
    *   "Self-contained", includes the .NET runtime.
    *   Larger file size.

*   **`SharePointVideoDownloader-vXX.XX-x86-Self-Contained.zip`**:
    *   For older computers with **32-bit Intel/AMD processors (x86)**.
    *   "Self-contained", includes the .NET runtime.
    *   Larger file size.

**Recommendation:** If you already have the .NET 9 Desktop Runtime installed, the `DotNet.zip` version is the easiest and smallest. Otherwise pick the `Self-Contained` variant for your architecture (most likely `x64-Self-Contained`).

After downloading, extract the ZIP file to a folder of your choice and run `SharePointVideoDownloader.exe`. If you want capture mode to produce a seekable file or an `.mp4` / `.mp3` instead of raw `.webm`, drop `ffmpeg.exe` into the same folder (or install it on PATH). The startup banner will tell you if ffmpeg was not found.

## Usage

The application can be run in two modes: interactive (default) or via command-line arguments.

**1. Interactive Mode (No Arguments):**

*   Run the application without any command-line arguments:
    *   From the project directory:
        ```bash
        dotnet run
        ```
    *   Or navigate to the output directory (e.g., `bin/Debug/netX.Y/`) and run the executable directly:
        ```bash
        # On Windows
        .\SharePointVideoDownloader.exe

        # On Linux/macOS
        ./SharePointVideoDownloader
        ```
*   **Enter Video URL:** When prompted, paste the full URL of the SharePoint/Stream page containing the video.
    *   Example: `https://yourtenant-my.sharepoint.com/personal/user_domain_com/_layouts/15/stream.aspx?id=%2F...`
*   **Select Download Type:** Choose whether to download the full video (default) or audio only.
*   **Enter Output Filename:** When prompted, enter the desired name for the downloaded file (e.g., `meeting_recording.mp4` or `podcast_audio.mp3`).
    *   If you omit the extension, `.mp4` (for video) or `.mp3` (for audio) will be appended.
    *   If left blank, a default filename with a timestamp will be used.

**2. Command-Line Mode:**

You can provide arguments to specify the URL, output filename, and whether to download audio only. This is useful for scripting or direct execution.

*   **Syntax:**
    ```
    SharePointVideoDownloader.exe [options]
    ```
    or
    ```
    dotnet run -- [options]
    ```

*   **Available Options:**
    *   `-u, --url <URL>`: **(Required)** The SharePoint/Stream video page URL.
        *   **Important:** Enclose the URL in "double quotes" if it contains special characters like `&` or `=`.
    *   `-c, --capture`: (Optional) Skip the direct-download attempt and go straight to in-browser capture. Use this for view-only / DRM-protected stream pages. Capture runs in real time.
    *   `--capture-seconds <N>`: (Optional, capture mode only) Stop the recording after N seconds. Useful for testing the capture pipeline without committing to a full meeting length.
    *   `-a, --audio`: (Optional) Produce an MP3 instead of a video file. Requires ffmpeg to be available.
    *   `-o, --output <FILENAME>`: (Optional) Desired output filename. The container extension (`.mp4`, `.webm`, `.mp3`) is honoured when ffmpeg is available; without ffmpeg, capture mode keeps the raw `.webm`.
    *   `-h, --help, -?, /?`: Display the help message.

*   **Examples:**
    *   Download a meeting you own (fast, returns the original mp4):
        ```bash
        .\SharePointVideoDownloader.exe -u "https://your-tenant-my.sharepoint.com/.../stream.aspx?id=..." -o "meeting.mp4"
        ```
    *   Record a view-only meeting that was shared with you (real-time):
        ```bash
        .\SharePointVideoDownloader.exe -u "..." -o "meeting.mp4" --capture
        ```
    *   Audio-only output (mp3, requires ffmpeg):
        ```bash
        .\SharePointVideoDownloader.exe -u "..." -o "meeting.mp3" --audio
        ```
    *   Test the capture pipeline against your own URL with a 30-second cap:
        ```bash
        .\SharePointVideoDownloader.exe -u "..." -o "test.mp4" --capture --capture-seconds 30
        ```

**3. Browser Interaction:**
*   On the first run, a Chromium window will open. **Log into Microsoft 365 manually** inside that window. The session is persisted in `%LOCALAPPDATA%\PuppeteerSession`, so subsequent runs do not need a fresh login.
*   Capture mode positions the window off-screen (`--window-position=-2400,-2400`) so it is not visually disruptive. The DRM CDM still requires a real, non-headless rendering surface, so the window genuinely exists — just out of sight.

**4.  Monitoring:**
The console reports progress: ffmpeg availability check, browser launch, navigation, the path being used (direct download or capture), and any post-processing (remux / transcode / mp3 extraction).

**5.  Completion:**
A green ✓ line tells you where the file was saved. Capture mode also prints intermediate steps (`Remuxed (seekable)`, `.mp4 ready`).

## Configuration (in `Program.cs`)

You can adjust the behavior by modifying constants at the top of `Program.cs`:

*   `RunHeadless`: Set to `true` to run the browser invisibly in the background for the direct-download path. Capture mode always runs non-headless (DRM playback requires a real surface) but pushes the window off-screen so you do not see it.
*   `userDataDir`: Path of the persistent Chromium profile. Defaults to `%LOCALAPPDATA%\PuppeteerSession`.

## Important Notes

*   **Authentication:** This tool **does not** automate the Microsoft login process. You **must** be logged in already in the persistent Chromium profile. Run once, sign in inside the Puppeteer window, then re-run to actually download.
*   **Legality & Permissions:** Only use this on content you have legitimate rights to. The capture path simply re-records what your browser is already lawfully rendering — it does not bypass DRM or extract keys. Respect your organization's data-handling policies.
*   **UI Changes:** Microsoft frequently changes the SharePoint Stream UI. If direct download fails because the URL no longer carries an `id=…` parameter, or capture stops finding `<video>`, those literals live in `Program.cs` and may need updating.
*   **Capture is real-time:** A one-hour meeting takes one hour to record. Plan accordingly.

## Troubleshooting

*   **Direct download succeeded but I expected capture to run:** That just means you have Download permission on the file (i.e., you own it or were granted Download access). Direct download is faster and produces the higher-quality original mp4 — let it work. Use `-c / --capture` only when direct download fails.
*   **Direct download failed and the program told me to use `--capture`:** Re-run with `-c`. You probably have a view-only / DRM-protected share.
*   **`ffmpeg was not found in PATH or alongside this executable.`:** Install ffmpeg (`winget install Gyan.FFmpeg`) and either add its `bin` folder to PATH or drop `ffmpeg.exe` next to `SharePointVideoDownloader.exe`. The capture path will still produce a `.webm`, but it may not be seekable in some players, `-o foo.mp4` cannot be honoured, and `--audio` (mp3) will be skipped.
*   **Capture: WARNING — could not confirm playback started:** The Puppeteer click on the `<video>` element did not start playback. Sometimes the SharePoint UI requires a different selector — try running once with the window on-screen (remove the `--window-position` arg in `Program.cs`) to see what is happening.
*   **Login screen appears repeatedly:** Something is wiping `%LOCALAPPDATA%\PuppeteerSession` between runs (anti-virus cleanup, manual deletion, profile change). Ensure that directory persists.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.


## TODO

- [x] Take the Sharepoint URL from the CLI (`url -o filename` style) - *Implemented with `-u/--url`, `-o/--output`, `-a/--audio`, `-c/--capture`, `--capture-seconds`.*
- [x] Make sure that if I login once, then I am logged the next time around. - *Partially addressed by `userDataDir` option in `Program.cs` for persistent sessions.*
