---
name: curiosity-toolchain-and-packages
description: The toolchain behind a Curiosity application — the Transpose C#-to-JavaScript compiler, the Tesserae UI component library, the .NET SDK, and the NuGet packages a connector, an endpoint project and a front end each need. Covers the update-packages-first rule and what breaks when versions drift apart. Read this before creating any project in a new Curiosity application repository.
---

# Toolchain and packages

Read this **first**, before creating any project. Half the "it does not build" time on a
Curiosity project is a toolchain that was never installed or packages that drifted apart.

---

## 0. Update every NuGet package to the latest release, first

**Do this before writing a line of code, and again at the start of any substantial piece of
work.** The Curiosity packages ship continuously; the published documentation lags them; and a
front end built against one version of the Curiosity packages and a different version of
Tesserae fails in ways that look like your bug.

```bash
dotnet restore
dotnet list package --outdated            # what has moved
dotnet add <project> package <Name>       # takes the latest stable
dotnet tool update --global Transpose.Compiler
```

Then **commit the version bump on its own**, before the work. A build failure after a
one-commit bump is diagnosable; the same failure inside a feature commit is not.

Rules that come with it:

- **Pin explicit versions in the csproj.** Never `*` or a floating range — a demo that
  resolves differently on the demo machine is not a demo.
- **Bump the whole Curiosity family together.** `Curiosity.FrontEnd`, `.Core`, `.API`,
  `.Admin` are one unit.
- **Bump Tesserae with them.** See §3 for the one case where going ahead of them is allowed.
- **Re-run the connector's schema emit after any package bump** that touches the library, and
  rebuild the front end — a signature that moved should surface as a compile error now, not
  during a demo.

---

## 1. The .NET SDK

Pin it in `global.json` at the repository root:

```json
{ "sdk": { "version": "10.0.0", "rollForward": "latestMajor", "allowPrerelease": true } }
```

Back-end projects (connector, dataset read model, checkers) target the current .NET —
`net10.0` at the time of writing. The **front end is different**: see §3.

On Linux, projects that pull in native assets (speech, ONNX runtime, SkiaSharp) need an
explicit `RuntimeIdentifier` or MSBuild tries to copy every platform's natives into the
output:

```xml
<PropertyGroup Condition="$([MSBuild]::IsOSPlatform('Linux'))">
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
</PropertyGroup>
```

---

## 2. Transpose — the C#-to-JavaScript compiler

**The Curiosity front end is C#, compiled to JavaScript by Transpose.** Not TypeScript, not
React, not Blazor. You write C#, reference the Curiosity front-end packages and Tesserae, and
the build emits a JavaScript bundle.

Two consequences you feel immediately:

- The front-end project uses a **Transpose SDK** rather than `Microsoft.NET.Sdk`, and targets
  `netstandard2.0` with `Transpose.BCL` standing in for the framework's own BCL.
- **`Transpose.Compiler` is a global dotnet tool** that the build shells out to (the binary on
  `PATH` is `tps`). A fresh container does not have it:

```bash
dotnet tool install --global Transpose.Compiler
export PATH="$PATH:$HOME/.dotnet/tools"      # add to your profile to persist
tps --help                                    # verify before building anything
```

If a front-end build fails with `MSB3073 … tps: not found` or exit code 127, the tool is
missing or `PATH` does not have `$HOME/.dotnet/tools` **in that shell**. Real C# errors only
appear after that step succeeds, as ordinary `CS….`

`tps.json` beside the project controls the bundle:

```json
{
  "output": "$(OutDir)/tps/",
  "fileName": "app.js",
  "outputFormatting": "Formatted",
  "combineScripts": false,
  "html": { "disabled": false, "title": "<Application name>" },
  "cleanOutputFolderBeforeBuildPattern": "*.js|*.css",
  "console": { "enabled": false },
  "loader": { "type": "Global" }
}
```

Keep a `tps.Release.json` alongside it with minification on.

**What Transpose costs you** — the constraints are in `curiosity-frontend-setup` §4, and the
one that bites first is: **no runtime reflection**, so every DTO crossing the boundary is flat.

**A clean build of one entry app is not proof.** Some entry projects consume referenced
projects as already-transpiled modules and do not re-run the compiler over their sources —
Transpose-only errors there compile fine and surface only when an entry app that *does*
re-transpile them is built. Build that one.

---

## 3. Tesserae — the UI component library

**Tesserae is the C# UI component library the front end is written in**, compiled by
Transpose and used through fluent builders. Its source is public
(`curiosity-ai/tesserae`) — **read it for any component's real constructor and fluent
surface**, because the published docs lag it.

What you use constantly: `TextBlock`, `Card`, `VStack` / `HStack`, `SplitView`, `Grid`,
`Defer`, `SearchableList`, `Dropdown`, `SegmentedPivot`, `Toast`, `Modal`, `DetailsGrid`,
`InlineLabel`, `Badge`, `Metric`, `Sparkline`, `ContributionBar`, `Banner`, `IconTile`,
`Plan`, `UIcons`.

Conventions:

- Use the **factory functions**, not `new`: `SearchArea()`, `Card(...)`, `VStack()`.
- Drive styling through the **fluent helpers** — `Background`, `Foreground`, `Padding`/`P`/
  `PL`…, `Margin`/`M`…, `Width`/`MaxHeight`…, `Rounded`, `Small`/`Medium`/`Large`,
  `SemiBold`/`Bold` — never by reaching into the rendered DOM. For CSS with no helper
  (`border`, `letter-spacing`, `font-family`, `overflow`) use the `.Style(s => …)` extension.
- **Colours come from the theme tokens** (`Theme.Default.Foreground`, `Theme.Primary.Background`,
  `Theme.Colors.Neutral400`…), never inlined CSS variable strings.
- Size/weight helpers set **discrete design tokens**, not arbitrary values — pick the token
  whose value matches, and use `.Style` for a bespoke size.
- `.WS()` / `.HS()` / `.S()` are width-100 % / height-100 % / both.

**Version pinning.** Pin Tesserae to the version the Curiosity packages were built against.
The one exception: if you need a component newer than that, a direct `PackageReference`
*ahead* of it works as long as the additions are additive — the pre-transpiled Curiosity
modules keep working. Bump both together as soon as the Curiosity packages catch up.

---

## 4. Packages per project

Versions below were current at the time of writing. **Take the latest** (§0) and pin what you
resolve.

### The connector — `src/<App>.Connector/<App>.Connector.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <!-- the graph client: Graph.Connect, [Node]/[Key]/[Property], HashCache, UploadFileAsync -->
    <PackageReference Include="Curiosity.Library"                     Version="26.8.3830" />
    <PackageReference Include="Microsoft.Extensions.Logging"          Version="10.0.10" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console"  Version="10.0.10" />

    <!-- source readers: add only what this connector actually reads -->
    <PackageReference Include="CsvHelper"                             Version="33.1.0" />   <!-- CSV/TSV -->
    <!-- <PackageReference Include="Parquet.Net"       Version="…" /> -->                    <!-- Parquet -->
    <!-- <PackageReference Include="ClosedXML"         Version="…" /> -->                    <!-- xlsx -->
    <!-- <PackageReference Include="ExcelDataReader"   Version="…" /> -->                    <!-- big xls/xlsx -->
    <!-- <PackageReference Include="YamlDotNet"        Version="…" /> -->
    <!-- <PackageReference Include="Dapper"            Version="…" /> -->                    <!-- + a DB provider -->
    <!-- <PackageReference Include="Npgsql"            Version="…" /> -->
    <!-- <PackageReference Include="Microsoft.Data.SqlClient" Version="…" /> -->
    <!-- System.Text.Json, System.Xml.Linq, System.IO.Compression, System.Net.Http: in-box -->
  </ItemGroup>
</Project>
```

See `curiosity-data-connector` §1.1 for which reader to choose for which format.

### The corpus read model — `src/<App>.Dataset/`

`System.Text.Json` (in-box) plus `CsvHelper` if the corpus has CSV. Nothing else: this project
is the executable schema and should have no dependency that can make it fail for another
reason.

### The front end — `src/<App>.FrontEnd/<App>.FrontEnd.csproj`

```xml
<Project Sdk="Transpose.Build.Target/26.7.2692">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <!-- CS1998 async-without-await, CA2007 ConfigureAwait: noise in transpiled code -->
    <NoWarn>CS1998,CA2007</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Curiosity.FrontEnd"       Version="26.8.3828" />
    <PackageReference Include="Curiosity.FrontEnd.Core"  Version="26.8.3828" />
    <PackageReference Include="Curiosity.FrontEnd.API"   Version="26.8.3828" />
    <PackageReference Include="Curiosity.FrontEnd.Admin" Version="26.8.3828" />
    <PackageReference Include="Transpose.BCL"            Version="26.7.3303" />
    <!-- pinned to the Tesserae the Curiosity packages above were built against -->
    <PackageReference Include="Tesserae"                 Version="2026.8.69318" />
  </ItemGroup>

  <ItemGroup>
    <None Update="tps.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>

</Project>
```

| Package | What it gives you |
| --- | --- |
| `Curiosity.FrontEnd` | `App.Initialize`, `App.DefaultSettings`, the sidebar, routing, `NodeRendererBase`, `SearchArea`, `ChatView` |
| `Curiosity.FrontEnd.Core` | helpers, request plumbing, `UID128`, route state |
| `Curiosity.FrontEnd.API` | the typed `API.*` server calls (`API.Query`, `API.Nodes`, `API.ChatAI`, `API.Endpoints`) |
| `Curiosity.FrontEnd.Admin` | the admin area, lazily loaded |
| `Transpose.BCL` | the BCL subset Transpose emits against |
| `Tesserae` | every UI component |

### Endpoints and the AI layer

**No csproj.** These are `.cs` *scripts* stored in the workspace and imported as definitions —
see `curiosity-code-endpoints`. They have no package references of their own: the execution
scope hands them `Graph`, `Q()`, `Body`, `CurrentUser`, and the workspace injects the
generated `N` / `E` above every script.

Keep them in a plain folder (`src/<App>.Definitions/code/`), version-controlled, with **no
project file** — a csproj there would only pretend they compile locally.

---

## 5. Verify the toolchain before you start

```bash
dotnet --list-sdks            # includes the version global.json pins
tps --help                    # the Transpose compiler is on PATH
dotnet list package --outdated  # nothing important is behind
```

If any of the three is wrong, fix it before writing code. Every one of them produces a failure
that reads as something else.

---

## 6. Checklist

- [ ] All NuGet packages updated to the latest release, in their own commit, before the work
- [ ] Versions pinned explicitly — no floating ranges
- [ ] The Curiosity front-end family bumped as one unit
- [ ] Tesserae pinned to what the Curiosity packages were built against
- [ ] `global.json` pins the SDK
- [ ] `Transpose.Compiler` installed globally and on `PATH`
- [ ] The front end targets `netstandard2.0` under the Transpose SDK, with `tps.json` copied
- [ ] The entry app that re-transpiles everything is the one being built
- [ ] Only the source readers this connector actually needs are referenced
- [ ] Endpoints and AI tools live in a plain folder, with no csproj

---

## Reference implementation

The csproj templates referenced above are real files:
[`../curiosity-data-connector/examples/App.Connector.csproj`](../curiosity-data-connector/examples/App.Connector.csproj)
and
[`../curiosity-frontend-setup/examples/App.FrontEnd.csproj`](../curiosity-frontend-setup/examples/App.FrontEnd.csproj),
with [`../curiosity-frontend-setup/examples/tps.json`](../curiosity-frontend-setup/examples/tps.json)
beside the second.
