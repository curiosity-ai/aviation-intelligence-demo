# Aviation Market Intelligence - design brief

## The decision this supports

An aviation market intelligence analyst has to tell their stakeholders - a strategy team, a
lessor's asset managers, a supplier's sales leadership - **what changed in commercial aviation
and aerospace, and what it means for them**, every morning and every week. Today that means
skimming dozens of newsrooms, trade titles and regulator pages, deduplicating syndicated press
releases, and hand-writing a brief and a slide deck.

The application does the reading and the first draft:

- it monitors a curated directory of **248 real RSS feeds** - airline and manufacturer newsrooms,
  trade and specialist titles, regulators and agencies, airports, MRO, leasing, defence, space;
- it reads each new article **in full** where the publisher allows it (a real browser plus
  Readability), and records where it could only read the feed's summary;
- an assistant and an agent turn a day or a week of it into a **daily brief** or a **weekly
  report**, each published as an **HTML page** and a **PowerPoint deck**, every claim linked to
  its article.

**Deviation from the template flow:** the corpus is not synthetic. The feeds are real and the
connector fetches live news. Only the offline test fixtures (`tests/fixtures/`) are synthetic,
and they say so on every page.

## Data

| Source | Shape | Volume | Identity |
| --- | --- | --- | --- |
| Feed directory (`data/feeds/aviation-aerospace-rss-directory.csv`) | CSV, 22 columns, curated by an analyst | 248 feeds, 15 categories | Feed URL |
| Feed items | RSS 2.0 / Atom | ~10-100 per feed, refreshed continuously | Canonical article URL |
| Article pages | HTML, often client-rendered, sometimes paywalled or bot-protected | one per item | Canonical article URL |

The directory is the curated part: what each feed is *for* (`Market intelligence use`), how much
to trust it (`Priority`: Core / Secondary / Low frequency), whether its articles can be read
(`Article access note`), its language, and an overlap note (publisher-wide vs topic feeds,
syndicated releases).

## Graph model

```
 Priority ◄─FeedHasPriority── Feed ──FeedInCategory──► FeedCategory ◄──ArticleInCategory── Article
 SourceType ◄─FeedOfSourceType─┘ │                                                          │  │
 Publisher ◄──PublishedBy────────┘ ◄──────────────────ArticleFromPublisher──────────────────┘  │
                                 Feed ◄──AppearedIn── Article ──TaggedWith──► Tag ◄───────────────┘
```

| Node | Key | Why a node |
| --- | --- | --- |
| `Article` | canonical URL | the unit of news; text fields `Title`, `Excerpt`, `Body` are what search and the AI read |
| `Feed` | feed URL | carries the directory's judgement and the last fetch's health |
| `Publisher` | registrable domain of the evidence page | several feeds share one (Lufthansa Group: German + English; ESA: 33 country editions) |
| `FeedCategory`, `SourceType`, `Priority` | name | shared vocabularies - facets across feeds and articles |
| `Tag` | lower-cased publisher tag | the publisher's own `<category>` labels |

**Deliberately not written by the connector:** which companies, aircraft programmes, engines,
airports or regulators an article is about. That is what the application *discovers* from the
text (entity recognition, search, the AI reading the article) - writing it from the feed would
make the demo prove nothing.

An article seen in several feeds is **one** node with an `AppearedIn` edge per feed; the reports
file it under its highest-priority feed, preferring the publisher's own newsroom over a trade
title that re-posted it.

## Retrieval and AI

- **Sandbox** (`Aviation News Desk`): the corpus as files (`/news/days/<day>/<uid>.md`, a 7-day
  `index.tsv`, `/feeds/directory.tsv`) plus `news`, `article`, `stats`, `search`, `consult` and the
  graph `query` engine - so an agent answers questions nobody wrote a tool for.
- **Templates**: one report JSON renders as an HTML brief and as a PowerPoint deck; the
  `render-report` command validates it (every item needs a "so what" and a source URL).
- **Skill** (`aviation-market-reports`): the house editorial method.
- **Agent** (`Aviation Market Intelligence Analyst`): produces a report end to end.
- **Assistant** (`Aviation Market Intelligence`): answers questions, writes reports, delegates.

## Success criteria

1. The connector ingests the directory and every reachable feed, reads articles in full where
   possible, and an unchanged article costs nothing on the next run.
2. Every number on the home page is computed live and opens the articles it counted.
3. A daily brief and a weekly report can be produced and published in both formats from the
   chat or from one button, with every claim linked to an article the agent read.
4. The workspace can be rebuilt from this repository alone.
