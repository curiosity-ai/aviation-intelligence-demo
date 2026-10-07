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
    private readonly HttpClient                               _http;

    private ArticleExtractor(ConnectorOptions options)
    {
        _options     = options;
        _concurrency = new SemaphoreSlim(Math.Max(1, options.BrowserConcurrency));
        _http        = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All, AllowAutoRedirect = false }) { Timeout = options.PageTimeout };

        //The fallback says who it is. Several publishers refuse a headless browser yet serve a plain
        //request, and some ask crawlers to identify themselves - so it does, rather than pass as Chrome.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ConnectorOptions.IdentifyingUserAgent);
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
                var (article, error) = await LoadAndReadAsync(url, unwrapRedirect: true);

                if (article is not null) return (article, null);

                //Refused or dropped by the browser: one plain request before giving up on the page.
                var (fallback, fallbackError) = await ReadWithHttpAsync(url, unwrapRedirect: true);

                return fallback is not null ? (fallback, null) : (null, $"{error}; plain request: {fallbackError}");
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

    /// <summary>
    /// Whether a failed page is worth trying again on the next run: the connection dropped or the page
    /// timed out, as opposed to a publisher that answered with a refusal.
    /// </summary>
    public static bool IsTransient(string? error)
        => error is not null
        && (error.Contains("net::ERR_", StringComparison.Ordinal)
         || error.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
         || error.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        && !error.Contains("plain request: HTTP 4", StringComparison.Ordinal);

    private async Task<(ExtractedArticle?, string?)> ReadWithHttpAsync(string url, bool unwrapRedirect)
    {
        try
        {
            //Redirects are followed by hand: HttpClient will not follow https → http, and a few
            //WordPress hosts send every request on a round trip through http to add a trailing slash.
            var finalUrl = url;
            var response = await _http.GetAsync(finalUrl);

            for (var hop = 0; hop < 6 && (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location; hop++)
            {
                finalUrl = new Uri(new Uri(finalUrl), location).AbsoluteUri;
                response.Dispose();
                response = await _http.GetAsync(finalUrl);
            }

            using var _ = response;

            if (!response.IsSuccessStatusCode) return (null, $"HTTP {(int)response.StatusCode}");

            if (unwrapRedirect && Urls.TryUnwrapRedirectNotice(finalUrl, out var target)) return await ReadWithHttpAsync(target, unwrapRedirect: false);

            return (Read(finalUrl, await response.Content.ReadAsStringAsync(), (int)response.StatusCode), null);
        }
        catch (HttpRequestException e)
        {
            return (null, FirstLine(e.Message));
        }
        catch (TaskCanceledException)
        {
            return (null, "timed out");
        }
    }

    private async Task<(ExtractedArticle?, string?)> LoadAndReadAsync(string url, bool unwrapRedirect)
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

            //A feed service's click tracker ends on a "Redirect Notice" naming the article: read that.
            if (unwrapRedirect && Urls.TryUnwrapRedirectNotice(finalUrl, out var target))
            {
                await page.CloseAsync();
                return await LoadAndReadAsync(target, unwrapRedirect: false);
            }

            //Readability over the DOM the browser built - never a second download.
            return (Read(finalUrl, html, status), null);
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
            if (!page.IsClosed) await page.CloseAsync();
        }
    }

    private static ExtractedArticle Read(string finalUrl, string html, int status)
    {
        var reader  = new SmartReader.Reader(finalUrl, html) { Logging = SmartReader.ReportLevel.Issue };
        var article = reader.GetArticle();

        return new ExtractedArticle(
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
                HttpStatus:    status);
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

        var lines      = text.Replace("\r", "").Split('\n').Select(l => l.Trim());
        var paragraphs = string.Join('\n', lines)
                               .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                               .Select(p => string.Join(' ', p.Split('\n', StringSplitOptions.RemoveEmptyEntries)))
                               .Where(p => p.Length > 0)
                               .ToList();

        //What Readability keeps above the first sentence is page furniture: "Skip to content",
        //"Anzeige", a dateline, an image credit. A story never opens with a paragraph that short.
        for (var i = 0; i < 3 && paragraphs.Count > 1 && HtmlText.WordCount(paragraphs[0]) < LEADING_FURNITURE_WORDS; i++) paragraphs.RemoveAt(0);

        return string.Join("\n\n", paragraphs);
    }

    private const int LEADING_FURNITURE_WORDS = 6;

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    public async ValueTask DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _http.Dispose();
    }

    private sealed class HostGate
    {
        public SemaphoreSlim  Semaphore   { get; } = new(1);
        public DateTimeOffset NextAllowed { get; set; }
    }
}
