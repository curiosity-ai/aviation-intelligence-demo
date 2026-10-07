# Running the demo locally against a live server

The loop used to validate this repository end to end: a Curiosity server built from source, the
connector ingesting into it, the definitions imported, this front end served, and the sandbox and
agent exercised through real chat turns - with no LLM key.

## 1. Build and start Curiosity

From a checkout of the product (`curiosity-ai/mosaik`), with the .NET 10 SDK and the Transpose
compiler (`dotnet tool install --global Transpose.Compiler`):

```bash
dotnet build Mosaik/Server/Mosaik.Server.csproj -c Debug -r linux-x64

# This repository's front end - the server serves whatever folder MSK_WWW_FOLDER names.
dotnet build <this repo>/src/AviationIntel.FrontEnd/AviationIntel.FrontEnd.csproj

cd Mosaik/Server/bin/Debug/net10.0/linux-x64
setsid env MSK_GRAPH_STORAGE=/tmp/curiosity/aviation \
    MSK_ADMIN_USER=admin MSK_ADMIN_PASSWORD=admin MSK_ADMIN_EMAIL=admin@test.local \
    MSK_DISABLE_TELEMETRY=true MSK_DISABLE_CLOUD_ERROR_REPORT=true \
    MSK_WWW_FOLDER=<this repo>/src/AviationIntel.FrontEnd/bin/Debug/netstandard2.0/tps \
    ./curiosity --MSK_DISABLE_RESOURCE_MONITORING=true --MSK_UNSAFE_AUTO_LOGIN=true \
    > /tmp/server.log 2>&1 < /dev/null &

until [ "$(curl -s http://localhost:8080/api/graph/ready)" = "true" ]; do sleep 3; done
```

`MSK_UNSAFE_AUTO_LOGIN` signs the browser in as `admin` - local use only. Add `--MSK_LOW_MEMORY=true`
only on a machine short of memory: it slows entity linking down badly (see below). A front-end-only change
needs a rebuild and a browser reload, not a server restart.

## 2. Ingest

```bash
B=http://localhost:8080/api
TOK=$(curl -s -X POST $B/login/create -H 'Content-Type: application/json' -d '{"UserName":"admin","Password":"admin"}' | tr -d '"')
LIBTOK=$(curl -s "$B/library/token?purpose=aviation-rss&duration=30" -H "Authorization: Bearer $TOK")

# The real feeds
dotnet run --project src/AviationIntel.Connector -- --root . --endpoint http://localhost:8080 --token "$LIBTOK"

# Or, where the publishers are unreachable (CI, a sandboxed container), the synthetic fixtures:
python3 tests/fixtures/serve.py --port 8099 &
dotnet run --project src/AviationIntel.Connector -- --root . --directory tests/fixtures/directory.csv \
    --endpoint http://localhost:8080 --token "$LIBTOK"
```

In a Claude Code cloud container Chromium is pre-installed - pass it rather than downloading one:
`CHROMIUM_PATH=/opt/pw-browsers/chromium-1194/chrome-linux/chrome`. A first full run over the real
directory loads ~1,500 pages and takes about 30 minutes; it writes and commits every 100 articles,
so a run that is stopped part-way keeps what it read.

The fixtures exercise every branch of the extraction: a server-rendered page, a page whose body
only exists after JavaScript runs, a paywall teaser, a 404, an Atom feed, a German feed, and an
item syndicated by two feeds. A second run reports every article as cached.

## 3. Import the definitions

```bash
python3 tools/import_definitions.py --endpoint http://localhost:8080
curl -s -H "Authorization: Bearer $TOK" $B/chatai/sandboxes    # DefinitionError must be null
```

After editing anything in the workspace, export and commit it - the files here are the export:

```bash
curl -s -H "Authorization: Bearer $TOK" $B/graph/export-definitions -o defs.zip
```

### Entity recognition after an import or a vocabulary change

The spotters are built from the vocabularies the connector writes, so ingest (at least the
`reference` step) before importing the definitions. Two things do not happen on their own:

```bash
# The spotter models are cached: after the terms change, refresh each one.
for m in AVspotorg1111111111111 AVspotPLane11111111111 AVspotEnginE1111111111 AVspotRockeT1111111111 AVspotAirpoRt111111111; do
  curl -s -X POST -H "Authorization: Bearer $TOK" "$B/nlp/model/refresh?modelUID=$m"; done

# Articles already parsed are linked again with the new terms once their parser index is recreated.
# Links found by a term that has since been removed are NOT taken away - only a rebuilt workspace drops them.
curl -s -H "Authorization: Bearer $TOK" $B/index/list/FieldToDocumentIndex      # Article Title/Body IndexUIDs
curl -s -X POST -H "Authorization: Bearer $TOK" "$B/index/recreate?nodeType=Article&indexUID=<uid>"

# Check a spotter on a sentence:
curl -s -X POST -H "Authorization: Bearer $TOK" -H 'Content-Type: text/plain' \
  "$B/nlp/model/parse/?modelUID=AVspotRockeT1111111111" --data-binary "SpaceX launched a Falcon 9"
```

A refreshed spotter does not reach a pipeline that already built it: re-import the definitions (or
restart the server) after the terms change. With `--MSK_LOW_MEMORY=true` the parser and linking
indexes pause while the heap is fragmented - for up to an hour on a full ingest - so leave it off
on a machine with a few gigabytes to spare; linking 1,500 articles then takes a few minutes.

## 4. Exercise the AI layer without a model

Add the **Lorem Ipsum** provider, then send a `:replay` script: each `call` line really runs the
tool with exactly those arguments.

```bash
curl -s -X POST -H "Authorization: Bearer $TOK" $B/ai-provider/create-new/LoremIpsum
CHAT=$(curl -s -X POST -H "Authorization: Bearer $TOK" $B/chatai/new-chat | python3 -c "import sys,json;print(json.load(sys.stdin)['UID'])")

curl -s -X POST -H "Authorization: Bearer $TOK" -H 'Content-Type: text/plain' \
  "$B/chatai/post-message?chatUID=$CHAT&useTools=AVsandboX1111111111111" --data-binary @- <<'EOF'
:replay
call news_desk {"script":"stats --since 1d | head -30"}
call news_desk {"script":"cp /templates/sample-daily.json /work/daily.json && render-report report /work/daily.json && render-report deck /work/daily.json && publish daily-2026-10-06.html"}
say done
EOF

curl -s -H "Authorization: Bearer $TOK" "$B/chatai/list-messages?chatUID=$CHAT"
```

An agent's own turn can be scripted the same way, because its input message is what the provider
reads - which is how the delegated path (render into `/agents/...`, then publish from the
conversation) was tested:

```
:replay
agent "Aviation Market Intelligence Analyst" :replay\ncall news_desk {"script":"pwd; render-report report /templates/sample-daily.json brief"}\nsay rendered
say done
```

With a real provider configured instead, press **Daily brief** on the home page.

## 5. Deploying to a hosted workspace

The same steps against a hosted workspace (done for `demos.curiosity.ai/aviation-marketing-intelligence`
on 2026-10-07, starting from an empty workspace). Mint the tokens in the workspace (Manage → Tokens,
or the API below) with a system administrator.

```bash
B=https://<workspace>/api
TOK=$(curl -s -X POST $B/login/create -H 'Content-Type: application/json' -d '{"UserName":"<admin>","Password":"<password>"}' | tr -d '"')
LIBTOK=$(curl -s "$B/library/token?purpose=aviation-rss&duration=30" -H "Authorization: Bearer $TOK")
FETOK=$(curl -s "$B/library/token-front-end-upload?purpose=aviation-frontend&duration=30" -H "Authorization: Bearer $TOK")

# 1. The corpus: the stand-alone connector (vocabularies, then the 248 feeds, with a browser)
CHROMIUM_PATH=/opt/pw-browsers/chromium-1194/chrome-linux/chrome \
  dotnet run --project src/AviationIntel.Connector -- --root . --endpoint https://<workspace> --token "$LIBTOK"

# 2. The definitions - schemas, NLP, endpoints, the AI layer, and the in-workspace connectors
python3 tools/import_definitions.py --endpoint https://<workspace> --token "$TOK"

# 3. The inputs the in-workspace connectors read (feed directory + vocabularies, private files)
dotnet run --project src/AviationIntel.Connector -- --root . --endpoint https://<workspace> --token "$LIBTOK" --upload-data

# 4. The front end: built locally in Release, uploaded with the Curiosity CLI
dotnet tool install --global Curiosity.CLI --version 26.10.6426
dotnet build src/AviationIntel.FrontEnd/AviationIntel.FrontEnd.csproj -c Release
curiosity-cli upload-front-end -s https://<workspace> -t "$FETOK" -p src/AviationIntel.FrontEnd/bin/Release/netstandard2.0/tps
```

Step 2 can run as soon as step 1 has written the vocabularies (the first seconds of the run); the
spotters are built from them. Once the definitions are in, the **Aviation RSS feeds** task runs
every hour inside the server and **Aviation reference vocabularies** every Monday; run either now
with `POST $B/scheduling/run?uid=AVconnRSS1111111111111` (or `AVconnRef1111111111111`), and read the
run under Manage → Data → Connector Runs. To try an edited connector without writing anything:

```bash
# body: {"Code": "<the task file without its [task: ...] header lines>", "Name": "..."}; answers server-sent events
curl -s -X POST -H "Authorization: Bearer $TOK" -H 'Content-Type: application/json' \
  $B/scheduling/data-connector/dry-run --data-binary @request.json
```

A hosted workspace also offers a server-side front-end build (`/api/frontend-dev/*`, a git repository
the workspace compiles); it is not used here - the upload is the path this repository supports.

## What was validated, and what was not

Validated against a live server built from `mosaik` main (October 2026), with
`Curiosity.Library` / `Curiosity.FrontEnd*` 26.10, in two sessions:

- connector: dry run and real run over the fixtures, second run fully cached; **four full runs
  over the real 248-feed directory** with an unrestricted network (235 feeds OK, ~1,560 articles,
  82% read from the page on the last run), one of them into a fresh workspace;
- extraction tuned on real pages: ~20 articles spot-checked across newsrooms, trade titles,
  regulators and subscription feeds; the fixes are in the commit history and `TODO.md`;
- entity recognition: the five vocabularies, spotters, pipeline and linking, checked by
  `top_mentions` over the corpus and by reading the articles behind the bad ones;
- definitions: import into a fresh workspace with no warnings, compile, sandbox validation,
  round-trip byte-identical with the export;
- sandbox: every mount and command, `--mentions`, the report contract refusing a bad file, the
  daily and weekly samples rendered and published, the weekly deck downloaded as an 11-slide
  `.pptx`, the delegated agent path;
- front end: home (desk width, phone width, dark theme), news sets with mention chips, feeds,
  the article page with its mention tabs, the pinned chat page.

Not validated: report *content* written by a real model (no provider key in either session), and
the workspace's LLM-interpreted `search`.
