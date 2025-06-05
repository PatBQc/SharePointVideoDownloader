# SharePoint/Stream Video Downloader (Puppeteer & yt-dlp)

This C# console application automates the process of downloading videos hosted on Microsoft SharePoint or Stream (specifically targeting the type used for Teams meeting recordings), particularly when you are not the meeting organizer but have viewing permissions.

It uses Puppeteer Sharp to control a headless (or visible) browser to:
1.  Navigate to the video page.
2.  Simulate starting video playback.
3.  Intercept network requests to find the hidden `videomanifest` URL.
4.  Process the manifest URL according to a specific algorithm.
5.  Pass the processed URL to `yt-dlp` to handle the actual video download.

**Disclaimer:** Only use this tool to download videos you have legitimate access rights and permissions to view and download. Respect privacy and organizational policies.

## Features

*   Automates browser interaction to find the video manifest.
*   Attempts to automatically click the play button to trigger manifest loading.
*   Listens for and captures the specific `videomanifest` network request.
*   Processes the captured URL to make it compatible with `yt-dlp`.
*   Uses the powerful `yt-dlp` tool for reliable video downloading.
*   Provides console feedback during the process.
*   Configurable headless mode and `yt-dlp` path.
*   **Command-line interface:** Supports passing URL, output filename, and audio-only preference via arguments.
*   **Audio-only downloads:** Option to download only the audio track as an MP3 file.

## Prerequisites

1.  **.NET SDK:** .NET 6 or later installed. ([Download .NET](https://dotnet.microsoft.com/download))
    *   Note: If you plan to use a pre-compiled "Self-Contained" release, you do not need to install the .NET SDK or Runtime. If you use a "DotNet" or "DotNet-Dependencies" release, you will need the .NET Desktop Runtime (version 6 or later).
2.  **yt-dlp:** The `yt-dlp` executable must be available.
    *   **Option A (Recommended):** Download `yt-dlp.exe` (or the binary for your OS) from the [yt-dlp Releases page](https://github.com/yt-dlp/yt-dlp/releases) and place it in the same directory where this application's executable will run (e.g., `bin/Debug/netX.Y/`).
    *   **Option B:** Add the directory containing `yt-dlp` to your system's `PATH` environment variable.
    *   **Option C:** Modify the `YtDlpPath` constant in `Program.cs` to point to the full path of your `yt-dlp` executable.
3.  **Web Browser:** Puppeteer Sharp will download a compatible version of Chromium by default on the first run. Alternatively, you can configure it to use an existing Chrome/Edge installation (see Configuration).
4.  **Microsoft 365/SharePoint Authentication:** **Crucially, you must be logged into your relevant Microsoft account in the browser profile Puppeteer uses.** The script *does not* handle the login process itself. See the [Authentication Note](#important-notes) below.

## Installation & Setup

1.  **Clone the Repository:**
    ```bash
    git clone https://github.com/PatBQc/SharePointVideoDownloader
    cd SharePointVideoDownloader
    ```
2.  **Place `yt-dlp`:** Ensure `yt-dlp.exe` (or equivalent) is accessible as described in [Prerequisites](#prerequisites).
3.  **Build the Project:**
    ```bash
    dotnet build
    ```
    (This will restore NuGet packages, including Puppeteer Sharp).

## Downloading Pre-compiled Releases

For users who prefer not to build the project from source, pre-compiled versions are available for download from the [Releases page](https://github.com/PatBQc/SharePointVideoDownloader/releases).

Here's a brief explanation of the different versions available (replace `vXX.XX` with the actual latest version number):

*   **`SharePointVideoDownloader-vXX.XX-DotNet.zip`**:
    *   **This is likely the simplest option for most users on Windows.**
    *   It requires the **.NET Desktop Runtime (version 6 or later) to be installed**, similar to the `DotNet-Dependencies` version.
    *   This version is typically a "framework-dependent deployment" which is smaller and relies on a globally installed .NET runtime.

*   **`SharePointVideoDownloader-vXX.XX-ARM64-Self-Contained.zip`**:
    *   This version is for computers with **ARM64 processors** (e.g., some newer Windows laptops, Apple Silicon Macs running Windows via Parallels).
    *   It is "self-contained," meaning it includes the .NET runtime and all necessary dependencies. You do **not** need to have .NET installed separately.
    *   The file size will be larger due to the included runtime.

*   **`SharePointVideoDownloader-vXX.XX-DotNet-Dependencies.zip`**:
    *   This version requires you to have the **.NET Desktop Runtime (version 6 or later) already installed** on your system. You can download it from [Microsoft's .NET download page](https://dotnet.microsoft.com/download/dotnet/6.0) (look for the "Desktop Runtime" installer for your OS).
    *   It only contains the application files and its direct dependencies, not the .NET runtime itself.
    *   The file size is smaller than self-contained versions.

*   **`SharePointVideoDownloader-vXX.XX-x64-Self-Contained.zip`**:
    *   This version is for computers with standard **64-bit Intel/AMD processors (x64 architecture)**, which is the most common type for modern Windows PCs.
    *   It is "self-contained," including the .NET runtime and all dependencies. No separate .NET installation is needed.
    *   Larger file size.

*   **`SharePointVideoDownloader-vXX.XX-x86-Self-Contained.zip`**:
    *   This version is for older computers with **32-bit Intel/AMD processors (x86 architecture)**.
    *   It is "self-contained," including the .NET runtime and all dependencies. No separate .NET installation is needed.
    *   Larger file size.

**Recommendation:** If you have the .NET 6 (or newer) Desktop Runtime installed, the `SharePointVideoDownloader-vXX.XX-DotNet.zip` version is generally the easiest to use and has a smaller download size. If you are unsure or prefer not to install .NET separately, choose the "Self-Contained" version appropriate for your computer's architecture (most likely `x64-Self-Contained` for modern PCs).

After downloading, extract the ZIP file to a folder of your choice and run `SharePointVideoDownloader.exe`. Ensure `yt-dlp.exe` is also present in the same folder or accessible via your system's PATH (see [Prerequisites](#prerequisites) for `yt-dlp`).

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
    *   `-a, --audio`: (Optional) Download audio only (MP3). Defaults to video (MP4) if not specified.
    *   `-o, --output <FILENAME>`: (Optional) Desired output filename (e.g., `my_video.mp4` or `my_audio.mp3`).
        *   If not provided, a default name will be generated.
        *   The correct extension (`.mp4` or `.mp3`) will be enforced or appended if missing.
    *   `-h, --help, -?, /?`: Display the help message with all options.

*   **Examples:**
    *   Download a video with a specific output name:
        ```bash
        # Using the executable
        .\SharePointVideoDownloader.exe -u "https://your-sharepoint.com/video/123" -o "project_update.mp4"
        # Using dotnet run (note the -- before arguments)
        dotnet run -- -u "https://your-sharepoint.com/video/123" -o "project_update.mp4"
        ```
    *   Download audio only with a specific output name:
        ```bash
        .\SharePointVideoDownloader.exe --url "https://your-stream-link.com/vid/abc" --audio --output "interview_audio.mp3"
        ```
    *   Download a video using the default output name:
        ```bash
        .\SharePointVideoDownloader.exe -u "https://url.com/another_video"
        ```
    *   Display help:
        ```bash
        .\SharePointVideoDownloader.exe --help
        ```

**3. Browser Interaction (if not headless):**
*   If `RunHeadless` is `false` in `Program.cs` (default for easier first-time use/debugging), a browser window will open.
*   **First Run / Authentication:** If you are not logged into Microsoft 365 in this browser profile, you will likely see a login page. **Log in manually within this Puppeteer-controlled browser window.** Puppeteer might reuse this session for subsequent runs if you enable `UserDataDir` in the code.
*   The script will attempt to find and click the play button.
    
**4.  Monitoring:** 
The console will show progress messages: launching the browser, navigating, waiting for the manifest, processing the URL, and the output from `yt-dlp` during the download.

**5.  Completion:** 
Once `yt-dlp` finishes, a success or error message will be displayed, and the video file should be present in the application's execution directory (or the location specified in the `-o` path if you modify the `yt-dlp` arguments).

## Configuration (in `Program.cs`)

You can adjust the behavior by modifying constants at the top of `Program.cs`:

*   `YtDlpPath`: Sets the path to the `yt-dlp` executable. Defaults to `yt-dlp.exe` (expecting it in the same directory or PATH).
*   `RunHeadless`: Set to `true` to run the browser invisibly in the background. Set to `false` (default) to see the browser window, which is useful for debugging and initial login.
*   `possibleSelectors` (array within `Main` method): If the script fails to click the play button, the selectors used to find it might be outdated. You can inspect the video page elements (using browser DevTools - F12) and update this array with new CSS selectors for the play button or video element.
*   `userDataDir` (commented out): You can uncomment and set a path here to make Puppeteer use a persistent browser profile directory. This can help maintain login sessions between runs but requires careful path management.
*   `launchOptions.ExecutablePath` (commented out): Uncomment and set this to use an existing Chrome/Edge installation instead of Puppeteer downloading Chromium.

## Important Notes

*   **Authentication:** This script **DOES NOT** automate the Microsoft login process. You **must** be logged in already. The easiest way is often to run with `RunHeadless = false` the first time and log in manually in the window that Puppeteer opens.
*   **Legality & Permissions:** Ensure you have the necessary permissions to view and download the videos you target with this script. Adhere to your organization's policies regarding data handling and downloads.
*   **UI Changes:** Microsoft frequently updates the web interface for SharePoint and Stream. If the script stops working (e.g., cannot find the play button or the manifest URL pattern changes), the selectors or the manifest URL processing logic in `Program.cs` may need updating.
*   **Error Handling:** The script includes basic error handling, but complex scenarios or unexpected page states might cause failures. Check the console output for error messages from both the C# application and `yt-dlp`.
*   **`yt-dlp` Updates:** Keep your `yt-dlp` executable updated, as streaming sites often change their methods, and `yt-dlp` is frequently updated to keep pace. (`yt-dlp -U` in your command line).

## Troubleshooting

*   **`yt-dlp` not found:** Verify the `YtDlpPath` in `Program.cs` is correct, or that `yt-dlp` is in the application's directory or your system PATH.
*   **Manifest URL not found / Timeout:**
    *   Are you logged into the correct Microsoft account in the browser profile used by Puppeteer? Try running with `RunHeadless = false` and log in manually.
    *   Did the video page load correctly? Is the provided URL correct?
    *   Did the script successfully click "Play"? If not, check the `possibleSelectors` in the code.
    *   Microsoft might have changed the URL structure for `videomanifest`. Check the Network tab in your browser's DevTools (F12) manually to see if the URL pattern still matches `videomanifest?provider`.
*   **Login screen appears repeatedly:** You might need to configure a persistent `UserDataDir` in the Puppeteer `LaunchOptions` within `Program.cs` to maintain the login session across runs.
*   **`yt-dlp` errors:** Check the `[yt-dlp ERR]` messages in the console. The issue might be with `yt-dlp` itself, the processed URL, or network connectivity. Try running the `yt-dlp` command manually with the shortened URL printed by the script to isolate the problem.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.


## TODO

- [x] Take the Sharepoint URL from the CLI and have the same signature as yt-dlp (url -o filename) - *Implemented with `-u/--url`, `-o/--output`, and `-a/--audio` flags.*
- [x] Make sure that if I login once, then I am logged the next time around. - *Partially addressed by `userDataDir` option in `Program.cs` for persistent sessions.*
