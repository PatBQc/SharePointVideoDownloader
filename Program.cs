using System;
using System.Diagnostics;
using System.IO;
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

    static async Task Main(string[] args)
    {
        Console.WriteLine("SharePoint/Stream Video Downloader using Puppeteer Sharp and yt-dlp");
        Console.WriteLine("-----------------------------------------------------------------");

        // 1. Get Target URL from User
        Console.Write("Enter the SharePoint/Stream video page URL: ");
        string targetUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(targetUrl) || !Uri.TryCreate(targetUrl, UriKind.Absolute, out _))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Invalid URL provided.");
            Console.ResetColor();
            return;
        }

        // 2. Get Desired Output Filename
        Console.Write("Enter the desired output filename (e.g., my_video.mp4): ");
        string outputFilename = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(outputFilename))
        {
            // Create a default filename if none provided
            outputFilename = $"downloaded_video_{DateTime.Now:yyyyMMddHHmmss}.mp4";
            Console.WriteLine($"No filename provided. Using default: {outputFilename}");
        }
        // Ensure it ends with a common video extension (yt-dlp often handles this, but good practice)
        if (!outputFilename.Contains('.'))
        {
            outputFilename += ".mp4"; // Default to mp4 if no extension
        }


        string manifestUrl = null;
        var manifestFoundTcs = new TaskCompletionSource<string>(); // To signal when manifest is found

        IBrowser browser = null;
        IPage page = null;

        try
        {
            // 3. Launch Puppeteer
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

            // 4. Setup Network Interception (Listen for Responses)
            Console.WriteLine("Setting up network listener...");
            page.Response += async (sender, e) =>
            {
                // Check if the URL contains the specific videomanifest marker
                if (e.Response.Url.Contains("videomanifest?provider", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Potential manifest found: {e.Response.Url}");
                    // Attempt to set the result. TrySetResult prevents exceptions if already set.
                    manifestFoundTcs.TrySetResult(e.Response.Url);
                }
            };

            // 5. Navigate to the Page
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

            // 6. Wait for Video Element and Click Play
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


            // 7. Wait for the Manifest URL
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

            // 8. Process the Manifest URL
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


            // 9. Execute yt-dlp
            Console.WriteLine($"Starting yt-dlp to download video as '{outputFilename}'...");
            await RunYtDlp(shortenedUrl, outputFilename);

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
            // 10. Cleanup
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

    static async Task RunYtDlp(string videoUrl, string outputFilename)
    {
        // Ensure filename is quoted in case it contains spaces
        // Ensure URL is quoted as it's very long and contains special characters
        string arguments = $"\"{videoUrl}\" -o \"{outputFilename}\"";

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

        Console.WriteLine($"Executing: {processStartInfo.FileName} {processStartInfo.Arguments}");

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
                    Console.WriteLine($"yt-dlp finished successfully. Video saved as '{outputFilename}'");
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
}