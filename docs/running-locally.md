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
    ./curiosity --MSK_LOW_MEMORY=true --MSK_DISABLE_RESOURCE_MONITORING=true --MSK_UNSAFE_AUTO_LOGIN=true \
    > /tmp/server.log 2>&1 < /dev/null &

until [ "$(curl -s http://localhost:8080/api/graph/ready)" = "true" ]; do sleep 3; done
```

`MSK_UNSAFE_AUTO_LOGIN` signs the browser in as `admin` - local use only. A front-end-only change
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

## What was validated, and what was not

Validated against a live server built from `mosaik` main (October 2026), with
`Curiosity.Library` / `Curiosity.FrontEnd*` 26.10:

- connector: dry run and real run over the fixtures; second run fully cached; the real 248-feed
  directory parsed and fetched (most hosts were blocked by the container's egress policy; the two
  reachable arXiv feeds ingested for real, falling back to feed text when the article host was
  blocked);
- definitions: import, compile, sandbox validation, round-trip byte-identical with the export;
- sandbox: every mount and command, the report contract refusing a bad file, both templates
  rendered and published as artifacts, the delegated agent path;
- front end: home, news sets, feeds, article preview, artifact pane, the report button handoff -
  with no console errors.

Not validated here: report *content* written by a real model (no provider key in the test
environment), and live extraction quality across the real publishers' pages.
