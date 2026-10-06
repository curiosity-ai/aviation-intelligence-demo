namespace AviationIntel.Connector.Extraction;

/// <summary>URL normalisation: the article key and the publisher key both come from here.</summary>
public static class Urls
{
    //Tracking parameters newsrooms and feed services append; two links differing only in these
    //are the same article.
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_name", "utm_id",
        "fbclid", "gclid", "mc_cid", "mc_eid", "cmpid", "ito", "ncid", "sr_share", "rss", "ref", "src",
    };

    //Public suffixes of more than one label that occur among aviation publishers. A full public
    //suffix list is overkill for a directory of a few hundred feeds; a missing entry only means a
    //publisher is keyed one label too short, which the directory review would catch.
    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.uk", "gov.uk", "org.uk", "ac.uk", "com.au", "gov.au", "org.au", "co.nz", "govt.nz", "co.jp", "or.jp",
        "com.br", "gov.br", "com.cn", "gov.cn", "co.in", "gov.in", "nic.in", "com.sg", "gov.sg", "co.za", "gov.za",
        "europa.eu", "gc.ca", "gouv.fr", "go.jp", "com.tr", "gov.tr", "com.mx", "gob.mx",
    };

    /// <summary>
    /// The article's identity: absolute, https where the source said http, no fragment, no tracking
    /// parameters, no trailing slash on the path. Deterministic, so the same item in two feeds - or in
    /// two runs - resolves to one node.
    /// </summary>
    public static string? Canonicalize(string? link, string? baseUrl = null)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;

        Uri? uri;

        if (!Uri.TryCreate(link.Trim(), UriKind.Absolute, out uri))
        {
            if (baseUrl is null || !Uri.TryCreate(new Uri(baseUrl), link.Trim(), out uri)) return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var query = string.Join("&", uri.Query.TrimStart('?')
                                        .Split('&', StringSplitOptions.RemoveEmptyEntries)
                                        .Where(p => !TrackingParameters.Contains(p.Split('=')[0])));

        var path = uri.AbsolutePath.Length > 1 ? uri.AbsolutePath.TrimEnd('/') : uri.AbsolutePath;

        var builder = new UriBuilder(uri)
        {
            Fragment = "",
            Query    = query,
            Path     = path,
            Host     = uri.Host.ToLowerInvariant(),
        };

        //Keep the scheme as published: a few government hosts still serve http only.
        if (builder.Uri.IsDefaultPort) builder.Port = -1;

        return builder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// The registrable domain of a URL ("www.easa.europa.eu" is "easa.europa.eu"), except on a hosted
    /// newsroom platform, where the tenant subdomain is the publisher ("boeing.mediaroom.com").
    /// </summary>
    public static string? RegistrableDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var labels = uri.Host.ToLowerInvariant().Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (labels.Length <= 2) return string.Join('.', labels);

        //Hosted newsroom platforms: the tenant is the publisher, not the platform.
        var lastTwo = string.Join('.', labels[^2..]);

        if (lastTwo is "mediaroom.com" or "gcs-web.com" or "prnewswire.com" or "businesswire.com" or "q4cdn.com" or "feedburner.com")
        {
            return labels.Length >= 3 ? labels[^3] + "." + lastTwo : lastTwo;
        }

        return MultiLabelSuffixes.Contains(lastTwo) ? string.Join('.', labels[^3..]) : lastTwo;
    }

    public static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
}
