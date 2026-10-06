using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace AviationIntel.Connector.Extraction;

/// <summary>What Readability made of one article page.</summary>
public sealed record ExtractedArticle(
    string          FinalUrl,
    string          Title,
    string          TextContent,
    string          Excerpt,
    string          Byline,
    string          Author,
    string          SiteName,
    string          Language,
    string          FeaturedImage,
    DateTimeOffset? PublishedAt,
    bool            IsReadable,
    int             HttpStatus);

/// <summary>
/// Loads article pages in headless Chromium and runs Readability (SmartReader) over the DOM the
/// browser ended up with.
/// </summary>
/// <remarks>
/// <para>
/// The workspace's own <c>RssTask</c> hands the URL to SmartReader, which does a plain HTTP GET. That
/// misses three things this corpus has a lot of: newsroom pages whose body is rendered client-side,
/// pages behind a bot check that a real browser passes, and consent interstitials that only a page
/// with scripts gets past. Loading the page in a browser and giving Readability the rendered DOM
/// fixes all three without a per-site scraper.
/// </para>
/// <para>
/// Politeness is per host: one page at a time per host, with a delay between them, while different
/// hosts load in parallel up to the configured concurrency. Images, media and fonts are never
/// requested - Readability reads the DOM, not the pixels.
/// </para>
/// </remarks>
public sealed class ArticleExtractor : IAsyncDisposable
{
    private readonly ConnectorOptions                         _options;
    private readonly SemaphoreSlim                            _concurrency;
    private readonly ConcurrentDictionary<string, HostGate>   _hosts = new(StringComparer.OrdinalIgnoreCase);
    private          IPlaywright?                             _playwright;
    private          IBrowser?                                _browser;
    private          IBrowserContext?                         _context;

    private ArticleExtractor(ConnectorOptions options)
    {
        _options     = options;
        _concurrency = new SemaphoreSlim(Math.Max(1, options.BrowserConcurrency));
    }

    public static async Task<ArticleExtractor> StartAsync(ConnectorOptions options)
    {
        var extractor = new ArticleExtractor(options);

        extractor._playwright = await Playwright.CreateAsync();

        //An explicit executable lets the connector run against a Chromium that was installed by
        //something other than `playwright install` (a container image, a CI cache). Left empty,
        //Playwright resolves its own.
        extractor._browser = await extractor._playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless       = true,
            ExecutablePath = string.IsNullOrWhiteSpace(options.ChromiumPath) ? null : options.ChromiumPath,
            Args           = new[] { "--no-sandbox", "--disable-dev-shm-usage" },
            Proxy          = ProxyFromEnvironment(),
        });

        extractor._context = await extractor._browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent         = options.UserAgent,
            Locale            = "en-US",
            JavaScriptEnabled = true,
            ViewportSize      = new ViewportSize { Width = 1280, Height = 900 },
        });

        await extractor._context.RouteAsync("**/*", async route =>
        {
            var type = route.Request.ResourceType;

            if (type is "image" or "media" or "font") await route.AbortAsync();
            else                                      await route.ContinueAsync();
        });

        return extractor;
    }

    /// <summary>Loads one page and extracts it. Never throws for a page that fails: the failure is in the result.</summary>
    public async Task<(ExtractedArticle? Article, string? Error)> ExtractAsync(string url, CancellationToken cancellationToken)
    {
        var gate = _hosts.GetOrAdd(Urls.Host(url), _ => new HostGate());

        await gate.Semaphore.WaitAsync(cancellationToken);

        try
        {
            var wait = gate.NextAllowed - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken);

            await _concurrency.WaitAsync(cancellationToken);

            try
            {
                return await LoadAndReadAsync(url);
            }
            finally
            {
                _concurrency.Release();
            }
        }
        finally
        {
            gate.NextAllowed = DateTimeOffset.UtcNow + _options.PerHostDelay;
            gate.Semaphore.Release();
        }
    }

    /// <summary>
    /// Fetches a feed document through the browser - for the publishers whose bot protection answers a
    /// plain HTTP client with a 403 or a challenge page but lets a browser through.
    /// </summary>
    /// <remarks>
    /// A challenge (Imperva, Cloudflare, Akamai) is a page whose script proves it is a browser and sets a
    /// cookie. So the page is loaded and given time to run, and the feed is then requested again through
    /// the context's request API, which carries the cookies the challenge just set - rather than read off
    /// the first response, which is the challenge itself.
    /// </remarks>
    public async Task<(string? Body, int Status, string? Error)> FetchTextAsync(string url)
    {
        var page = await _context!.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = (float)_options.PageTimeout.TotalMilliseconds });

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
            }
            catch (TimeoutException)
            {
            }

            var again = await _context.APIRequest.GetAsync(url, new APIRequestContextOptions { Timeout = (float)_options.PageTimeout.TotalMilliseconds });

            if (again.Ok) return (await again.TextAsync(), again.Status, null);

            return response is null
                ? (null, again.Status, $"HTTP {again.Status}")
                : (await response.TextAsync(), response.Status, response.Ok ? null : $"HTTP {response.Status}");
        }
        catch (PlaywrightException e)
        {
            return (null, 0, FirstLine(e.Message));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task<(ExtractedArticle?, string?)> LoadAndReadAsync(string url)
    {
        var page = await _context!.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout   = (float)_options.PageTimeout.TotalMilliseconds,
            });

            var status = response?.Status ?? 0;

            if (response is not null && !response.Ok) return (null, $"HTTP {status}");

            //Client-rendered newsrooms fill the body after DOMContentLoaded. Give the network a short
            //chance to settle; a page with a live ticker never does, so a timeout here is expected.
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 8_000 });
            }
            catch (TimeoutException)
            {
            }

            var html     = await page.ContentAsync();
            var finalUrl = page.Url;

            //Readability over the DOM the browser built - never a second download.
            var reader  = new SmartReader.Reader(finalUrl, html) { Logging = SmartReader.ReportLevel.Issue };
            var article = reader.GetArticle();

            return (new ExtractedArticle(
                FinalUrl:      finalUrl,
                Title:         article.Title         ?? "",
                TextContent:   Normalise(article.TextContent),
                Excerpt:       article.Excerpt       ?? "",
                Byline:        article.Byline        ?? "",
                Author:        article.Author        ?? "",
                SiteName:      article.SiteName      ?? "",
                Language:      article.Language      ?? "",
                FeaturedImage: article.FeaturedImage ?? "",
                PublishedAt:   article.PublicationDate is { } d ? new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)) : null,
                IsReadable:    article.IsReadable,
                HttpStatus:    status), null);
        }
        catch (PlaywrightException e)
        {
            return (null, FirstLine(e.Message));
        }
        catch (TimeoutException e)
        {
            return (null, FirstLine(e.Message));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    //The same proxy the HTTP client honours, so the browser reaches what the feed fetch reaches.
    //Loopback stays direct, which is what keeps a local fixture server reachable behind a proxy.
    private static Proxy? ProxyFromEnvironment()
    {
        var server = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");

        if (string.IsNullOrWhiteSpace(server)) return null;

        return new Proxy { Server = server, Bypass = "localhost,127.0.0.1,[::1]" };
    }

    //Readability's text keeps the page's indentation; collapse it to paragraphs.
    private static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim());

        return string.Join("\n\n", string.Join('\n', lines)
                                         .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                                         .Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.RemoveEmptyEntries)))
                                         .Where(p => p.Length > 0));
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
    }

    private sealed class HostGate
    {
        public SemaphoreSlim  Semaphore   { get; } = new(1);
        public DateTimeOffset NextAllowed { get; set; }
    }
}
