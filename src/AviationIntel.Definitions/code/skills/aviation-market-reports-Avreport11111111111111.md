[skill: Curiosity.Skills.UID("Avreport11111111111111")]
[skill: Curiosity.Skills.Name("aviation-market-reports")]
[skill: Curiosity.Skills.Description("How to write the daily aviation market brief and the weekly market report from the monitored news, and publish each as an HTML page and a PowerPoint deck with the Aviation News Desk templates.")]
[skill: Curiosity.Skills.Category("General")]
[skill: Curiosity.Skills.Icon("fi-rr-plane-alt")]

# Aviation market reports: the daily brief and the weekly report

You write market intelligence for people who follow airlines, manufacturers, lessors, MRO
providers, airports and regulators for a living. They do not want the news - they have
the news. They want to know **what changed in the market and what it means**, with a
link to the evidence for every claim.

Everything you need is in the **Aviation News Desk** sandbox (`news_desk`): the articles,
the feed directory, the numbers and the two templates. Read `/templates/README.md` once
before your first report; it is the JSON contract the templates render.

## The two products

| | Daily brief | Weekly report |
|---|---|---|
| Window | last 24 h (`--since 1d`), or the calendar day asked for | last 7 days (`--since 7d`), Monday-Sunday if asked "last week" |
| Headline | the one development that moves the market today | the theme of the week |
| Summary | 3-4 bullets | 5 bullets |
| Sections | 3-5, only those with something material | 5-7, every major segment |
| Items per section | 1-3 | 2-5 |
| Charts | by category + most active publishers | by category + articles per day |
| Watchlist | 2-4 events with a date | 4-6, the week ahead |
| `kind` | `"daily"` | `"weekly"` |

Unless asked for one format, publish **both** the HTML report and the deck: they come from
the same JSON, so the second costs one command.

## The workflow

```bash
stats --since 1d                                   # 1. size the period: counts, categories, feed health
news --since 1d --priority Core | cut -f1-5,9      # 2. scan the Core feeds first
news --since 1d --priority Secondary | cut -f1-5,9 #    then the rest
news --since 1d --grep "order" --json | jq -r '.uid + "  " + .title'
article <uid> <uid> ...                            # 3. read every article you will cite
cat > /work/daily-2026-10-06.json <<'EOF'          # 4. write the report JSON
{ ... }
EOF
render-report report /work/daily-2026-10-06.json  # 5. render both
render-report deck   /work/daily-2026-10-06.json
publish daily-2026-10-06.html                      # 6. publish both
publish daily-2026-10-06-deck.html
```

**Delegating to the agent.** The Aviation Market Intelligence Analyst agent runs the same
workflow, but a delegated run can only write in its own directory and cannot publish: it renders
into `/agents/aviation-market-intelligence-analyst/<run>/` and returns the paths. Copy them and
publish:

```bash
cp /agents/aviation-market-intelligence-analyst/<run>/*.html /artifacts/
publish aviation-daily-2026-10-06.html && publish aviation-daily-2026-10-06-deck.html
```

`render-report` checks the JSON and names every problem; fix the JSON and run it again - never
edit the rendered HTML. Set `"fileName"` (e.g. `aviation-daily-2026-10-06`) so the artifact and
the downloaded `.pptx` carry a meaningful name.

## Choosing what goes in

Read the window, then **select**. A report that lists every article is a feed reader.

1. **Material first.** An item earns its place if it changes supply, demand, cost, capacity, risk
   or competitive position for someone in the market: orders and cancellations, delivery and
   production rates, engine and supply-chain constraints, airworthiness directives and
   groundings, route and network moves, capacity and fares, earnings and guidance, lessor
   activity, airport capacity and charges, ATC disruption, regulation and SAF policy, defence
   programme awards.
2. **Priority is a tie-breaker, not a filter.** A `Core` feed is the directory's judgement that the
   source matters; prefer it when two sources say the same thing, but a material story from a
   `Secondary` feed still goes in.
3. **One story, one item.** The same press release appears in the newsroom feed and in trade
   titles. Merge them into one item and cite the original announcement first, then the analysis
   that adds something.
4. **Connect items.** The value of a report is the link nobody else drew: an airworthiness
   directive on an engine type *and* an order for aircraft with that engine *and* shop-visit
   backlogs belong in the same thought. Say it in the `so_what`, the summary and the headline.
5. **Leave out**: consumer travel tips, aircraft-spotting, trip reports, PR fluff with no number,
   research preprints unless they bear on an industry decision, and anything you could not read
   beyond its title.

## Sections

Map the directory's categories to these sections and keep this order. Drop a section with
nothing material in the window; never pad one.

| Section | Directory categories |
|---|---|
| Safety & regulation | Regulators, public agencies and R&D (regulatory items only) |
| Airlines | Airline company announcements; Airlines and commercial aviation; Regional aviation; Cargo and logistics |
| Manufacturers & supply chain | Manufacturers and company announcements; Aviation industry and OEM analysis; MRO and aviation supply chain |
| Airports & air traffic | Airports and air traffic management |
| Finance & leasing | Company financial disclosures; leasing stories from any category |
| Business aviation & AAM | Business aviation and rotorcraft; Drones and advanced air mobility |
| Defence & space | Defense aviation and aerospace; Space and satellite industry |
| Sustainability | Sustainability and aviation energy; SAF and emissions policy from any category |

## Writing each item

- **headline** - your words, a noun phrase with the number: "Northwind firms 24 M-220s, options
  for 12 more". Not the source's headline, not a sentence.
- **so_what** - the implication for the market, in one or two sentences, carrying the figure
  that matters. "Replaces 31 regional jets from Q2 2028; capex guidance unchanged." If you cannot
  say why it matters, it does not belong in the report.
- **detail** - optional context: the caveat, the second figure, the link to another item.
- **signal** - from the point of view of the market's health and the reader's exposure:
  `positive` (capacity, demand, rate increases, orders), `negative` (groundings, delays,
  shortages, losses), `watch` (unresolved, could go either way, needs follow-up), `neutral`
  (context). Be consistent across items.
- **sources** - every article you relied on: `title`, `publisher`, `url`, `published`
  (YYYY-MM-DD) and `uid`. For an article whose `content` is `Feed summary only` add
  `"note": "summary only"` and claim nothing the summary does not say.

## Numbers

- `metrics` and `charts` come from `stats` for the same window - never typed in from memory.
  `coverage.articles`, `.feeds`, `.publishers` and `.summaryOnly` are `stats`' `articles`,
  `feeds_with_articles`, `publishers_with_articles` and the `Feed summary only` count.
- Figures inside items are the source's figures, quoted as the source states them, with the
  unit. Do not convert currencies or restate a "nearly a third" as "33%".
- If a feed that usually reports is missing (`stats` lists `silent` Core feeds), or feeds failed
  (`failing`), say so in `coverage.notes` - the reader must know what the report could not see.

## Language

The directory includes German, French, Dutch and other national-language feeds (`lang`). Read
them, and write the report in English unless asked otherwise; translate the facts, keep proper
names, and cite the original article.

## Before you publish

- The headline and the first summary bullet say the same thing, and it is the most material
  development of the window.
- Every item has a `so_what` and at least one source with a working URL.
- No claim rests on an article you have not read with `article` (or, for `Feed summary only`,
  on more than its summary).
- Dates are absolute (`2026-10-06`, "Tuesday 6 October 2026"), never "yesterday".
- In your reply: two sentences on what the report concludes, and that the HTML page and the
  deck are attached. Do not repeat the report in the chat.
