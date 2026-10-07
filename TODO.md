# TODO — Aviation Market Intelligence

Where the project stands, and what is open. Read `CLAUDE.md`, `README.md` and
`docs/running-locally.md` first.

## State at handoff (2026-10-07)

Built, committed and validated against a local Curiosity server (mosaik main, packages 26.10),
**with an unrestricted network** this time:

- [x] Feed directory: `data/feeds/aviation-aerospace-rss-directory.csv` (248 real feeds)
- [x] Connector: RSS/Atom → Chromium (Playwright) → Readability, plain-request fallback with an
      identifying User-Agent, follow.it unwrapping, hosts interleaved, written and committed 100
      articles at a time, transient page failures retried on the next run
- [x] **Full real runs.** 235 of 248 feeds OK; ~1,560 articles per run, ~32 minutes. Content split
      on the last run: Page 1,274 (82%) · Page (possibly paywalled) 103 · Feed full text 94 ·
      Feed summary only 84 (down from 253 before tuning)
- [x] **Entity recognition (non-negotiable 4).** Five vocabularies generated from Wikidata and
      OurAirports (`tools/build_reference_vocabularies.py`, `data/reference/`): 2,073
      organisations, 1,285 aircraft types, 594 engine types, 251 launch vehicles, 1,151 airports.
      Spotters, the `Aviation-Entities` pipeline and linking over `Article.Title` / `Article.Body`
      (`nlp/`, `config/pipeline-mapping.json`, `config/indexes/`). Mentions on article cards, in
      the sandbox (`news --mentions`, `article`, `stats` `top_mentions`) and in the report skill
- [x] Renderers for all 12 types; `#/` reflows at phone width; dark theme checked
- [x] Chat page: `AviationChatView` pins the assistant, tools and agent, parses replies against
      the entity pipeline. Contacts, Inbox, Calendar and Notes off (`config/interface.json`)
- [x] `sample-daily.json` and `sample-weekly.json` written from real articles; both pass
      `render-report`; the weekly deck builds an 11-slide `.pptx`; `tools/preview_report.py`
- [x] **Rebuild-from-scratch rehearsal**: fresh workspace → connector → `import_definitions.py`
      (`Success: True`, no warnings) → sandbox compiles → linking → front end → both reports
- [x] Definitions round-trip byte for byte with the workspace export

## Open tasks

### P0 — needs a real model

- [ ] **Real reports with a real provider.** No LLM key in either session. Configure a provider,
      press *Daily brief* and *Weekly report*, and compare with `templates/sample-*.json`
      (written by hand from the same corpus, so they are a fair yardstick): selection, "so what",
      citations, numbers from `stats`. Iterate on the skill, the agent prompt and the assistant prompt.
- [ ] **Search with a real model.** The sandbox's `search` goes through the LLM query interpreter;
      with the Lorem Ipsum provider it ignores the query. Check it once a provider is configured.

### P1 — product completeness

- [ ] **Mention precision, second pass.** The rules in `data/reference/README.md` removed the noise
      found so far (GEO, Middle East, CSA/CCA/ISR, Falcon, Pegasus, "air defense"…). Still seen:
      "Viper" (an engine) on "Viper Shield"; generic Wikidata "Manufacturer" labels such as
      "Space Systems". Look at `stats --since 7d` `top_mentions` and fix the *rule* that produced
      each bad one. Note: linking never removes an edge - tune on a scratch workspace, or rebuild
      the real one after the rules change (see the entity-linking skill §7).
- [ ] **Vocabulary gaps.** Lessors and MRO shops are only present where Wikidata classifies them
      under an aerospace industry; programmes without a Wikidata item (e.g. Anduril's Fury CCA,
      the D328eco) are not spotted. Consider a curated supplement - generated from a public list,
      not hand-typed.
- [ ] **Search configuration.** Tune search/indexes for `Article` (title/excerpt/body weights,
      embeddings for similarity), and add the entity types as search facets; export
      `config/search.json`.
- [ ] **Near-duplicate stories.** The same announcement from the newsroom and three trade titles is
      still merged only by the AI (the British Airways A380 retrofit came from four publishers on
      6 October). A similarity scenario for "same story", or a shared-entity + same-day heuristic.
- [ ] **Scheduling.** Run the connector hourly (cron/CI); with the hash cache an hourly run touches
      only new items. Consider a scheduled task that runs the analyst agent each morning.
- [ ] **Sandbox at real volume.** At ~1,500 articles `stats` and `news --mentions` answer in well
      under a second. Profile again at ~10k (a few months of ingest).

### P2 — polish and delivery

- [ ] Bot-protected feeds: the 10 Airbus feeds return an Imperva interstitial from datacenter IPs,
      even to a browser; Cargo Facts and Air Cargo Next refuse every client. Check from an office
      network; if still blocked, mark them in the directory or find alternate official feeds. No
      evasion.
- [ ] Extraction: a few templates still come through Readability as navigation (AviTrader's
      "Royal Air Maroc receives sixth Boeing 737-8" body is the site menu). Detect a body made of
      many short lines and fall back to the feed text.
- [ ] Article page: the mention tabs read "_Mentions _ → Organisation" (the raw edge name); give
      the edges display names in the schema styles, keep paragraph breaks in the body, and add a
      tab with the original page link and the other feeds that carried the story.
- [ ] Publisher names come from the first directory row's feed name, split on " — ". Review them
      (e.g. ESA's 33 country editions).
- [ ] `--install-browser` (Playwright's installer) not exercised: the cloud image ships Chromium,
      used via `CHROMIUM_PATH`.
- [ ] Decide whether to delete `sample-application/` (nothing depends on it).

## Re-creating the local test environment

`docs/running-locally.md` has the full loop. In short: build `Mosaik/Server` and this front end,
start the server with `MSK_WWW_FOLDER=<repo>/src/AviationIntel.FrontEnd/bin/Debug/netstandard2.0/tps`
and `--MSK_UNSAFE_AUTO_LOGIN=true` (not `--MSK_LOW_MEMORY`, which throttles linking), mint a library
token, run the connector with `CHROMIUM_PATH=/opt/pw-browsers/chromium-1194/chrome-linux/chrome`,
`python3 tools/import_definitions.py`, and add the Lorem Ipsum provider for `:replay` tests.
After any change made in the workspace UI, export the definitions and commit the files.
