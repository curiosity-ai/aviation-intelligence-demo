# Deployment notes

Workspace `aviation-marketing-intelligence` on demos.curiosity.ai (`/aviation-marketing-intelligence`).

## 2026-11-20 refresh

- Image: `update-image` run on 2026-11-20 (curiosityai/curiosity:latest), workspace ready.
- Packages: Curiosity.FrontEnd* 26.11.8270, Tesserae 2026.11.73805, Curiosity.Library 26.11.8230, Transpose.BCL 26.11.7972,
  Microsoft.Playwright 1.65.1 (CsvHelper stays 33.1.0). Connector and front end build clean.
- Definitions re-imported (`tools/import_definitions.py`): `Success: True`, `GET api/build/compile-status` is `{"FailingIds":[],"Pending":false}`,
  every sandbox `DefinitionError: null`, `aviation/dashboard` answers 200 (792 articles in 7 days, 0 in the last 24 h at the time: the corpus was not re-ingested).
- `config/interface.json`: the entry `EnableClipboard` is gone (the product removed the setting; the import failed on it).
- Front end: Debug build uploaded with `curiosity-cli upload-front-end`.

## Concurrent editing: desk notes

A **desk note** is the analyst's editorial commentary on a daily brief or a weekly report (the "so what"), written as a shared markdown
document that people and the assistant edit at the same time. It is an addition to the report, never a second source of truth: the report
stays the JSON that `render-report` turns into the HTML page and the PowerPoint deck, and the note is a separate `_EditingDocument`.

- Back end: the news desk sandbox (`AVsandboX1111111111111`) declares `Editing => SandboxEditingAccess.Everything()`, which adds the `doc` command
  (list, cat, create, append, replace, history, diff). The assistant prompt has a "Shared documents: the desk note" section: work in the document the
  page names, prefer append/replace, every point cites the article it comes from, never invent an article, a figure or a source.
- Front end: `#/desknotes` (sidebar entry "Desk notes", `Dashboard/DeskNotesView.cs`) lists the user's notes and opens one in a `CollaborativeEditor`
  with the assistant docked beside it (`Components/EmbeddedAssistant.cs`, its context names the document uid). The home page's "Desk note" button and
  the list's two buttons find or create `Desk note <yyyy-MM-dd>` / `Desk note, week to <yyyy-MM-dd>` in the user's "Desk notes" category, seeded with a heading.
- Access: a document belongs to its creator and is shared by adding owners (Documents page); there is no group model in this demo. A read-only
  administrator can open the list and existing notes but not create or edit them.
- Verified live on 2026-11-20 as the workspace admin (Playwright, two browser contexts, real model): created the note through the UI, asked the docked
  assistant for a three-point "so what" of the week with links; while it wrote (it read 11 articles, then appended) the analyst typed a line at the top of the
  document. `GET api/editing/documents/text` held BOTH the analyst's line and the assistant's cited text; a second session opening `?doc=` showed the same
  text. Test document and category deleted afterwards. NOT verified: as `claude-ro` (that user does not exist in this workspace and was not created here),
  `tools/readonly-admin-check.py`, the weekly button, a phone width.

## Why the dashboard can show 0 articles in the last 24 hours (checked 2026-11-20)

Nothing is broken. The corpus is kept current by the in-workspace connector task **Aviation RSS feeds** (`AVconnRSS1111111111111`, hourly at :17), and a scheduled task only runs **while the workspace is awake**. Workspaces on `demos.curiosity.ai` go to sleep after an hour without use, so after a few idle days the newest article is days old and "Articles 24 h" is 0 (the 7-day tile still counts what was ingested earlier). The dashboard counts articles by node timestamp and is correct.

Checked on the updated image: the task is enabled and not failing (`LastStartTime` 02:17 UTC, 97 s, no failure); a manual `POST api/scheduling/run?uid=AVconnRSS1111111111111` finished in about 40 s and added 7 articles; the dashboard then answered `Articles24h` 306 and `Articles7d` 1,470, with the newest article 03:28 UTC (the feeds' own latest).

To have fresh news at a demo: open the workspace a few minutes before and run the task once from Manage > Scheduled tasks (or the call above), or keep the workspace warm (`POST /api/admin/instances/aviation-marketing-intelligence/keep-warm`, `{"AlwaysOnline":true}`, which costs a running container).

Feed health on the same day: of 295 feeds, 222 `OK`, 54 `Unreachable` (publishers that refuse a datacentre address or a library user agent), 17 `Invalid feed`, 2 `Empty`. The dashboard's "feeds failing" tile (71) is these.
