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
            var launchOptions = new LaunchOptions
            {
                Headless = RunHeadless,
                Args = new[] { "--no-sandbox" }, // Often needed on Linux/Docker
                UserDataDir = userDataDir // Uncomment to use a persistent session
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
                if (e.Response.Url.Contains("videomanifest?provider", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Potential manifest found: {e.Response.Url}");

                    // Capture the headers the browser actually sent for this request.
                    // The browser's request succeeds; if we replay these exact headers
                    // from yt-dlp the *.svc.ms CDN should accept it too.
                    if (capturedManifestHeaders == null)
                    {
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
                    }

                    // Attempt to set the result. TrySetResult prevents exceptions if already set.
                    manifestFoundTcs.TrySetResult(e.Response.Url);
                }
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


            Console.WriteLine("Page loaded. Looking for video player and attempting to play...");

            // 7. Wait for Video Element and Click Play
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
