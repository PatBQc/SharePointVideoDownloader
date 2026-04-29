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
    // OPTION 1: Place yt-dlp.exe next to your app's .exe or ensure it's in PATH
    const string YtDlpPath = "yt-dlp.exe";
    // OPTION 2: Provide the full path if yt-dlp is elsewhere
    // const string YtDlpPath = @"C:\path\to\your\yt-dlp.exe"; 

    // Set to false to see the browser window (useful for debugging/initial login)
    const bool RunHeadless = false;
    // Optional: Specify a user data directory to persist sessions/logins
    static string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PuppeteerSession");
    // --- End Configuration ---

    static void ShowHelp()
    {
        Console.WriteLine("SharePoint/Stream Video Downloader Usage:");
        Console.WriteLine("-----------------------------------------");
        Console.WriteLine("Interactive mode (no arguments):");
        Console.WriteLine("  The program will prompt you for URL, download type, and output filename.");
        Console.WriteLine();
        Console.WriteLine("Command-line arguments:");
        Console.WriteLine("  -u, --url <URL>         : (Required) The SharePoint/Stream video page URL.");
        Console.WriteLine("                            Important: enclose the URL within \"double quotes\"");
        Console.WriteLine("                            if it contains special characters like & or =");
        Console.WriteLine();
        Console.WriteLine("  -a, --audio             : (Optional) Download audio only (MP3). Defaults to video (MP4).");
        Console.WriteLine();
        Console.WriteLine("  -o, --output <FILENAME> : (Optional) Desired output filename (e.g., my_video.mp4 or my_audio.mp3).");
        Console.WriteLine("                            If not provided, a default name will be generated.");
        Console.WriteLine();
        Console.WriteLine("  -c, --capture           : (Optional) Force the browser-side capture path (record the");
        Console.WriteLine("                            playing video + audio via getDisplayMedia + MediaRecorder).");
        Console.WriteLine("                            Use this when the meeting is shared with you in view-only");
        Console.WriteLine("                            mode (no Download permission) and is DRM-protected.");
        Console.WriteLine("                            Output is .webm (VP9 + Opus).");
        Console.WriteLine();
        Console.WriteLine("  --capture-seconds <N>   : (Optional, capture mode only) Stop the recording after N");
        Console.WriteLine("                            seconds even if the video has not ended. Useful for testing.");
        Console.WriteLine();
        Console.WriteLine("  -h, --help, -?, /?      : Display this help message.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  SharePointVideoDownloader.exe -u \"https://your-sharepoint-site.com/video/123\" -o \"meeting_recording.mp4\"");
        Console.WriteLine("  SharePointVideoDownloader.exe --url \"https://your-stream-link.com/vid/abc\" --audio --output \"podcast_episode.mp3\"");
        Console.WriteLine("  SharePointVideoDownloader.exe -u \"https://url.com/video\" (will prompt for output filename if not specified and use default for audio/video)");
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
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine("SharePoint/Stream Video Downloader using Puppeteer Sharp and yt-dlp");
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine();

        string targetUrl = null;
        bool audioOnly = false;
        string outputFilename = null;
        bool useArgs = false;
        bool argsValid = true;
        bool captureMode = false;
        int captureMaxSeconds = 0;

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
            string downloadTypeInput = Console.ReadLine()?.Trim().ToUpperInvariant();
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
                 Console.WriteLine($"Warning: Provided extension '{currentExtension}' is not a typical video extension (.mp4, .mkv, .webm, .mov). yt-dlp will attempt to download in the best available video format.");
                 Console.ResetColor();
            }
        }

        string manifestUrl = null;
        var manifestFoundTcs = new TaskCompletionSource<string>(); // To signal when manifest is found
        // Captured request headers from the actual successful manifest fetch in the
        // browser. yt-dlp will replay these so its request looks identical to the
        // browser's — the *.svc.ms CDN authenticates based on these headers (and
        // possibly an Authorization Bearer token injected by the SharePoint JS player).
        System.Collections.Generic.Dictionary<string, string> capturedManifestHeaders = null;

        IBrowser browser = null;
        IPage page = null;

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
            var launchOptions = new LaunchOptions
            {
                Headless = captureMode ? false : RunHeadless, // Capture path needs a real surface for DRM
                Args = browserArgs.ToArray(),
                UserDataDir = userDataDir,
                DefaultViewport = null, // let the OS window size drive the viewport in capture mode
                // ExecutablePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe" // Example: Use existing Chrome
            };

            // Download browser if needed
            var browserFetcher = new BrowserFetcher();
            Console.WriteLine("Ensuring browser is available...");
            await browserFetcher.DownloadAsync();

            browser = await Puppeteer.LaunchAsync(launchOptions);
            page = await browser.NewPageAsync();
            await page.SetViewportAsync(new ViewPortOptions { Width = 1280, Height = 800 });

            // 5. Setup Network Interception (Listen for Responses)
            Console.WriteLine("Setting up network listener...");
            page.Response += async (sender, e) =>
            {
                // Check if the URL contains the specific videomanifest marker
                if (!e.Response.Url.Contains("videomanifest?provider", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // The browser sends a CORS preflight (OPTIONS) before the real GET.
                // The preflight's request headers carry only Access-Control-Request-*
                // metadata — NOT the SharePoint POP auth token (x-spopactoken) that
                // the real GET adds. We need to skip the preflight and capture the
                // actual fetch.
                System.Net.Http.HttpMethod? method = null;
                try { method = e.Response?.Request?.Method; } catch { /* best effort */ }
                bool isPreflight = method != null && method.Equals(System.Net.Http.HttpMethod.Options);

                Console.WriteLine($"Potential manifest {(isPreflight ? "preflight (OPTIONS)" : "fetch")} found: {e.Response.Url}");

                if (isPreflight)
                {
                    // Don't resolve the TCS yet — wait for the real GET so headers are
                    // populated by the time downstream code reads capturedManifestHeaders.
                    return;
                }

                try
                {
                    var reqHeaders = e.Response?.Request?.Headers;
                    if (reqHeaders != null)
                    {
                        capturedManifestHeaders = new System.Collections.Generic.Dictionary<string, string>(
                            reqHeaders, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch { /* best effort */ }

                // Attempt to set the result. TrySetResult prevents exceptions if already set.
                manifestFoundTcs.TrySetResult(e.Response.Url);
            };

            // 6. Navigate to the Page
            Console.WriteLine($"Navigating to: {targetUrl}");
            try
            {
                await page.GoToAsync(targetUrl, WaitUntilNavigation.Networkidle2); // Increased timeout, wait for network to be relatively idle
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
                bool capOk = await TryCaptureViaPlaybackAsync(page, outputFilename, captureMaxSeconds);
                if (capOk) return;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Capture path failed. See messages above.");
                Console.ResetColor();
                return;
            }

            // 7. Try direct file download from SharePoint first.
            // Microsoft now serves DRM-protected DASH streams to the web player on
            // *.svc.ms, which yt-dlp cannot decrypt (no CDM). The original mp4 is
            // still available via SharePoint's standard "?download=1" endpoint as
            // long as the user has read access — which they obviously do, since
            // they can play the video in the browser.
            try
            {
                bool directOk = await TryDirectDownloadAsync(page, targetUrl, outputFilename);
                if (directOk)
                {
                    return; // Done — skip the manifest/yt-dlp fallback entirely.
                }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Direct download did not complete; falling back to manifest + yt-dlp...");
                Console.ResetColor();
            }
            catch (Exception ddEx)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Direct download attempt threw: {ddEx.Message}");
                Console.WriteLine("Falling back to manifest + yt-dlp...");
                Console.ResetColor();
            }

            Console.WriteLine("Looking for video player and attempting to play...");

            // 8. Wait for Video Element and Click Play (legacy path — only used when
            // direct download was not possible)
            try
            {
                // Try common selectors for video players or play buttons
                // Adjust these selectors if they don't work for your specific page structure.
                // Inspect the element in your browser's DevTools (F12) to find the right one.
                string[] possibleSelectors = {
                    "video",                               // The video tag itself
                    "[data-testid='media-play-button']",   // Common test ID
                    "button[aria-label='Play']",           // Accessibility label
                    ".playbutton_playpause",               // A class name seen on some players
                    "[class*='videoPlayer--play']"         // Partial class match
                    // Add more potential selectors here
                };

                IElementHandle playElement = null;
                foreach (var selector in possibleSelectors)
                {
                    try
                    {
                        playElement = await page.WaitForSelectorAsync(selector, new WaitForSelectorOptions { Timeout = 20000 }); // Wait 20s for element
                        if (playElement != null)
                        {
                            Console.WriteLine($"Found player/button with selector: {selector}");
                            break; // Found one, exit loop
                        }
                    }
                    catch (WaitTaskTimeoutException)
                    {
                        Console.WriteLine($"Selector '{selector}' not found or timed out.");
                    }
                }


                if (playElement != null)
                {
                    await Task.Delay(1000); // Small delay before clicking
                    Console.WriteLine("Clicking play element...");
                    await playElement.ClickAsync();
                    await Task.Delay(2000); // Wait a bit for playback to potentially start triggering network requests
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: Could not find a recognizable play button/video element to click automatically.");
                    Console.WriteLine("Playback might need to be started manually if the manifest isn't found.");
                    Console.ResetColor();
                    // We'll still wait for the manifest below, in case it loaded anyway or the user clicks play manually
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: Error trying to find or click play button: {ex.Message}");
                Console.ResetColor();
                // Continue trying to find the manifest
            }


            // 8. Wait for the Manifest URL
            Console.WriteLine("Waiting for videomanifest URL (up to 60 seconds)...");
            try
            {
                // Wait for the TaskCompletionSource to be set by the Response event handler OR timeout
                var completedTask = await Task.WhenAny(manifestFoundTcs.Task, Task.Delay(60000));

                if (completedTask == manifestFoundTcs.Task)
                {
                    manifestUrl = await manifestFoundTcs.Task; // Get the result
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Successfully captured manifest URL: {manifestUrl.Substring(0, Math.Min(manifestUrl.Length, 100))}..."); // Show beginning
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Error: Timed out waiting for the videomanifest URL.");
                    Console.WriteLine("Possible reasons: Video didn't play, page structure changed, login required, or manifest URL pattern differs.");
                    Console.ResetColor();
                    return; // Exit if timed out
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error while waiting for manifest: {ex.Message}");
                Console.ResetColor();
                return;
            }

            // 9. Process the Manifest URL
            Console.WriteLine("Processing manifest URL...");
            string searchTerm = "index&format=dash";
            int index = manifestUrl.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);

            if (index == -1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: Could not find '{searchTerm}' in the captured manifest URL.");
                Console.WriteLine($"Full URL was: {manifestUrl}");
                Console.ResetColor();
                return;
            }

            // Get the substring up to and including the search term
            string shortenedUrl = manifestUrl.Substring(0, index + searchTerm.Length);
            Console.WriteLine($"Shortened URL: {shortenedUrl.Substring(0, Math.Min(shortenedUrl.Length, 100))}..."); // Show beginning


            // 10. Export browser session so yt-dlp can authenticate to the media CDN.
            // Microsoft's *.svc.ms CDN rejects requests that lack the auth context the
            // SharePoint web player attaches. The signed query parameters embedded in
            // the manifest URL are necessary but no longer sufficient. We therefore:
            //   (a) export every cookie from the browser via CDP Network.getAllCookies
            //       (the per-URL cookies API misses partitioned/cross-domain cookies);
            //   (b) replay the exact request headers the browser sent for the manifest
            //       (typically including Authorization, Origin, Sec-Fetch-*, X-MS-*).
            // Both pieces are forwarded to yt-dlp.
            string? cookiesFile = null;
            try
            {
                Console.WriteLine("Exporting browser cookies (all domains, via CDP) for yt-dlp...");
                cookiesFile = await ExportAllCookiesToNetscapeAsync(page);

                if (capturedManifestHeaders == null || capturedManifestHeaders.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Warning: no request headers were captured for the manifest fetch. yt-dlp will fall back to default headers, which may fail with 401.");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"Captured {capturedManifestHeaders.Count} request header(s) from the browser's manifest fetch.");
                }

                // 11. Execute yt-dlp
                Console.WriteLine($"Starting yt-dlp to download {(audioOnly ? "audio" : "video")} as '{outputFilename}'...");
                await RunYtDlp(shortenedUrl, outputFilename, audioOnly, cookiesFile, capturedManifestHeaders);
            }
            finally
            {
                // The cookies file contains live session credentials — wipe it ASAP.
                if (!string.IsNullOrEmpty(cookiesFile) && File.Exists(cookiesFile))
                {
                    try { File.Delete(cookiesFile); } catch { /* ignore */ }
                }
            }

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

    static async Task RunYtDlp(string videoUrl, string outputFilename, bool audioOnly,
        string? cookiesFile = null,
        System.Collections.Generic.Dictionary<string, string>? requestHeaders = null)
    {
        string effectiveOutputFilename = outputFilename;
        string arguments;

        // Build common auth/header flags. These are required because Microsoft's media
        // CDN authenticates via the SharePoint browser session, not just the URL.
        var prefix = new StringBuilder();
        if (!string.IsNullOrEmpty(cookiesFile))
        {
            prefix.Append($"--cookies \"{cookiesFile}\" ");
        }

        // Replay the exact request headers the browser used for the manifest fetch.
        // Skip headers that yt-dlp manages itself or that would corrupt our quoting.
        if (requestHeaders != null && requestHeaders.Count > 0)
        {
            var deny = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Provided to yt-dlp via --cookies file
                "cookie",
                // Set automatically by HTTP stack
                "host", "content-length", "transfer-encoding", "connection",
                "expect", "te", "upgrade", "trailer",
                // yt-dlp manages compression and ranges itself
                "accept-encoding", "range",
                // Cache validators we don't want to replay
                "if-modified-since", "if-none-match",
                // POST body type — irrelevant for our GETs
                "content-type",
                // CORS preflight metadata — not part of the real fetch.
                "access-control-request-method", "access-control-request-headers",
            };

            var forwarded = new System.Collections.Generic.List<string>();
            foreach (var kv in requestHeaders)
            {
                string name = kv.Key;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith(":", StringComparison.Ordinal)) continue; // HTTP/2 pseudo-headers
                if (deny.Contains(name)) continue;
                string value = kv.Value ?? string.Empty;
                // Skip values containing characters that would break our shell quoting.
                if (value.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) continue;

                // yt-dlp expects KEY:VALUE for --add-header.
                prefix.Append($"--add-header \"{name}:{value}\" ");
                forwarded.Add(name);
            }

            if (forwarded.Count > 0)
            {
                Console.WriteLine($"Forwarding {forwarded.Count} header(s) to yt-dlp: {string.Join(", ", forwarded)}");
            }
        }

        if (audioOnly)
        {
            // Ensure the filename for yt-dlp has an .mp3 extension for audio
            effectiveOutputFilename = Path.ChangeExtension(outputFilename, ".mp3");
            arguments = $"{prefix}\"{videoUrl}\" -x --extract-audio --audio-format mp3 --audio-quality 0 -o \"{effectiveOutputFilename}\"";
            Console.WriteLine($"Requesting audio extraction to: {effectiveOutputFilename}");
        }
        else
        {
            // Ensure filename is quoted in case it contains spaces
            // Ensure URL is quoted as it's very long and contains special characters
            arguments = $"{prefix}\"{videoUrl}\" -o \"{outputFilename}\"";
        }

        // Add --verbose for more detailed yt-dlp output during debugging
        // arguments += " --verbose";

        var processStartInfo = new ProcessStartInfo
        {
            FileName = YtDlpPath, // Path to yt-dlp executable
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,     // Required for redirection
            CreateNoWindow = true,       // Don't show the yt-dlp console window
        };

        // Mask sensitive bits in the echoed command line: the cookies file path
        // points at live session credentials, and any forwarded Authorization header
        // value is a Bearer token that grants access to the user's tenant.
        string echoedArgs = arguments;
        if (!string.IsNullOrEmpty(cookiesFile))
        {
            echoedArgs = echoedArgs.Replace(cookiesFile, "<cookies-file>");
        }
        echoedArgs = System.Text.RegularExpressions.Regex.Replace(
            echoedArgs,
            "--add-header \"(?<n>[Aa]uthorization|[Pp]roxy-[Aa]uthorization|[Xx]-[Mm]s-[Aa]uth[^:]*):[^\"]*\"",
            "--add-header \"${n}:<redacted>\"");
        Console.WriteLine($"Executing: {processStartInfo.FileName} {echoedArgs}");

        using (var process = new Process { StartInfo = processStartInfo })
        {
            // Capture standard output and error streams
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null) Console.WriteLine($"[yt-dlp] {e.Data}");
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[yt-dlp ERR] {e.Data}");
                    Console.ResetColor();
                }
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine(); // Start reading output asynchronously
                process.BeginErrorReadLine();  // Start reading error asynchronously

                await process.WaitForExitAsync(); // Wait for the process to complete

                if (process.ExitCode == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"yt-dlp finished successfully. {(audioOnly ? "Audio" : "Video")} saved as '{effectiveOutputFilename}'");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"yt-dlp exited with error code: {process.ExitCode}");
                    Console.WriteLine("Check the [yt-dlp ERR] messages above for details.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to run yt-dlp: {ex.Message}");
                if (ex is System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 2)
                {
                    Console.WriteLine($"'{YtDlpPath}' not found. Make sure yt-dlp is installed and its path is correct in the script or system PATH.");
                }
                Console.ResetColor();
            }
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
    static async Task<bool> TryDirectDownloadAsync(IPage page, string pageUrl, string outputFilename)
    {
        Uri pageUri;
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
        string downloadDir = Path.GetDirectoryName(Path.GetFullPath(outputFilename)) ?? Directory.GetCurrentDirectory();
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
    }

    // Capture the playing video via getDisplayMedia + MediaRecorder, then save the
    // resulting webm. This is the only legitimate path for content the user has
    // streaming-only access to (DRM-protected, no Download permission).
    //
    // The browser was launched with --mute-audio so no sound reaches the speakers;
    // the open question — verified by running this — is whether the captured audio
    // track still contains real samples or whether --mute-audio also silences the
    // capture pipeline. Diagnostic output reports both conditions.
    static async Task<bool> TryCaptureViaPlaybackAsync(IPage page, string outputFilename, int maxSeconds)
    {
        // Force a .webm extension on the output: MediaRecorder produces VP9 + Opus
        // in a WebM container. Transcoding to mp4 would require ffmpeg, which is a
        // separate concern — we punt on it here.
        string webmName = Path.GetFileNameWithoutExtension(outputFilename);
        if (string.IsNullOrEmpty(webmName)) webmName = "capture";
        webmName += ".webm";
        string downloadDir = Path.GetDirectoryName(Path.GetFullPath(outputFilename)) ?? Directory.GetCurrentDirectory();
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
            videoHeight: window.__spvd.videoHeight
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

            video.addEventListener('ended', function(){ try { recorder.stop(); } catch (e) {} });

            recorder.start(2000);
            window.__spvd.recorder = recorder;
            window.__spvd.state = 'recording';

            if (maxSeconds > 0) {
                setTimeout(function() {
                    try { if (recorder.state === 'recording') recorder.stop(); } catch (e) {}
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
                Console.WriteLine($"Capture: in-page Blob assembled ({status.BlobSize:N0} bytes), waiting for the file to land on disk...");
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
                await PostProcessCaptureAsync(finalPath, outputFilename);

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
    static async Task PostProcessCaptureAsync(string rawWebmPath, string requestedOutput)
    {
        string ffmpeg = LocateFfmpeg();
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
        bool remuxOk = await RunFfmpegAsync(ffmpeg, $"-y -i \"{rawWebmPath}\" -c copy \"{remuxed}\"");
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
        string requestedExt = Path.GetExtension(requestedOutput);
        if (!string.IsNullOrEmpty(requestedExt) && !requestedExt.Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            string outDir = Path.GetDirectoryName(Path.GetFullPath(rawWebmPath)) ?? Directory.GetCurrentDirectory();
            string mp4Path = Path.Combine(outDir, Path.GetFileNameWithoutExtension(rawWebmPath) + requestedExt);
            try { if (File.Exists(mp4Path)) File.Delete(mp4Path); } catch { }
            Console.WriteLine($"Transcoding to {requestedExt} (this re-encodes; takes a while)...");
            bool tx = await RunFfmpegAsync(ffmpeg, $"-y -i \"{rawWebmPath}\" -c:v libx264 -preset veryfast -crf 22 -c:a aac -b:a 160k -movflags +faststart \"{mp4Path}\"");
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

    static string LocateFfmpeg()
    {
        // Prefer a sibling ffmpeg.exe next to our own .exe; fall back to PATH.
        try
        {
            string? selfDir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
            if (!string.IsNullOrEmpty(selfDir))
            {
                string candidate = Path.Combine(selfDir, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }

        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrEmpty(dir)) continue;
                    string p = Path.Combine(dir, "ffmpeg.exe");
                    if (File.Exists(p)) return p;
                    string p2 = Path.Combine(dir, "ffmpeg");
                    if (File.Exists(p2)) return p2;
                }
            }
        }
        catch { }

        return string.Empty;
    }

    static async Task<bool> RunFfmpegAsync(string ffmpeg, string args)
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
            // Drain both streams so ffmpeg does not block on a full pipe.
            var _o = p.StandardOutput.ReadToEndAsync();
            var _e = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            await _o; await _e;
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ffmpeg invocation failed: {ex.Message}");
            return false;
        }
    }

    // DTOs for Network.getAllCookies CDP response. The CDP cookie shape (camelCase
    // properties) maps onto these via System.Text.Json's case-insensitive matching.
    private sealed class CdpCookie
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Path { get; set; } = "/";
        public double Expires { get; set; } = -1;
        public bool HttpOnly { get; set; }
        public bool Secure { get; set; }
        public bool Session { get; set; }
    }

    private sealed class CdpAllCookiesResponse
    {
        public CdpCookie[]? Cookies { get; set; }
    }

    // Export *every* cookie known to the browser into a Netscape cookies.txt file
    // that yt-dlp can consume via --cookies. Uses the raw CDP Network.getAllCookies
    // call rather than IPage.GetCookiesAsync(urls) because the latter filters by URL
    // and silently drops cookies set on unrelated domains during the auth flow
    // (login.microsoftonline.com, *.office.com, partitioned cookies, etc.).
    static async Task<string> ExportAllCookiesToNetscapeAsync(IPage page)
    {
        CdpCookie[] cookies = Array.Empty<CdpCookie>();
        try
        {
            var cdp = await page.CreateCDPSessionAsync();
            var response = await cdp.SendAsync<CdpAllCookiesResponse>("Network.getAllCookies");
            cookies = response?.Cookies ?? Array.Empty<CdpCookie>();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: failed to read all cookies via CDP: {ex.Message}");
            Console.ResetColor();
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Netscape HTTP Cookie File");
        sb.AppendLine("# Generated by SharePointVideoDownloader for yt-dlp authentication.");
        sb.AppendLine();

        int written = 0;
        var perDomain = new System.Collections.Generic.SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cookies)
        {
            if (c == null || string.IsNullOrEmpty(c.Name) || string.IsNullOrEmpty(c.Domain))
            {
                continue;
            }
            string value = c.Value ?? string.Empty;
            // Netscape cookies.txt is tab-separated. Skip cookies whose fields contain
            // tab/newline characters since they'd corrupt the file.
            if (c.Name.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) continue;
            if (value.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) continue;

            string domain = c.Domain;
            // The Netscape format flag for "include subdomains" is TRUE iff the domain
            // begins with a leading dot.
            bool includeSubdomains = domain.StartsWith(".", StringComparison.Ordinal);

            string path = string.IsNullOrEmpty(c.Path) ? "/" : c.Path;

            // Session cookies (no expiry) are written as 0; otherwise use the unix ts.
            long expires = (!c.Session && c.Expires > 0) ? (long)c.Expires : 0L;

            // yt-dlp recognises the "#HttpOnly_" prefix (Mozilla extension) to
            // preserve the HttpOnly flag on a cookie.
            string domainLine = c.HttpOnly ? "#HttpOnly_" + domain : domain;

            sb.Append(domainLine).Append('\t')
              .Append(includeSubdomains ? "TRUE" : "FALSE").Append('\t')
              .Append(path).Append('\t')
              .Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
              .Append(expires).Append('\t')
              .Append(c.Name).Append('\t')
              .AppendLine(value);
            written++;

            string bucket = includeSubdomains ? domain.TrimStart('.') : domain;
            perDomain[bucket] = perDomain.TryGetValue(bucket, out var n) ? n + 1 : 1;
        }

        string filePath = Path.Combine(Path.GetTempPath(), $"spvd_cookies_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filePath, sb.ToString());
        Console.WriteLine($"Wrote {written} cookies (across {perDomain.Count} domain(s)) to temporary file for yt-dlp.");
        if (perDomain.Count > 0)
        {
            Console.WriteLine("  Cookies per domain: " + string.Join(", ", perDomain.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        return filePath;
    }
}
