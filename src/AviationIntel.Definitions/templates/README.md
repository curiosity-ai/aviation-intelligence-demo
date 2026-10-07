# /templates - the report templates

Two templates, one data file. You write the report **once**, as JSON, and render it as:

- `render-report report /work/<file>.json` - `report.html`, a self-contained HTML brief (KPI tiles,
  executive summary, charts, sections of item cards with signal chips, watchlist, coverage note).
  Light and dark theme, readable in the side pane, links on every source.
- `render-report deck /work/<file>.json` - `deck.html`, a page that builds a native PowerPoint deck
  with PptxGenJS and hands it over from a download button: title, summary + KPIs, native charts,
  one slide per section (first four items), watchlist table, sources appendix.

Both write to `/artifacts/`; then `publish <name>.html`. `sample-daily.json` (Tuesday 6 October 2026) and
`sample-weekly.json` (the week to that day) are complete examples, written from real articles in the
corpus: every figure in them is quoted from the article it cites.
Never edit the HTML templates for one report - everything that varies is in the JSON.

## The JSON contract

```jsonc
{
  "kind": "daily",                       // "daily" | "weekly"                          (required)
  "title": "Aviation Daily Brief",       //                                             (required)
  "fileName": "aviation-daily-2026-10-06", // optional; names the artifact and the .pptx
  "period": { "from": "2026-10-05", "to": "2026-10-06", "label": "Tuesday 6 October 2026" }, // (required)
  "generatedAt": "2026-10-06 18:00 UTC",
  "audience": "Market intelligence desk",
  "headline": "One sentence: the single most important thing in the period.",            // (required)
  "summary": ["3-5 bullets, each a complete, specific sentence with its number"],         // (required)
  "metrics": [ { "label": "Articles reviewed", "value": "128", "note": "last 24 h" } ],   // 3-4 tiles
  "charts": {
    "byCategory":  [ { "label": "Airlines and commercial aviation", "value": 12 } ],      // from `stats`
    "byPublisher": [ { "label": "AeroTime", "value": 9 } ],                               // daily: right-hand chart
    "byDay":       [ { "label": "2026-10-01", "value": 31 } ]                             // weekly: right-hand chart
  },
  "sections": [                                                                           // (required, >= 1)
    {
      "title": "Airlines",
      "takeaway": "One line: what this section adds up to.",
      "items": [
        {
          "headline": "Short noun-phrase headline, your words, not the source's",          // (required)
          "so_what": "Why it matters to the market - the implication, with the number.",   // (required)
          "detail": "Optional supporting context (HTML report only).",
          "signal": "positive",          // positive | negative | watch | neutral
          "sources": [                   // (required, >= 1, http(s) url each)
            { "title": "Article title", "publisher": "Publisher", "url": "https://...", "published": "2026-10-06",
              "uid": "article uid", "note": "summary only" }
          ]
        }
      ]
    }
  ],
  "watchlist": [ { "item": "What to follow", "why": "Why it matters", "next": "Date or event" } ],
  "coverage": { "articles": 128, "feeds": 64, "publishers": 51, "summaryOnly": 9,
                "notes": "What you could not read, and anything that limits the report." }
}
```

`render-report` refuses a file that misses a required field, an item without `so_what`, or an item
without a source URL, and says which.

## Where the numbers come from

- `stats --since 1d` (daily) or `stats --since 7d` (weekly): `articles`, `feeds_with_articles`,
  `publishers_with_articles`, `by_category`, `by_publisher`, `by_day`, `by_content`
  (`Feed summary only` is `coverage.summaryOnly`).
- Figures inside items come from the article text - quote them as the source states them.
