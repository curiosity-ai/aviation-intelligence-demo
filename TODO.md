# TODO — Aviation Market Intelligence

Where the project stands, and what is open. Read `CLAUDE.md`, `README.md` and
`docs/running-locally.md` first.

## State at handoff (2026-10-06)

Built, committed and validated against a local Curiosity server (mosaik main, packages 26.10):

- [x] Feed directory: `data/feeds/aviation-aerospace-rss-directory.csv` (248 real feeds)
- [x] Connector: RSS/Atom → Chromium (Playwright) → Readability (SmartReader), hash cache, browser fallback for bot checks
- [x] Offline fixtures: `tests/fixtures/` (synthetic; JS page, paywall, 404, Atom, German, syndicated item)
- [x] Sandbox tool "Aviation News Desk" (`AVsandboX1111111111111`)
- [x] Report templates (HTML + PowerPoint from one JSON) + `render-report` validation
- [x] Skill `aviation-market-reports`, agent "Aviation Market Intelligence Analyst", assistant "Aviation Market Intelligence"
- [x] Endpoint `aviation/dashboard`, node schemas + styles exported
- [x] Front end: home, `#/news?set=…`, `#/feeds`, renderers for all 7 types

**The previous session's sandbox blocked almost every feed host**, and had no LLM key. So only
the fixtures and two arXiv feeds were ingested, and every AI turn was a scripted `:replay` with
the Lorem Ipsum provider.

## Open tasks

### P0 — needs an unrestricted network / a real model

- [ ] **Full real run.** `dotnet run --project src/AviationIntel.Connector -- --root . --dry-run --no-browser`,
      then with the browser, then a real run into a workspace. Review the OUTCOMES counters: feeds
      OK / Unreachable / Invalid, and the content split (Page / Feed full text / Feed summary only).
- [ ] **Tune extraction on real pages.** Spot-check ~20 articles across source types (newsroom,
      trade title, regulator, `Subscription likely` feeds). Adjust `MIN_READABLE_WORDS` (80),
      `MIN_FEED_BODY_WORDS` (80), the "possibly paywalled" rule (`ArticlesIngest.ChooseBody`), and
      per-host politeness (`PerHostDelay`, `--browser-concurrency`). Bump `ArticlesIngest.Version` after.
- [ ] **Bot-protected feeds.** 10 Airbus feeds answer with an Imperva challenge even in headless
      Chromium. Check whether they work from a normal network. If not, mark them in the directory
      or find alternate official feeds. Do **not** build evasion.
- [ ] **`--install-browser`** (Playwright's installer) has not been run — verify it.
- [ ] **Real reports with a real provider.** Configure an LLM provider, press *Daily brief* and
      *Weekly report*, and review the output against the skill: selection, "so what", citations,
      numbers from `stats`. Iterate on `code/skills/aviation-market-reports-*.md`, the agent prompt
      and the assistant prompt.
- [ ] **Weekly templates.** Only `sample-daily.json` was previewed. Add `templates/sample-weekly.json`
      (byDay chart, 5–7 sections) and preview both templates
      (see the preview script pattern in `docs/running-locally.md`). Then `python3 tools/embed_templates.py`.

### P1 — product completeness

- [ ] **Entity recognition (non-negotiable 4).** Companies, aircraft programmes, engines, airports
      and regulators mentioned in articles are not linked yet. Add spotters / NLP linking
      (`curiosity-entity-linking-nlp`) over `Article.Title`, `Excerpt`, `Body`. Seed vocabularies
      from reference lists (script-generated, not hand-authored). Then expose `_Mentions` in the
      renderers and the sandbox (e.g. `news --mentions <entity>`).
- [ ] **Search configuration.** Tune search/indexes for `Article` (title/excerpt/body weights,
      embeddings for similarity) and export `config/search.json` and `config/indexes`.
- [ ] **Near-duplicate stories.** The same announcement under different URLs (newsroom vs trade
      rewrite) is merged only by the AI today. Consider a similarity scenario
      (`curiosity-similarity-scenarios`) for a "same story" cluster.
- [ ] **Scheduling.** Run the connector on a schedule (cron/CI, hourly). Consider a workspace
      scheduled task that runs the analyst agent each morning and stores or sends the daily brief.
- [ ] **Chat page.** `AppSettings.AssistantView` is not set. Add a pinned chat view on the Aviation
      assistant with welcome prompts (`curiosity-chat-experience`). Hide unused sidebar entries
      (Mail, Contacts, Notes…) via the interface settings, and export them.
- [ ] **Sandbox at real volume.** `FeedLookup.Build` queries every feed per call. `news`/`stats`
      traverse per article. Profile with ~10k articles and cache per script if slow. Check the
      20,000-file and output limits on `/news/days`.

### P2 — polish and delivery

- [ ] Front end: check dark theme and a narrow viewport. Add an article-page tab with the
      original page link and the other feeds that carried it.
- [ ] Publisher names come from the first directory row's feed name, split on " — ". Review them
      for the real directory (e.g. ESA's 33 country editions).
- [ ] `sample-daily.json` cites fixture URLs (`127.0.0.1:8099`). Replace it with a sample built
      from real articles once they are ingested.
- [ ] Rebuild-from-scratch rehearsal (`curiosity-delivery`): fresh workspace → connector →
      `tools/import_definitions.py` → front end → reports.
- [ ] Decide whether to delete `sample-application/` (nothing depends on it).

## Re-creating the local test environment

`docs/running-locally.md` has the full loop. In short: build `Mosaik/Server` and this front end,
start the server with `MSK_WWW_FOLDER=<repo>/src/AviationIntel.FrontEnd/bin/Debug/netstandard2.0/tps`
and `--MSK_UNSAFE_AUTO_LOGIN=true`, mint a library token, run the connector,
`python3 tools/import_definitions.py`, and add the Lorem Ipsum provider for `:replay` tests.
After any change made in the workspace UI, export the definitions and commit the files.
