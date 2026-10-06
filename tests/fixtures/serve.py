"""Offline fixture server for the RSS connector.

Serves synthetic feeds and article pages on localhost so the connector's whole path - feed
fetch, Chromium page load, Readability extraction, dedupe, caching - can be exercised where
the real publishers are unreachable (CI, a sandboxed container).

EVERYTHING HERE IS SYNTHETIC. The airlines, manufacturers, airports and figures are invented;
every page and feed item says so. Dates are relative ("days_ago") and rendered at request time,
so the fixtures stay inside the connector's --since-days window whenever they are served.

    python3 tests/fixtures/serve.py --port 8099
    dotnet run --project src/AviationIntel.Connector -- --root . \
        --directory tests/fixtures/directory.csv --endpoint http://localhost:8080 --token <token>
"""
import argparse
import datetime as dt
import html
import json
import os
from email.utils import format_datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))
MARKER = "SYNTHETIC FIXTURE - invented for offline testing; not real news."


def load():
    with open(os.path.join(HERE, "articles.json"), encoding="utf-8") as f:
        return json.load(f)


def when(days_ago, hour):
    now = dt.datetime.now(dt.timezone.utc).replace(minute=0, second=0, microsecond=0)
    return (now - dt.timedelta(days=days_ago)).replace(hour=hour)


def article_url(base, a):
    return f"{base}/articles/{a['slug']}"


def rss(base, feed, articles):
    items = []
    for a in articles:
        cats = "".join(f"<category>{html.escape(t)}</category>" for t in a.get("tags", []))
        content = ""
        if a.get("feed_content") == "full":
            content = "<content:encoded><![CDATA[" + "".join(f"<p>{p}</p>" for p in a["body"]) + "]]></content:encoded>"
        items.append(
            f"<item><title>{html.escape(a['title'])}</title><link>{article_url(base, a)}?utm_source=rss&amp;utm_medium=feed</link>"
            f"<guid isPermaLink=\"false\">{a['slug']}</guid><pubDate>{format_datetime(when(a['days_ago'], a['hour']))}</pubDate>"
            f"<dc:creator>{html.escape(a.get('author', ''))}</dc:creator>{cats}"
            f"<description><![CDATA[<p>{a['summary']}</p><p>The post {html.escape(a['title'])} appeared first on {html.escape(feed['name'])}.</p>]]></description>"
            f"{content}</item>")
    return ("<?xml version=\"1.0\" encoding=\"UTF-8\"?><rss version=\"2.0\" xmlns:content=\"http://purl.org/rss/1.0/modules/content/\" "
            "xmlns:dc=\"http://purl.org/dc/elements/1.1/\"><channel>"
            f"<title>{html.escape(feed['name'])}</title><link>{base}/</link><description>{MARKER}</description>"
            + "".join(items) + "</channel></rss>")


def atom(base, feed, articles):
    entries = []
    for a in articles:
        entries.append(
            f"<entry><title>{html.escape(a['title'])}</title><link href=\"{article_url(base, a)}\"/><id>{article_url(base, a)}</id>"
            f"<updated>{when(a['days_ago'], a['hour']).isoformat()}</updated><author><name>{html.escape(a.get('author', ''))}</name></author>"
            f"<summary type=\"html\">{html.escape(a['summary'])}</summary></entry>")
    return ("<?xml version=\"1.0\" encoding=\"utf-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\">"
            f"<title>{html.escape(feed['name'])}</title><id>{base}/feeds/{feed['id']}</id><updated>{when(0, 0).isoformat()}</updated>"
            + "".join(entries) + "</feed>")


def page(base, a):
    body = "".join(f"<p>{p}</p>" for p in a["body"])
    published = when(a["days_ago"], a["hour"]).isoformat()
    nav = "<nav><a href='/'>Home</a> | <a href='/news'>News</a> | <a href='/about'>About</a> | <a href='/contact'>Contact</a></nav>"
    footer = "<footer><p>Cookie settings</p><p>&copy; Fixture publisher. " + MARKER + "</p></footer>"
    head = (f"<meta charset='utf-8'><title>{html.escape(a['title'])}</title>"
            f"<meta property='og:title' content='{html.escape(a['title'])}'><meta name='author' content='{html.escape(a.get('author', ''))}'>"
            f"<meta property='article:published_time' content='{published}'><meta property='og:site_name' content='{html.escape(a['site'])}'>")
    lang = a.get("lang", "en")
    marker = f"<p><em>{MARKER}</em></p>"
    if a.get("page") == "js":
        # The body arrives only after scripts run - a plain GET sees an empty shell.
        payload = json.dumps(body + marker)
        return (f"<!doctype html><html lang='{lang}'><head>{head}</head><body>{nav}<main><article><h1>{html.escape(a['title'])}</h1>"
                f"<div id='story'>Loading…</div></article></main>{footer}"
                f"<script>setTimeout(function(){{document.getElementById('story').innerHTML={payload};}}, 300);</script></body></html>")
    if a.get("page") == "paywall":
        teaser = f"<p>{a['body'][0]}</p>"
        return (f"<!doctype html><html lang='{lang}'><head>{head}</head><body>{nav}<main><article><h1>{html.escape(a['title'])}</h1>"
                f"{teaser}<div class='paywall'><h2>Subscribe to keep reading</h2><p>Subscribers get full access to analysis and data.</p></div>{marker}"
                f"</article></main>{footer}</body></html>")
    return (f"<!doctype html><html lang='{lang}'><head>{head}</head><body>{nav}<main><article><h1>{html.escape(a['title'])}</h1>"
            f"<p class='byline'>By {html.escape(a.get('author', ''))}</p>{body}{marker}</article>"
            f"<aside><h3>Most read</h3><ul><li><a href='/x'>Unrelated headline one</a></li><li><a href='/y'>Unrelated headline two</a></li></ul></aside>"
            f"</main>{footer}</body></html>")


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def send(self, status, body, content_type):
        data = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        data = load()
        base = f"http://{self.headers.get('Host')}"
        path = self.path.split("?")[0]
        if path.startswith("/feeds/"):
            fid = path[len("/feeds/"):].rsplit(".", 1)[0]
            feed = next((f for f in data["feeds"] if f["id"] == fid), None)
            if feed is None:
                return self.send(404, "not found", "text/plain")
            arts = [a for a in data["articles"] if fid in a["feeds"]]
            if feed.get("format") == "Atom":
                return self.send(200, atom(base, feed, arts), "application/atom+xml; charset=utf-8")
            return self.send(200, rss(base, feed, arts), "application/rss+xml; charset=utf-8")
        if path.startswith("/articles/"):
            slug = path[len("/articles/"):]
            a = next((a for a in data["articles"] if a["slug"] == slug), None)
            if a is None or a.get("page") == "missing":
                return self.send(404, "<html><body><h1>Page not found</h1></body></html>", "text/html")
            return self.send(200, page(base, a), "text/html; charset=utf-8")
        return self.send(404, "not found", "text/plain")


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=8099)
    args = p.parse_args()
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()
