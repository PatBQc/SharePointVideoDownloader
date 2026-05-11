using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PuppeteerSharp;

class Program
{
    // --- Configuration ---
    // Persistent Puppeteer profile so that the user only needs to log into
    // Microsoft 365 once. The presence of a "Default/Cookies" file inside this
    // directory is also our signal for "smart headless" detection — see
    // ShouldRunHeadless() below.
    static string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuppeteerSession");
    // --- End Configuration ---

    // Smart headless detection (originally contributed in
    // https://github.com/mmueller22/SharePointVideoDownloader). Returns true
    // when the persisted Chromium profile already contains a Default/Cookies
    // file or Default/Network folder, which is a reliable proxy for "the user
    // has already logged into Microsoft 365 in this profile". On a fresh
    // install we return false so the user can see the browser, sign in, and
    // complete MFA. Capture mode overrides this and always runs non-headless
    // because the PlayReady CDM requires a real, non-headless rendering
    // surface — but we still push the window off-screen so it is invisible.
    static bool ShouldRunHeadless()
    {
        try
        {
            if (Directory.Exists(userDataDir))
            {
                string defaultFolder = Path.Combine(userDataDir, "Default");
                string cookiesFile = Path.Combine(defaultFolder, "Cookies");
                string networkFolder = Path.Combine(defaultFolder, "Network");
                if (Directory.Exists(defaultFolder) &&
                    (File.Exists(cookiesFile) || Directory.Exists(networkFolder)))
                {
                    Console.WriteLine("Existing Microsoft 365 session detected — running headless.");
                    return true;
                }
            }
        }
        catch { /* fall through to visible */ }

        Console.WriteLine("No saved Microsoft 365 session found — running with visible browser so you can sign in.");
        return false;
    }

    static void ShowHelp()
    {
        Console.WriteLine("SharePoint/Stream Video Downloader Usage:");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("Two strategies are tried automatically:");
        Console.WriteLine("  1. Direct download    - fast, returns the original mp4. Used by default.");
        Console.WriteLine("                          Requires Download permission on the file (your own");
        Console.WriteLine("                          recordings, or shares that grant Download).");
        Console.WriteLine("  2. Capture (-c)       - re-records the playing video. Real-time (a 1 h meeting");
        Console.WriteLine("                          takes 1 h). Use this for view-only / DRM-protected");
        Console.WriteLine("                          recordings shared with you, where direct download");
        Console.WriteLine("                          returns 403 or the stream is PlayReady-protected.");
        Console.WriteLine();
        Console.WriteLine("Optional dependency: ffmpeg in PATH (or alongside this exe). When present, the");
        Console.WriteLine("capture path produces a seekable file and can transcode to .mp4, and --audio");
        Console.WriteLine("can extract an .mp3. Without ffmpeg, you still get a .webm but it may not be");
        Console.WriteLine("seekable in some players. The startup will print a heads-up if ffmpeg is missing.");
        Console.WriteLine();
        Console.WriteLine("Interactive mode (no arguments):");
        Console.WriteLine("  The program will prompt you for URL, download type, and output filename.");
        Console.WriteLine();
        Console.WriteLine("Command-line arguments:");
        Console.WriteLine("  -u, --url <URL>         : (Required) The SharePoint/Stream video page URL.");
        Console.WriteLine("                            Important: enclose the URL within \"double quotes\"");
        Console.WriteLine("                            if it contains special characters like & or =");
        Console.WriteLine();
        Console.WriteLine("  -a, --audio             : (Optional) Produce an .mp3 instead of a video file.");
        Console.WriteLine("                            Requires ffmpeg.");
        Console.WriteLine();
        Console.WriteLine("  -o, --output <FILENAME> : (Optional) Desired output filename. The container");
        Console.WriteLine("                            extension (.mp4, .webm, .mp3) is honoured when");
        Console.WriteLine("                            ffmpeg is available; otherwise capture mode falls");
        Console.WriteLine("                            back to .webm.");
        Console.WriteLine();
        Console.WriteLine("  -c, --capture           : (Optional) Use the capture path instead of trying");
        Console.WriteLine("                            direct download first. Required for view-only or");
        Console.WriteLine("                            DRM-protected stream pages.");
        Console.WriteLine();
        Console.WriteLine("  --capture-seconds <N>   : (Optional, capture mode only) Stop the recording");
        Console.WriteLine("                            after N seconds even if the video has not ended.");
        Console.WriteLine("                            Useful for testing.");
        Console.WriteLine();
        Console.WriteLine("  -v, --visible           : (Optional) Force a visible browser window even if");
        Console.WriteLine("                            a saved session is detected. Useful when the cached");
        Console.WriteLine("                            login has expired and you need to re-authenticate.");
        Console.WriteLine("                            Capture mode is always non-headless regardless;");
        Console.WriteLine("                            this flag specifically affects the direct-download");
        Console.WriteLine("                            path which now runs headless by default when a");
        Console.WriteLine("                            session is cached.");
        Console.WriteLine();
        Console.WriteLine("  --browser-path <PATH>   : (Optional) Path to an existing browser executable");
        Console.WriteLine("                            (e.g., Edge, Chrome, Chromium). If provided, this");
        Console.WriteLine("                            browser will be used instead of downloading Chromium.");
        Console.WriteLine("                            Example: --browser-path \"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"");
        Console.WriteLine();
        Console.WriteLine("  --ffmpeg-path <PATH>    : (Optional) Path to ffmpeg executable. Use this if");
        Console.WriteLine("                            you have a portable ffmpeg version or it's not in PATH.");
        Console.WriteLine("                            Example: --ffmpeg-path \"C:\\ffmpeg\\bin\\ffmpeg.exe\"");
        Console.WriteLine();
        Console.WriteLine("  -h, --help, -?, /?      : Display this help message.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  # Download a meeting you own (fast):");
        Console.WriteLine("  SharePointVideoDownloader.exe -u \"https://your-tenant-my.sharepoint.com/.../stream.aspx?id=...\" -o \"meeting.mp4\"");
        Console.WriteLine();
        Console.WriteLine("  # Record a view-only meeting shared with you (real-time):");
        Console.WriteLine("  SharePointVideoDownloader.exe -u \"...\" -o \"meeting.mp4\" --capture");
        Console.WriteLine();
        Console.WriteLine("  # Audio-only output (mp3):");
        Console.WriteLine("  SharePointVideoDownloader.exe -u \"...\" -o \"meeting.mp3\" --audio");
    }

    static async Task Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (args.Contains("-h") || args.Contains("--help") || args.Contains("-?") || args.Contains("/?"))
            {
                ShowHelp();
                return;
            }
        }

        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine("SharePoint/Stream Video Downloader using Puppeteer Sharp + ffmpeg (soft)");
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine();

        string? targetUrl = null;
        bool audioOnly = false;
        string? outputFilename = null;
        bool useArgs = false;
        bool argsValid = true;
        bool captureMode = false;
        int captureMaxSeconds = 0;
        bool forceVisible = false;
        string? browserPath = null;
        string? ffmpegPath = null;

        if (args.Length > 0)
        {
            useArgs = true; // Assume we'll try to use args if any are present (and not help)
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "-u":
                    case "--url":
                        if (i + 1 < args.Length)
                        {
                            targetUrl = args[++i];
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Error: Missing value for -u/--url argument.");
                            Console.ResetColor();
                            argsValid = false;
                        }
                        break;
                    case "-a":
                    case "--audio":
                        audioOnly = true;
                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                        {
                            outputFilename = args[++i];
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Error: Missing value for -o/--output argument.");
                            Console.ResetColor();
                            argsValid = false;
                        }
                        break;
                    case "-c":
                    case "--capture":
                        captureMode = true;
                        break;
                    case "-v":
                    case "--visible":
                        forceVisible = true;
                        break;
                    case "--capture-seconds":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var capSecs) && capSecs > 0)
                        {
                            captureMaxSeconds = capSecs;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Error: --capture-seconds requires a positive integer.");
                            Console.ResetColor();
                            argsValid = false;
                        }
                        break;
                    case "--browser-path":
                        if (i + 1 < args.Length)
                        {
                            browserPath = args[++i];
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Error: Missing value for --browser-path argument.");
                            Console.ResetColor();
                            argsValid = false;
                        }
                        break;
                    case "--ffmpeg-path":
                        if (i + 1 < args.Length)
                        {
                            ffmpegPath = args[++i];
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Error: Missing value for --ffmpeg-path argument.");
                            Console.ResetColor();
                            argsValid = false;
                        }
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error: Unknown argument '{args[i]}'");
                        Console.ResetColor();
                        argsValid = false;
                        break;
                }
                if (!argsValid) break;
            }

            if (argsValid && string.IsNullOrWhiteSpace(targetUrl))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Target URL (-u or --url) is required when using command-line arguments.");
                Console.ResetColor();
                argsValid = false;
            }

            if (!argsValid)
            {
                ShowHelp();
                Console.WriteLine("\nFalling back to interactive mode due to invalid or incomplete arguments...");
                useArgs = false; // Force interactive mode
            }
            else
            {
                Console.WriteLine("Using command-line arguments:");
                Console.WriteLine($"  URL: {targetUrl}");
                Console.WriteLine($"  Audio Only: {audioOnly}");
                Console.WriteLine($"  Capture Mode: {captureMode}");
                Console.WriteLine($"  Force Visible: {forceVisible}");
                if (!string.IsNullOrWhiteSpace(outputFilename))
                {
                    Console.WriteLine($"  Output Filename: {outputFilename}");
                }                
            }
        }

        if (!useArgs || !argsValid) // If no args, or args were invalid, prompt user
        {
            
            // 1. Get Target URL from User
            Console.Write("Enter the SharePoint/Stream video page URL: ");
            targetUrl = Console.ReadLine();
            
            // 2. Get Download Type
            Console.Write("Download video or audio only? (Enter V for Video, A for Audio - default V): ");
            string? downloadTypeInput = Console.ReadLine()?.Trim().ToUpperInvariant();
            if (downloadTypeInput == "A")
            {
                audioOnly = true;
            }
            // audioOnly defaults to false, so no 'else' needed to set it to false.

            // 3. Get Desired Output Filename
            Console.Write($"Enter the desired output filename (e.g., my_{(audioOnly ? "audio" : "video")}.{(audioOnly ? "mp3" : "mp4")}): ");
            outputFilename = Console.ReadLine();

        }

        // Validate URL (whether from args or input)
        if (string.IsNullOrWhiteSpace(targetUrl) || !Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid or missing URL provided.");
            Console.ResetColor();
            if (useArgs && argsValid) ShowHelp(); // Show help if args were attempted but URL was bad/missing
            return;
        }
        
        // Process audioOnly confirmation (whether from args or input)
        if (audioOnly)
        {
             if (!useArgs || !argsValid) Console.WriteLine("Audio download selected."); // Only print if interactive
        }
        else
        {
             if (!useArgs || !argsValid) Console.WriteLine("Video download selected (default)."); // Only print if interactive
        }

        // Process and validate outputFilename (whether from args or input)
        string defaultExtension = audioOnly ? ".mp3" : ".mp4";
        string fileTypeDescription = audioOnly ? "audio" : "video";

        if (string.IsNullOrWhiteSpace(outputFilename))
        {
            outputFilename = $"downloaded_{fileTypeDescription}_{DateTime.Now:yyyyMMddHHmmss}{defaultExtension}";
            Console.WriteLine($"No output filename provided. Using default: {outputFilename}");
        }
        else
        {
            string currentExtension = Path.GetExtension(outputFilename);
            if (string.IsNullOrEmpty(currentExtension))
            {
                outputFilename += defaultExtension;
                Console.WriteLine($"No extension provided for '{Path.GetFileNameWithoutExtension(outputFilename)}'. Appending default: {outputFilename}");
            }
            else if (audioOnly && !currentExtension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Provided extension '{currentExtension}' for an audio download. yt-dlp will attempt to save as MP3.");
                Console.ResetColor();
                // yt-dlp will handle the format. We could change outputFilename here to .mp3 if we want to be strict.
                // outputFilename = Path.ChangeExtension(outputFilename, ".mp3");
            }
            else if (!audioOnly && !currentExtension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) && 
                                   !currentExtension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) && 
                                   !currentExtension.Equals(".webm", StringComparison.OrdinalIgnoreCase) &&
                                   !currentExtension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ) // Added .mov as common
            {
                 Console.ForegroundColor = ConsoleColor.Yellow;
                 Console.WriteLine($"Warning: Provided extension '{currentExtension}' is not a typical video extension (.mp4, .mkv, .webm, .mov).");
                 Console.ResetColor();
            }
        }

        // Check ffmpeg availability up front so the user knows what to expect
        // before the long-running browser work even starts. ffmpeg is a soft
        // dependency: when present we get seekable webm + .mp4 transcode +
        // audio extraction. When absent we leave the raw .webm in place.
        WarnIfFfmpegMissing(ffmpegPath ?? string.Empty, audioOnly, outputFilename ?? string.Empty);

        IBrowser? browser = null;
        IPage? page = null;

        try
        {
            // 4. Launch Puppeteer
            Console.WriteLine("Launching browser...");
            var browserArgs = new System.Collections.Generic.List<string>
            {
                "--no-sandbox", // Often needed on Linux/Docker
            };
            if (captureMode)
            {
                // Make the capture path silent + invisible:
                //  - We deliberately DO NOT pass --mute-audio anymore. That flag was
                //    confirmed to mute the captured audio as well, not just the
                //    system output. Instead, we silence the speakers from JS by
                //    rerouting the <video> element's audio through Web Audio
                //    (createMediaElementSource → MediaStreamDestination, never
                //    connected to audioContext.destination), which both silences
                //    the speakers and lets us capture at full volume.
                //  - --use-fake-ui-for-media-stream + --auto-accept-this-tab-capture:
                //    suppress the getDisplayMedia picker; auto-grant for the current tab.
                //  - --window-position off-screen: keep the window invisible while
                //    still being rendered (DRM playback requires a real, non-headless
                //    surface, so we cannot just go headless).
                browserArgs.Add("--use-fake-ui-for-media-stream");
                browserArgs.Add("--auto-accept-this-tab-capture");
                browserArgs.Add("--auto-select-tab-capture-source-by-title=spvd-capture");
                browserArgs.Add("--disable-features=IsolateOrigins,site-per-process");
                browserArgs.Add("--window-position=-2400,-2400");
                browserArgs.Add("--window-size=1280,720");
            }
            // Decide whether to run headless. Capture mode always runs non-
            // headless (DRM requires a real surface). Otherwise we use the
            // smart-headless heuristic — but the user can force visible with
            // --visible, e.g. to redo a sign-in after the cached session
            // expires.
            bool runHeadless;
            if (captureMode)
            {
                runHeadless = false;
            }
            else if (forceVisible)
            {
                Console.WriteLine("--visible: forcing visible browser regardless of saved session.");
                runHeadless = false;
            }
            else
            {
                runHeadless = ShouldRunHeadless();
            }

            var launchOptions = new LaunchOptions
            {
                Headless = runHeadless,
                Args = browserArgs.ToArray(),
                UserDataDir = userDataDir,
                DefaultViewport = null, // let the OS window size drive the viewport in capture mode
                ExecutablePath = string.IsNullOrWhiteSpace(browserPath) ? null : browserPath
            };

            // Download browser if needed
            if (string.IsNullOrWhiteSpace(browserPath))
            {
                var browserFetcher = new BrowserFetcher();
                Console.WriteLine("Ensuring browser is available...");
                await browserFetcher.DownloadAsync();
            }
            else
            {
                Console.WriteLine($"Using browser from: {browserPath}");
                if (!File.Exists(browserPath))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: Browser executable not found at {browserPath}");
                    Console.ResetColor();
                    return;
                }
            }

            browser = await Puppeteer.LaunchAsync(launchOptions);
            page = await browser.NewPageAsync();
            if (!captureMode)
            {
                // Capture mode lets the OS window size drive the viewport (set
                // via DefaultViewport=null + --window-size flag). For the other
                // paths we keep a fixed viewport so layout is predictable.
                await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });
            }

            // 5. Navigate to the Page
            Console.WriteLine($"Navigating to: {targetUrl}");
            try
            {
                await page.EvaluateFunctionAsync("url => { window.location.href = url; }", targetUrl);
                await page.WaitForNavigationAsync(new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle2 } });
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Warning: Page navigation timed out (NetworkIdle2). Continuing, but page might not be fully loaded.");
            }
            catch (Exception navEx)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error navigating to page: {navEx.Message}");
                Console.ResetColor();
                return; // Exit if navigation fails critically
            }


            Console.WriteLine("Page loaded.");

            // Capture mode short-circuit: skip both the direct download and the
            // manifest+yt-dlp paths. We record the playing video via getDisplayMedia
            // + MediaRecorder, which is the only legitimate way to extract content
            // when the user has streaming-only access to a DRM-protected recording.
            if (captureMode)
            {
                bool capOk = await TryCaptureViaPlaybackAsync(page, outputFilename, captureMaxSeconds, ffmpegPath);
                if (!capOk)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Capture path failed. See messages above.");
                    Console.ResetColor();
                    return;
                }

                // Honor -a/--audio: post-process the captured file (mp4 if ffmpeg
                // transcoded it, webm otherwise) into mp3.
                if (audioOnly)
                {
                    string? captured = null;
                    if (!string.IsNullOrEmpty(outputFilename))
                    {
                        captured = File.Exists(outputFilename)
                            ? outputFilename
                            : Path.Combine(
                                Path.GetDirectoryName(Path.GetFullPath(outputFilename)) ?? Directory.GetCurrentDirectory(),
                                Path.GetFileNameWithoutExtension(outputFilename) + ".webm");
                    }
                    if (!string.IsNullOrEmpty(captured))
                    {
                        await ExtractAudioMp3Async(captured, ffmpegPath);
                    }
                }
                return;
            }

            // 6. Default path: try the direct file download from SharePoint.
            // Works for any recording you have Download permission on (your own
            // recordings, or shared with you with Download enabled). Returns
            // the original non-DRM mp4 directly.
            bool directOk = false;
            try
            {
                directOk = await TryDirectDownloadAsync(page, targetUrl, outputFilename);
            }
            catch (Exception ddEx)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Direct download attempt threw: {ddEx.Message}");
                Console.ResetColor();
            }

            if (directOk)
            {
                // Honor -a/--audio: extract audio from the downloaded mp4.
                    if (audioOnly && !string.IsNullOrWhiteSpace(outputFilename))
                return;
            }

            // Direct download was not possible (no Download permission, or
            // the link is to view-only DRM-protected content). yt-dlp + DASH
            // manifest used to be the fallback here, but Microsoft now ships
            // PlayReady DRM on its DASH streams and yt-dlp cannot decrypt
            // those segments — the only legitimate way to get a file from a
            // view-only stream is to re-record it as it plays. Tell the user
            // to re-run with --capture.
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine();
            Console.WriteLine("Direct download was not possible (likely a view-only share, or DRM-protected stream).");
            Console.WriteLine("Re-run with -c / --capture to record the video as it plays in the browser.");
            Console.WriteLine("Capture mode runs in real time (a 1-hour meeting takes 1 hour to record).");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Console.ResetColor();
        }
        finally
        {
            // 11. Cleanup
            if (page != null)
            {
                // Optional: You might want to keep the page open briefly if headless is false
                // if (!RunHeadless) await Task.Delay(5000); 
                await page.CloseAsync();
            }
            if (browser != null)
            {
                Console.WriteLine("Closing browser...");
                await browser.CloseAsync();
            }
            Console.WriteLine("Process finished.");
        }
    }


    // Extract a query-string parameter value (URL-encoded) by name. Returns null
    // if the query is empty or the name is not present.
    static string? ParseQueryParam(string query, string name)
    {
        if (string.IsNullOrEmpty(query)) return null;
        if (query.StartsWith("?")) query = query.Substring(1);
        foreach (var pair in query.Split('&'))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            string key = pair.Substring(0, eq);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Substring(eq + 1);
            }
        }
        return null;
    }

    // Re-encode a SharePoint document path (e.g. "/personal/u/Documents/My File.mp4")
    // into a URL path (each segment escaped via Uri.EscapeDataString). Preserves
    // leading and internal slashes.
    static string EncodePath(string decodedPath)
    {
        if (string.IsNullOrEmpty(decodedPath)) return decodedPath ?? string.Empty;
        var segments = decodedPath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Length == 0 ? string.Empty : Uri.EscapeDataString(segments[i]);
        }
        return string.Join("/", segments);
    }

    // Try to download the original mp4 directly from SharePoint by deriving the
    // file URL from the stream.aspx?id=<path> query parameter. Returns true if
    // the file was saved successfully, false if we should fall back to the
    // legacy manifest/yt-dlp flow.
    static async Task<bool> TryDirectDownloadAsync(IPage page, string? pageUrl, string? outputFilename)
    {
        Uri pageUri;
        if (string.IsNullOrEmpty(pageUrl) || string.IsNullOrEmpty(outputFilename)) return false;

        try { pageUri = new Uri(pageUrl); }
        catch { return false; }

        string? idParam = ParseQueryParam(pageUri.Query, "id");
        if (string.IsNullOrEmpty(idParam))
        {
            Console.WriteLine("Direct download: no 'id' parameter in URL — cannot derive file path.");
            return false;
        }

        // Querystring values use '+' for space; convert before unescaping.
        string decodedPath = Uri.UnescapeDataString(idParam.Replace('+', ' '));
        if (!decodedPath.StartsWith("/", StringComparison.Ordinal))
        {
            Console.WriteLine($"Direct download: unexpected 'id' format (expected leading '/'): {decodedPath}");
            return false;
        }

        string encodedPath = EncodePath(decodedPath);
        string downloadUrl = $"{pageUri.Scheme}://{pageUri.Host}{encodedPath}?download=1";
        Console.WriteLine($"Direct download URL: {downloadUrl}");

        // Configure where Chromium should save downloads, and what filename to use.
        string resolvedOutput = outputFilename;
        string downloadDir = Path.GetDirectoryName(Path.GetFullPath(resolvedOutput)) ?? Directory.GetCurrentDirectory();
        if (string.IsNullOrEmpty(downloadDir)) downloadDir = Directory.GetCurrentDirectory();
        string targetFileName = Path.GetFileName(outputFilename);
        string finalPath = Path.Combine(downloadDir, targetFileName);
        string crdownloadPath = finalPath + ".crdownload";

        // Clean up any stale partial / final files from previous attempts so our
        // wait loop can detect a fresh download cleanly.
        try { if (File.Exists(finalPath)) File.Delete(finalPath); } catch { /* ignore */ }
        try { if (File.Exists(crdownloadPath)) File.Delete(crdownloadPath); } catch { /* ignore */ }

        var cdp = await page.CreateCDPSessionAsync();
        try
        {
            await cdp.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = downloadDir,
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Direct download: Page.setDownloadBehavior failed ({ex.Message}).");
            return false;
        }

        // Trigger the download by injecting an <a download> click. This makes
        // Chromium handle auth, redirects, and the Content-Disposition response.
        string js =
            "(function(url, name){" +
            "  var a = document.createElement('a');" +
            "  a.href = url; a.download = name;" +
            "  document.body.appendChild(a); a.click();" +
            "  setTimeout(function(){ document.body.removeChild(a); }, 100);" +
            "})(" +
            System.Text.Json.JsonSerializer.Serialize(downloadUrl) + "," +
            System.Text.Json.JsonSerializer.Serialize(targetFileName) + ");";
        try
        {
            await page.EvaluateExpressionAsync(js);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Direct download: failed to dispatch click ({ex.Message}).");
            return false;
        }

        Console.WriteLine($"Saving to: {finalPath}");
        Console.WriteLine("Waiting for download to complete (poll every 2s, max 30 min)...");

        DateTime start = DateTime.UtcNow;
        bool startedDownloading = false;
        long lastReportedSize = -1;

        while ((DateTime.UtcNow - start).TotalMinutes < 30)
        {
            await Task.Delay(2000);

            bool inProgress = File.Exists(crdownloadPath);
            bool done = File.Exists(finalPath);

            if (inProgress)
            {
                startedDownloading = true;
                long size = 0;
                try { size = new FileInfo(crdownloadPath).Length; } catch { }
                if (size != lastReportedSize)
                {
                    Console.WriteLine($"  Downloading: {size:N0} bytes...");
                    lastReportedSize = size;
                }
                continue;
            }

            if (done)
            {
                // .crdownload disappeared and the final file exists — give the OS
                // a beat to flush, then accept it.
                await Task.Delay(500);
                long size = 0;
                try { size = new FileInfo(finalPath).Length; } catch { }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Direct download complete: {finalPath} ({size:N0} bytes)");
                Console.ResetColor();
                return true;
            }

            if (startedDownloading)
            {
                // Was downloading, now neither file is present — Chromium may have
                // canceled the download (e.g., navigated away, server error).
                Console.WriteLine("Direct download: partial file disappeared — assuming cancellation.");
                return false;
            }

            // Hasn't started yet. Give it a bounded amount of time.
            if ((DateTime.UtcNow - start).TotalSeconds > 60)
            {
                Console.WriteLine("Direct download: nothing showed up in the download directory after 60 seconds.");
                return false;
            }
        }

        Console.WriteLine("Direct download: timed out after 30 minutes.");
        return false;
    }

    // Snapshot of the in-page recorder state. Mirrors the JSON we build in
    // window._spvd_status() in TryCaptureViaPlaybackAsync.
    private sealed class CaptureStatus
    {
        public string? State { get; set; }
        public string? Error { get; set; }
        public int Chunks { get; set; }
        public long TotalBytes { get; set; }
        public long BlobSize { get; set; }
        public int AudioTracks { get; set; }
        public int VideoTracks { get; set; }
        public string? AudioPathway { get; set; }
        public string? VideoSource { get; set; }
        public int? CanvasProbeNonZero { get; set; }
        public string? CanvasProbeError { get; set; }
        public string? RecorderState { get; set; }
        public bool? VideoPaused { get; set; }
        public double? VideoCurrentTime { get; set; }
        public double? VideoDuration { get; set; }
        public int? VideoWidth { get; set; }
        public int? VideoHeight { get; set; }
        public string? StopReason { get; set; }
    }

    // Capture the playing video via getDisplayMedia + MediaRecorder, then save the
    // resulting webm. This is the only legitimate path for content the user has
    // streaming-only access to (DRM-protected, no Download permission).
    //
    // The browser was launched with --mute-audio so no sound reaches the speakers;
    // the open question — verified by running this — is whether the captured audio
    // track still contains real samples or whether --mute-audio also silences the
    // capture pipeline. Diagnostic output reports both conditions.
    static async Task<bool> TryCaptureViaPlaybackAsync(IPage page, string? outputFilename, int maxSeconds, string? ffmpegPath = null)
    {
        // Force a .webm extension on the output: MediaRecorder produces VP9 + Opus
        // in a WebM container. Transcoding to mp4 would require ffmpeg, which is a
        // separate concern — we punt on it here.
        string webmName = string.IsNullOrEmpty(outputFilename) ? "capture" : Path.GetFileNameWithoutExtension(outputFilename);
        if (string.IsNullOrEmpty(webmName)) webmName = "capture";
        webmName += ".webm";
        string fullPath = string.IsNullOrEmpty(outputFilename) ? Path.Combine(Directory.GetCurrentDirectory(), webmName) : Path.GetFullPath(outputFilename);
        string downloadDir = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        if (string.IsNullOrEmpty(downloadDir)) downloadDir = Directory.GetCurrentDirectory();
        string finalPath = Path.Combine(downloadDir, webmName);
        string crdownloadPath = finalPath + ".crdownload";

        // Clean up any stale partial / final files from a previous attempt so the
        // wait-loop below can detect a fresh download cleanly.
        try { if (File.Exists(finalPath)) File.Delete(finalPath); } catch { }
        try { if (File.Exists(crdownloadPath)) File.Delete(crdownloadPath); } catch { }

        // Tag the page title so --auto-select-tab-capture-source-by-title can
        // resolve to this tab when the picker would otherwise appear.
        try { await page.EvaluateExpressionAsync("document.title = 'spvd-capture'"); } catch { }

        // Set the download directory via CDP so the <a download> click below lands
        // where we expect it.
        var cdp = await page.CreateCDPSessionAsync();
        try
        {
            await cdp.SendAsync("Page.setDownloadBehavior", new
            {
                behavior = "allow",
                downloadPath = downloadDir,
            });
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Capture: Page.setDownloadBehavior failed: {ex.Message}");
            Console.ResetColor();
            return false;
        }

        // Wait for the player to mount a <video> element.
        Console.WriteLine("Capture: waiting for <video> element...");
        try
        {
            await page.WaitForSelectorAsync("video", new WaitForSelectorOptions { Timeout = 30000 });
        }
        catch
        {
            Console.WriteLine("Capture: no <video> element appeared within 30 s.");
            return false;
        }

        Console.WriteLine($"Capture: starting MediaRecorder (output: {finalPath}, max seconds: {(maxSeconds > 0 ? maxSeconds.ToString() : "unlimited")}).");

        // Build the in-page capture script. The script is a synchronous outer
        // IIFE that publishes window.__spvd state + helpers and *schedules* the
        // real async work via setTimeout, so EvaluateExpressionAsync below
        // returns immediately. The C# caller then clicks play, and the async
        // setup proceeds when the SharePoint player has started actual playback.
        //
        // Strategy:
        //  1. Audio: hook the <video> element via Web Audio
        //     (createMediaElementSource → MediaStreamDestination, NEVER connected
        //     to audioContext.destination). The speakers stay silent because Web
        //     Audio now owns audio routing, and we capture at full volume.
        //  2. Video: TRY direct frame capture from the page <video> via
        //     canvas.drawImage(video). This is officially blocked for EME-
        //     protected media but Chromium's enforcement on this Microsoft
        //     content has already proven lax (Web Audio works), so it is worth
        //     probing. If a one-pixel sample of the canvas comes back non-black,
        //     we use direct capture (full source resolution, no UI chrome).
        //  3. Video fallback: if the canvas read returns all-black or throws,
        //     fall back to getDisplayMedia tab capture cropped to the <video>
        //     element's bounds.
        //  4. MediaRecorder on the combined audio + video MediaStream.
        //  5. Stop on <video>.ended OR after maxSeconds.
        //  6. On stop, build a Blob and trigger an <a download> click so the
        //     file lands in the directory we configured via setDownloadBehavior.
        string js = @"
(function(filename, maxSeconds) {
    window.__spvd = {
        state: 'starting',
        error: null,
        chunks: 0,
        totalBytes: 0,
        blobSize: 0,
        audioTracks: 0,
        videoTracks: 0,
        audioPathway: null,
        recorderState: null,
        videoPaused: null,
        videoCurrentTime: null,
        videoDuration: null,
        videoWidth: null,
        videoHeight: null,
        filename: filename,
        maxSeconds: maxSeconds,
        recorder: null
    };
    window.__spvd_status = function() {
        var v = document.querySelector('video');
        return JSON.stringify({
            state: window.__spvd.state,
            error: window.__spvd.error,
            chunks: window.__spvd.chunks,
            totalBytes: window.__spvd.totalBytes,
            blobSize: window.__spvd.blobSize,
            audioTracks: window.__spvd.audioTracks,
            videoTracks: window.__spvd.videoTracks,
            audioPathway: window.__spvd.audioPathway,
            videoSource: window.__spvd.videoSource,
            canvasProbeNonZero: window.__spvd.canvasProbeNonZero,
            canvasProbeError: window.__spvd.canvasProbeError,
            recorderState: window.__spvd.recorder ? window.__spvd.recorder.state : null,
            videoPaused: v ? v.paused : null,
            videoCurrentTime: v ? v.currentTime : null,
            videoDuration: v ? (isFinite(v.duration) ? v.duration : null) : null,
            videoWidth: window.__spvd.videoWidth,
            videoHeight: window.__spvd.videoHeight,
            stopReason: window.__spvd.stopReason
        });
    };
    window.__spvd_stop = function() {
        try { if (window.__spvd.recorder && window.__spvd.recorder.state === 'recording') window.__spvd.recorder.stop(); } catch (e) {}
    };

    // Schedule async setup so this outer IIFE returns immediately and the C#
    // caller can click play (which is what kicks off real playback).
    setTimeout(async function() {
        try {
            var video = document.querySelector('video');
            if (!video) throw new Error('No <video> element on page');

            // -------- Audio: Web Audio hook on the page's <video> --------
            // This is set up BEFORE playback because once the SharePoint player
            // starts pumping audio through the element, we want our hook to be
            // the only consumer (so nothing reaches the speakers).
            try {
                var audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                var mediaSrc = audioCtx.createMediaElementSource(video);
                var dest = audioCtx.createMediaStreamDestination();
                mediaSrc.connect(dest);
                // Note: we never call mediaSrc.connect(audioCtx.destination), so
                // audio does NOT go to the speakers.
                window.__spvd._audioTrack = dest.stream.getAudioTracks()[0] || null;
                window.__spvd.audioPathway = 'web-audio-hook';
            } catch (e) {
                window.__spvd.audioPathway = 'web-audio-failed: ' + (e.name || '') + ': ' + (e.message || '');
            }
            window.__spvd.state = 'audio-ready-waiting-for-play';

            // -------- Wait for actual playback before probing canvas --------
            // The C# caller dispatches the play click after this function is
            // injected. Real frames only flow once the SharePoint player has
            // fetched the manifest, set up MSE, and the CDM has provisioned.
            var tWait = Date.now();
            while ((video.paused || video.videoWidth === 0) && Date.now() - tWait < 30000) {
                await new Promise(function(r){ setTimeout(r, 200); });
            }
            if (video.paused || video.videoWidth === 0) {
                throw new Error('video did not start playing within 30 s (paused=' + video.paused + ', videoWidth=' + video.videoWidth + ')');
            }
            window.__spvd.state = 'video-playing';
            window.__spvd.videoWidth = video.videoWidth;
            window.__spvd.videoHeight = video.videoHeight;

            // -------- Video source: try direct frame capture first --------
            var canvas = document.createElement('canvas');
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            var ctx2d = canvas.getContext('2d');

            var canvasProbeOk = false;
            try {
                ctx2d.drawImage(video, 0, 0, canvas.width, canvas.height);
                var sample = ctx2d.getImageData(Math.max(0, Math.floor(canvas.width / 2) - 4), Math.max(0, Math.floor(canvas.height / 2) - 4), 8, 8);
                var nonZero = 0;
                for (var i = 0; i < sample.data.length; i += 4) {
                    if (sample.data[i] || sample.data[i+1] || sample.data[i+2]) nonZero++;
                }
                window.__spvd.canvasProbeNonZero = nonZero;
                canvasProbeOk = nonZero > 0;
            } catch (e) {
                window.__spvd.canvasProbeError = (e && e.name ? e.name + ': ' : '') + (e && e.message ? e.message : String(e));
            }

            var videoTrack = null;
            var rafHandle = null;
            var tapVideo = null;
            var tabStream = null;

            if (canvasProbeOk) {
                // Direct frame capture: read frames from the page's <video>
                // straight into our canvas. Output is at native videoWidth ×
                // videoHeight (full source resolution) and contains only the
                // video content, no SharePoint UI chrome.
                window.__spvd.videoSource = 'page-video-direct';
                function drawDirect(now, metadata) {
                    try {
                        if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
                            canvas.width = video.videoWidth;
                            canvas.height = video.videoHeight;
                            window.__spvd.videoWidth = canvas.width;
                            window.__spvd.videoHeight = canvas.height;
                        }
                        ctx2d.drawImage(video, 0, 0, canvas.width, canvas.height);
                    } catch (e) {}
                    if (typeof video.requestVideoFrameCallback === 'function') {
                        video.requestVideoFrameCallback(drawDirect);
                    } else {
                        rafHandle = requestAnimationFrame(drawDirect);
                    }
                }
                if (typeof video.requestVideoFrameCallback === 'function') {
                    video.requestVideoFrameCallback(drawDirect);
                } else {
                    rafHandle = requestAnimationFrame(drawDirect);
                }
                videoTrack = canvas.captureStream(30).getVideoTracks()[0];
            } else {
                // Fallback: tab capture cropped to the <video> bounds.
                window.__spvd.videoSource = 'tab-capture-cropped';
                tabStream = await navigator.mediaDevices.getDisplayMedia({
                    video: { displaySurface: 'browser' },
                    audio: false,
                    preferCurrentTab: true,
                    selfBrowserSurface: 'include'
                });
                tapVideo = document.createElement('video');
                tapVideo.muted = true;
                tapVideo.playsInline = true;
                tapVideo.srcObject = tabStream;
                try { await tapVideo.play(); } catch (e) {}

                // Reset canvas to the on-screen <video> CSS size for the crop
                // (we are reading from the tab capture frames, which are sized
                // in CSS pixels of the viewport).
                var rect0 = video.getBoundingClientRect();
                canvas.width = Math.max(1, Math.round(rect0.width));
                canvas.height = Math.max(1, Math.round(rect0.height));
                window.__spvd.videoWidth = canvas.width;
                window.__spvd.videoHeight = canvas.height;

                function drawCropped() {
                    try {
                        var r = video.getBoundingClientRect();
                        var rw = Math.max(1, Math.round(r.width));
                        var rh = Math.max(1, Math.round(r.height));
                        if (rw !== canvas.width || rh !== canvas.height) {
                            canvas.width = rw;
                            canvas.height = rh;
                            window.__spvd.videoWidth = rw;
                            window.__spvd.videoHeight = rh;
                        }
                        ctx2d.drawImage(tapVideo, Math.max(0, r.left), Math.max(0, r.top), r.width, r.height, 0, 0, canvas.width, canvas.height);
                    } catch (e) {}
                    rafHandle = requestAnimationFrame(drawCropped);
                }
                drawCropped();
                videoTrack = canvas.captureStream(30).getVideoTracks()[0];
            }

            // -------- Combined stream + MediaRecorder --------
            var tracks = [];
            if (videoTrack) tracks.push(videoTrack);
            if (window.__spvd._audioTrack) tracks.push(window.__spvd._audioTrack);
            var combined = new MediaStream(tracks);
            window.__spvd.audioTracks = combined.getAudioTracks().length;
            window.__spvd.videoTracks = combined.getVideoTracks().length;

            var mime = 'video/webm; codecs=vp9,opus';
            if (!MediaRecorder.isTypeSupported(mime)) mime = 'video/webm';
            var recorder = new MediaRecorder(combined, { mimeType: mime, videoBitsPerSecond: 4000000 });
            var chunks = [];
            recorder.ondataavailable = function(e) {
                if (e.data && e.data.size > 0) {
                    chunks.push(e.data);
                    window.__spvd.chunks = chunks.length;
                    window.__spvd.totalBytes += e.data.size;
                }
            };
            recorder.onstop = function() {
                try {
                    if (rafHandle) cancelAnimationFrame(rafHandle);
                    if (tabStream) { try { tabStream.getTracks().forEach(function(t){ t.stop(); }); } catch (e) {} }
                    var blob = new Blob(chunks, { type: mime.split(';')[0] });
                    window.__spvd.blobSize = blob.size;
                    var url = URL.createObjectURL(blob);
                    var a = document.createElement('a');
                    a.href = url; a.download = filename;
                    document.body.appendChild(a); a.click();
                    setTimeout(function(){ try { URL.revokeObjectURL(url); document.body.removeChild(a); } catch (e) {} }, 1000);
                    window.__spvd.state = 'downloaded';
                } catch (e) {
                    window.__spvd.error = 'onstop failed: ' + e.message;
                    window.__spvd.state = 'error';
                }
            };
            recorder.onerror = function(e) {
                window.__spvd.error = 'recorder error: ' + ((e && e.error && e.error.message) || e.error || e);
                window.__spvd.state = 'error';
            };

            // Stop the recorder when the video reaches its end. We need belt and
            // suspenders here because the SharePoint OnePlayer is MSE+PlayReady
            // and we cannot trust any single end-of-stream signal:
            //   1. <video>.ended event — may or may not fire on MSE+DRM streams.
            //   2. currentTime >= duration - epsilon — the actual end-of-content
            //      is reliable when video.duration is known and finite.
            //   3. Stall detection — if currentTime has not advanced for ~10s and
            //      we are within 1 s of duration, treat that as the end too.
            //   4. Hard duration cap — if duration is known, schedule a stop at
            //      duration + a small grace window so we never run forever.
            //   5. maxSeconds explicit cap — already wired below.
            // Whichever signal fires first wins; recorder.stop() is idempotent
            // (it checks recorder.state).
            var stopReason = null;
            function stopRecording(reason) {
                if (stopReason) return; // already stopping
                stopReason = reason;
                window.__spvd.stopReason = reason;
                try { if (recorder.state === 'recording') recorder.stop(); } catch (e) {}
            }

            video.addEventListener('ended', function(){ stopRecording('video.ended event'); });

            // Schedule a hard cap based on the known duration. We give 30 s of
            // grace because some players seek slightly past the end during
            // teardown, and we add an extra 5 s for buffer flush.
            var initialDuration = (typeof video.duration === 'number' && isFinite(video.duration)) ? video.duration : 0;
            if (initialDuration > 0) {
                var capMs = Math.max(0, (initialDuration - video.currentTime) * 1000) + 30000;
                setTimeout(function() { stopRecording('hard duration cap (' + initialDuration.toFixed(1) + 's + 30 s)'); }, capMs);
            }

            // Poll for end-of-stream by content position + stall.
            var lastTime = video.currentTime;
            var stallTicks = 0;
            var endPollHandle = setInterval(function() {
                if (stopReason) { clearInterval(endPollHandle); return; }
                var dur = (typeof video.duration === 'number' && isFinite(video.duration)) ? video.duration : 0;
                var t = video.currentTime;
                if (dur > 0 && t >= dur - 0.5) {
                    clearInterval(endPollHandle);
                    stopRecording('currentTime reached duration (' + t.toFixed(2) + ' / ' + dur.toFixed(2) + ')');
                    return;
                }
                if (Math.abs(t - lastTime) < 0.05) {
                    stallTicks++;
                    if (stallTicks >= 10) {
                        // ~10 s of no advancement.
                        if (dur > 0 && t >= dur - 1.5) {
                            clearInterval(endPollHandle);
                            stopRecording('stalled near duration (' + t.toFixed(2) + ' / ' + dur.toFixed(2) + ')');
                        } else if (dur === 0) {
                            // No known duration AND playback frozen. Most likely
                            // we have hit the end of an open-ended MSE stream.
                            clearInterval(endPollHandle);
                            stopRecording('stalled with unknown duration at ' + t.toFixed(2) + 's');
                        }
                        // If we are stalled but well before the end, the user may
                        // have paused; keep waiting.
                    }
                } else {
                    stallTicks = 0;
                    lastTime = t;
                }
            }, 1000);

            recorder.start(2000);
            window.__spvd.recorder = recorder;
            window.__spvd.state = 'recording';

            if (maxSeconds > 0) {
                setTimeout(function() {
                    stopRecording('--capture-seconds=' + maxSeconds + ' elapsed');
                }, maxSeconds * 1000);
            }
        } catch (e) {
            window.__spvd.error = (e && e.name ? e.name + ': ' : '') + (e && e.message ? e.message : String(e));
            window.__spvd.state = 'error';
        }
    }, 0);
})";
        js += $"({System.Text.Json.JsonSerializer.Serialize(webmName)}, {maxSeconds});";

        try
        {
            await page.EvaluateExpressionAsync(js);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Capture: failed to inject capture script: {ex.Message}");
            Console.ResetColor();
            return false;
        }

        // Trigger playback. SharePoint's player wires its own click/keyboard
        // handlers that do manifest fetch + DRM provisioning + MSE setup before
        // starting playback. Try several strategies until the <video> reports
        // paused === false:
        //   1. Click the bare <video> element (works in the legacy yt-dlp path).
        //   2. Press Space (the player's keyboard shortcut for play/pause).
        //   3. Dispatch a synthetic bubbling click event on the video element.
        async Task<bool> IsPlayingAsync()
        {
            try
            {
                return await page.EvaluateExpressionAsync<bool>(
                    "(function(){var v=document.querySelector('video');return !!(v && !v.paused);})()");
            }
            catch { return false; }
        }

        Console.WriteLine("Capture: dispatching play (mouse click on <video>)...");
        try
        {
            var videoEl = await page.QuerySelectorAsync("video");
            if (videoEl != null) await videoEl.ClickAsync();
        }
        catch (Exception ex) { Console.WriteLine($"  (click failed: {ex.Message})"); }
        await Task.Delay(1500);

        if (!await IsPlayingAsync())
        {
            Console.WriteLine("Capture: still paused after click — trying Space key...");
            try { await page.Keyboard.PressAsync("Space"); } catch (Exception ex) { Console.WriteLine($"  (space failed: {ex.Message})"); }
            await Task.Delay(1500);
        }

        if (!await IsPlayingAsync())
        {
            Console.WriteLine("Capture: still paused — trying synthetic click via JS...");
            try
            {
                await page.EvaluateExpressionAsync(
                    "(function(){var v=document.querySelector('video');if(!v)return;v.dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true,view:window}));})()");
            }
            catch (Exception ex) { Console.WriteLine($"  (synthetic click failed: {ex.Message})"); }
            await Task.Delay(1500);
        }

        if (await IsPlayingAsync())
        {
            Console.WriteLine("Capture: playback started.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Capture: WARNING — could not confirm playback started; continuing anyway. Recording may be of a still frame.");
            Console.ResetColor();
        }

        // Poll the in-page recorder status until it reports 'downloaded' or 'error'.
        DateTime start = DateTime.UtcNow;
        TimeSpan timeout = maxSeconds > 0
            ? TimeSpan.FromSeconds(maxSeconds + 120)
            : TimeSpan.FromHours(8); // generous upper bound for full-meeting recordings
        string lastState = "";
        while (DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(2000);

            CaptureStatus? status = null;
            try
            {
                string json = await page.EvaluateExpressionAsync<string>("window.__spvd_status ? window.__spvd_status() : null");
                if (!string.IsNullOrEmpty(json))
                {
                    status = System.Text.Json.JsonSerializer.Deserialize<CaptureStatus>(json,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  (poll error: {ex.Message})");
            }

            if (status == null)
            {
                continue;
            }

            string s = status.State ?? "?";
            if (s != lastState)
            {
                string probe = status.CanvasProbeNonZero.HasValue ? $"nonZero={status.CanvasProbeNonZero}" : (status.CanvasProbeError ?? "?");
                Console.WriteLine($"  state={s} audioPathway={status.AudioPathway} videoSource={status.VideoSource ?? "?"} canvasProbe={probe} dim={status.VideoWidth}x{status.VideoHeight} a={status.AudioTracks} v={status.VideoTracks} recorder={status.RecorderState}");
                lastState = s;
            }
            else if (s == "recording")
            {
                Console.WriteLine($"  recording: {status.Chunks} chunks, {status.TotalBytes:N0} bytes captured (video t={status.VideoCurrentTime:F1}s, paused={status.VideoPaused})");
            }

            if (s == "error")
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Capture: in-page error: {status.Error}");
                Console.ResetColor();
                return false;
            }

            if (s == "downloaded")
            {
                Console.WriteLine($"Capture: in-page Blob assembled ({status.BlobSize:N0} bytes); stopReason='{status.StopReason ?? "?"}'. Waiting for the file to land on disk...");
                break;
            }
        }

        // Wait for the file to finish writing (Chromium produces a .crdownload then
        // renames to the final name).
        DateTime fileWaitStart = DateTime.UtcNow;
        while ((DateTime.UtcNow - fileWaitStart).TotalMinutes < 5)
        {
            await Task.Delay(1000);
            if (File.Exists(finalPath))
            {
                await Task.Delay(500);
                long size = 0;
                try { size = new FileInfo(finalPath).Length; } catch { }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Capture complete (raw): {finalPath} ({size:N0} bytes)");
                Console.ResetColor();

                // Post-process via ffmpeg to fix two MediaRecorder limitations:
                //  1. The raw webm has no Cues element, so seeking does not work
                //     in most players. A `-c copy` remux through ffmpeg writes a
                //     proper Cues block.
                //  2. The user originally asked for a .mp4 (we forced .webm only
                //     because MediaRecorder cannot output mp4 in Chromium). If
                //     ffmpeg is available we transcode to mp4 too.
                await PostProcessCaptureAsync(finalPath, outputFilename, ffmpegPath);

                return true;
            }
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Capture: file did not appear at {finalPath} within 5 minutes after the recorder stopped.");
        Console.ResetColor();
        return false;
    }

    // Post-process the raw MediaRecorder webm: remux it (in place) to add the
    // Cues element so seeking works, and — if the user originally requested an
    // mp4 — transcode to H.264 + AAC. Both steps are best-effort; if ffmpeg is
    // missing or fails we leave the raw webm alone and tell the user where it is.
    static async Task PostProcessCaptureAsync(string rawWebmPath, string? requestedOutput, string? customFfmpegPath = null)
    {
        string ffmpeg = LocateFfmpeg(customFfmpegPath);
        if (string.IsNullOrEmpty(ffmpeg))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("ffmpeg not found in PATH or alongside the executable — skipping seek-fix and mp4 transcode.");
            Console.WriteLine("Install ffmpeg and re-run, or play the raw .webm in a tolerant player (VLC seeks fine; some browsers do not).");
            Console.ResetColor();
            return;
        }

        // Step 1: remux .webm → .webm with cues (seekable). Write to a sibling
        // temp file then atomically replace.
        string remuxed = rawWebmPath + ".seekable.webm";
        try { if (File.Exists(remuxed)) File.Delete(remuxed); } catch { }
        Console.WriteLine("Remuxing webm to add seek index...");
        bool remuxOk = await RunFfmpegAsyncWithErrorCapture(ffmpeg, $"-y -i \"{rawWebmPath}\" -c copy \"{remuxed}\"");
        if (remuxOk && File.Exists(remuxed) && new FileInfo(remuxed).Length > 0)
        {
            try
            {
                File.Delete(rawWebmPath);
                File.Move(remuxed, rawWebmPath);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Remuxed (seekable): {rawWebmPath}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not replace raw webm with remuxed version: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("Remux failed; raw webm is left as-is (seek may not work in some players).");
            try { if (File.Exists(remuxed)) File.Delete(remuxed); } catch { }
        }

        // Step 2: if the user asked for .mp4 (or any non-.webm extension), produce that too.
        string requestedExt = Path.GetExtension(requestedOutput ?? string.Empty);
        if (!string.IsNullOrEmpty(requestedExt) && !requestedExt.Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            string outDir = Path.GetDirectoryName(Path.GetFullPath(rawWebmPath)) ?? Directory.GetCurrentDirectory();
            string mp4Path = Path.Combine(outDir, Path.GetFileNameWithoutExtension(rawWebmPath) + requestedExt);
            try { if (File.Exists(mp4Path)) File.Delete(mp4Path); } catch { }
            Console.WriteLine($"Transcoding to {requestedExt} (this re-encodes; takes a while)...");
            bool tx = await RunFfmpegAsyncWithErrorCapture(ffmpeg, $"-y -i \"{rawWebmPath}\" -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 160k -movflags +faststart \"{mp4Path}\"", showProgress: true);
            if (tx && File.Exists(mp4Path) && new FileInfo(mp4Path).Length > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ {requestedExt} ready: {mp4Path} ({new FileInfo(mp4Path).Length:N0} bytes)");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"Transcode to {requestedExt} failed — keeping the .webm.");
                try { if (File.Exists(mp4Path)) File.Delete(mp4Path); } catch { }
            }
        }
    }

    // Print a friendly heads-up if ffmpeg is missing. This runs at startup,
    // before any browser work, so the user has a chance to install ffmpeg and
    // re-run rather than discover the problem after a one-hour capture has
    // already produced an unseekable webm.
    static void WarnIfFfmpegMissing(string customFfmpegPath, bool audioOnlyRequested, string? requestedOutput)
    {
        if (!string.IsNullOrEmpty(LocateFfmpeg(customFfmpegPath))) return;

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[!] ffmpeg was not found in PATH or alongside this executable.");
        Console.WriteLine("    The download itself will still work, but the resulting file may have issues:");
        Console.WriteLine("    - In capture mode, the produced .webm has no seek index and many players");
        Console.WriteLine("      will not be able to scrub through it (VLC handles it; the Windows movie");
        Console.WriteLine("      app and most browsers do not).");
        string ext = string.IsNullOrEmpty(requestedOutput) ? "" : Path.GetExtension(requestedOutput);
        if (!string.IsNullOrEmpty(ext) && !ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"    - You asked for a {ext} file but in capture mode we cannot transcode");
            Console.WriteLine("      from webm to that format without ffmpeg, so the .webm will be kept as-is.");
        }
        if (audioOnlyRequested)
        {
            Console.WriteLine("    - --audio (mp3 extraction) requires ffmpeg, so the audio-only step");
            Console.WriteLine("      will be skipped and you will get the full video file instead.");
        }
        Console.WriteLine();
        Console.WriteLine("    To install ffmpeg on Windows (one-time, takes 30 s):");
        Console.WriteLine("      winget install Gyan.FFmpeg");
        Console.WriteLine("      or  choco install ffmpeg");
        Console.WriteLine("      or  download from https://ffmpeg.org/download.html and unzip");
        Console.WriteLine("    Then either add the bin folder to your PATH, or drop ffmpeg.exe into");
        Console.WriteLine("    the same folder as this executable. The tool will pick it up automatically");
        Console.WriteLine("    on the next run.");
        Console.WriteLine();
        Console.WriteLine("    Continuing without ffmpeg...");
        Console.ResetColor();
        Console.WriteLine();
    }

    // Extract an mp3 from any video / webm produced by the download or capture
    // path, then delete the original. No-op (with a warning) if ffmpeg is
    // missing — the user keeps the source file in that case.
    static async Task ExtractAudioMp3Async(string sourcePath, string? customFfmpegPath = null)
    {
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
        {
            Console.WriteLine($"Audio extraction: source file '{sourcePath}' not found, skipping.");
            return;
        }

        string ffmpeg = LocateFfmpeg(customFfmpegPath);
        if (string.IsNullOrEmpty(ffmpeg))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Audio extraction skipped: ffmpeg is not available.");
            Console.WriteLine($"You can run `ffmpeg -i \"{sourcePath}\" -vn -c:a libmp3lame -q:a 2 \"...\\output.mp3\"` manually after installing ffmpeg.");
            Console.ResetColor();
            return;
        }

        string mp3Path = Path.ChangeExtension(sourcePath, ".mp3");
        try { if (File.Exists(mp3Path)) File.Delete(mp3Path); } catch { }
        Console.WriteLine($"Extracting audio to {mp3Path}...");
        bool ok = await RunFfmpegAsyncWithErrorCapture(ffmpeg, $"-y -i \"{sourcePath}\" -vn -c:a libmp3lame -q:a 2 \"{mp3Path}\"");
        if (ok && File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0)
        {
            try { File.Delete(sourcePath); } catch { /* leave source if delete fails */ }
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Audio extracted: {mp3Path} ({new FileInfo(mp3Path).Length:N0} bytes); source video removed.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Audio extraction failed; keeping the original video file.");
            Console.ResetColor();
            try { if (File.Exists(mp3Path) && new FileInfo(mp3Path).Length == 0) File.Delete(mp3Path); } catch { }
        }
    }

    static string LocateFfmpeg(string? customPath = null)
    {
        // If a custom path was provided, validate and use it
        if (!string.IsNullOrEmpty(customPath))
        {
            if (File.Exists(customPath))
            {
                return customPath;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Custom ffmpeg path '{customPath}' does not exist.");
                Console.ResetColor();
                // Fall through to auto-detection
            }
        }

        // Cross-platform candidate names: Windows uses ffmpeg.exe, Unix-like
        // systems use ffmpeg (no extension). We probe both on every host so
        // the lookup also works when running a Linux-style ffmpeg binary on
        // Windows (e.g., a Cygwin / WSL build dropped next to the exe).
        string[] candidates = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
            ? new[] { "ffmpeg.exe", "ffmpeg" }
            : new[] { "ffmpeg", "ffmpeg.exe" };

        // Prefer a sibling ffmpeg next to our own .exe.
        try
        {
            string? selfDir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (!string.IsNullOrEmpty(selfDir))
            {
                foreach (var name in candidates)
                {
                    string candidate = Path.Combine(selfDir, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch { }

        // Fall back to PATH.
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    foreach (var name in candidates)
                    {
                        string p = Path.Combine(dir, name);
                        if (File.Exists(p)) return p;
                    }
                }
            }
        }
        catch { }

        // Last resort on macOS: Homebrew installs ffmpeg in /opt/homebrew/bin
        // (Apple Silicon) or /usr/local/bin (Intel) which are sometimes not on
        // the PATH inherited by GUI-launched apps.
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            foreach (var hard in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
            {
                if (File.Exists(hard)) return hard;
            }
        }

        return string.Empty;
    }

    static async Task<bool> RunFfmpegAsync(string ffmpeg, string args, bool showProgress = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = new Process { StartInfo = psi };
            p.Start();

            // If we need to show progress, read both streams and update a single console line
            if (showProgress)
            {
                var progressData = new Dictionary<string, string>();
                object sync = new object();

                void UpdateProgress()
                {
                    var parts = new List<string>();
                    if (progressData.TryGetValue("time", out var time)) parts.Add($"time={time}");
                    if (progressData.TryGetValue("bitrate", out var bitrate)) parts.Add($"bitrate={bitrate}");
                    if (progressData.TryGetValue("dup", out var dup)) parts.Add($"dup={dup}");
                    if (progressData.TryGetValue("drop", out var drop)) parts.Add($"drop={drop}");
                    if (progressData.TryGetValue("speed", out var speed)) parts.Add($"speed={speed}");

                    if (parts.Count > 0)
                    {
                        string output = $"  [ffmpeg] {string.Join(" ", parts)}";
                        lock (sync)
                        {
                            Console.Write($"\r{output.PadRight(80)}");
                            Console.Out.Flush();
                        }
                    }
                }

                async Task ReadStreamAsync(TextReader reader)
                {
                    var buffer = new char[1024];
                    var builder = new StringBuilder();
                    while (true)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0) break;

                        for (int i = 0; i < read; i++)
                        {
                            char ch = buffer[i];
                            if (ch == '\r' || ch == '\n')
                            {
                                if (builder.Length > 0)
                                {
                                    var line = builder.ToString().Trim();
                                    builder.Clear();
                                    if (line.Contains('='))
                                    {
                                        var kv = line.Split('=', 2);
                                        if (kv.Length == 2)
                                        {
                                            string key = kv[0].Trim();
                                            string value = kv[1].Trim();
                                            if (key == "time" || key == "bitrate" || key == "dup" || key == "drop" || key == "speed")
                                            {
                                                progressData[key] = value;
                                                UpdateProgress();
                                            }
                                        }
                                    }
                                }
                                continue;
                            }
                            builder.Append(ch);
                        }
                    }

                    if (builder.Length > 0)
                    {
                        var line = builder.ToString().Trim();
                        if (line.Contains('='))
                        {
                            var kv = line.Split('=', 2);
                            if (kv.Length == 2)
                            {
                                string key = kv[0].Trim();
                                string value = kv[1].Trim();
                                if (key == "time" || key == "bitrate" || key == "dup" || key == "drop" || key == "speed")
                                {
                                    progressData[key] = value;
                                    UpdateProgress();
                                }
                            }
                        }
                    }
                }

                var stderrTask = ReadStreamAsync(p.StandardError);
                var stdoutTask = ReadStreamAsync(p.StandardOutput);
                await Task.WhenAll(stderrTask, stdoutTask, p.WaitForExitAsync());
                Console.WriteLine();
            }
            else
            {
                // Original behavior: silently drain both streams
                var _o = p.StandardOutput.ReadToEndAsync();
                var _e = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                await _o; await _e;
            }

            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ffmpeg invocation failed: {ex.Message}");
            return false;
        }
    }

    static async Task<bool> RunFfmpegAsyncWithErrorCapture(string ffmpeg, string args, bool showProgress = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = new Process { StartInfo = psi };
            p.Start();

            string stderrOutput = "";
            string stdoutOutput = "";

            // If we need to show progress, read both streams and update a single console line
            if (showProgress)
            {
                var progressData = new Dictionary<string, string>();
                object sync = new object();

                void UpdateProgress()
                {
                    var parts = new List<string>();
                    if (progressData.TryGetValue("time", out var time)) parts.Add($"time={time}");
                    if (progressData.TryGetValue("bitrate", out var bitrate)) parts.Add($"bitrate={bitrate}");
                    if (progressData.TryGetValue("dup", out var dup)) parts.Add($"dup={dup}");
                    if (progressData.TryGetValue("drop", out var drop)) parts.Add($"drop={drop}");
                    if (progressData.TryGetValue("speed", out var speed)) parts.Add($"speed={speed}");

                    if (parts.Count > 0)
                    {
                        string output = $"  [ffmpeg] {string.Join(" ", parts)}";
                        lock (sync)
                        {
                            Console.Write($"\r{output.PadRight(80)}");
                            Console.Out.Flush();
                        }
                    }
                }

                async Task ReadStreamAsync(TextReader reader, StringBuilder outputBuilder)
                {
                    var buffer = new char[1024];
                    var builder = new StringBuilder();
                    while (true)
                    {
                        int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0) break;

                        for (int i = 0; i < read; i++)
                        {
                            char ch = buffer[i];
                            outputBuilder.Append(ch);
                            if (ch == '\r' || ch == '\n')
                            {
                                if (builder.Length > 0)
                                {
                                    var line = builder.ToString().Trim();
                                    builder.Clear();
                                    // Parse progress line: "frame= 123 fps=25 q=28.0 size=256kB time=00:00:05.00 bitrate=400.0kbits/s dup=0 drop=0 speed=1.00x"
                                    if (line.Contains('='))
                                    {
                                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                        foreach (var part in parts)
                                        {
                                            if (part.Contains('='))
                                            {
                                                var kv = part.Split('=', 2);
                                                if (kv.Length == 2)
                                                {
                                                    string key = kv[0].Trim();
                                                    string value = kv[1].Trim();
                                                    if (key == "time" || key == "bitrate" || key == "dup" || key == "drop" || key == "speed")
                                                    {
                                                        progressData[key] = value;
                                                    }
                                                }
                                            }
                                        }
                                        UpdateProgress();
                                    }
                                }
                                continue;
                            }
                            builder.Append(ch);
                        }
                    }

                    if (builder.Length > 0)
                    {
                        var line = builder.ToString().Trim();
                        outputBuilder.Append(line);
                        if (line.Contains('='))
                        {
                            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var part in parts)
                            {
                                if (part.Contains('='))
                                {
                                    var kv = part.Split('=', 2);
                                    if (kv.Length == 2)
                                    {
                                        string key = kv[0].Trim();
                                        string value = kv[1].Trim();
                                        if (key == "time" || key == "bitrate" || key == "dup" || key == "drop" || key == "speed")
                                        {
                                            progressData[key] = value;
                                        }
                                    }
                                }
                            }
                            UpdateProgress();
                        }
                    }
                }

                var stderrBuilder = new StringBuilder();
                var stdoutBuilder = new StringBuilder();
                var stderrTask = ReadStreamAsync(p.StandardError, stderrBuilder);
                var stdoutTask = ReadStreamAsync(p.StandardOutput, stdoutBuilder);
                await Task.WhenAll(stderrTask, stdoutTask, p.WaitForExitAsync());
                Console.WriteLine();
                stderrOutput = stderrBuilder.ToString();
                stdoutOutput = stdoutBuilder.ToString();
            }
            else
            {
                // Original behavior: silently drain both streams
                var _o = p.StandardOutput.ReadToEndAsync();
                var _e = p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                await _o; await _e;
            }

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"ffmpeg failed with exit code {p.ExitCode}");
                if (!string.IsNullOrEmpty(stderrOutput))
                {
                    Console.WriteLine("ffmpeg stderr:");
                    Console.WriteLine(stderrOutput);
                }
                if (!string.IsNullOrEmpty(stdoutOutput))
                {
                    Console.WriteLine("ffmpeg stdout:");
                    Console.WriteLine(stdoutOutput);
                }
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ffmpeg invocation failed: {ex.Message}");
            return false;
        }
    }

}
