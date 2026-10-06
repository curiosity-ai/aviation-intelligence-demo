---
name: curiosity-ai-tools-and-agents
description: How to build the AI layer of a Curiosity application — which work belongs to an assistant, an agent, a consult reader or a custom tool; writing per-node-type consult readers; custom tools over graph state; agent definitions; assistant system prompts; and the context rules that decide whether an answer is grounded. Use when adding or changing anything under code/chat-ai-tools, code/agents, or an assistant prompt.
---

# The AI layer — assistants, agents, tools, readers

Four layers, each with a job. Conflating them is what produces an assistant that
confidently invents identifiers.

```
ASSISTANT   one system prompt. Knows the domain, the corpus, the flow, what to refuse,
            and which agent answers which shape of question. The method.
    │
    ├── AGENT        a sub-task with its own prompt and its own tools. Invoked by name for
    │                a multi-document judgement that deserves its own reasoning budget.
    │
    ├── CONSULT      per-node-type readers. "Read this record in full."
    │   READERS      One per type the user can attach. No reader ⇒ "not consultable".
    │
    ├── SEARCH       the built-in. Reaches records that are NOT in the conversation.
    │
    └── CUSTOM       your own tools, over your own graph queries. Workload, state,
        TOOLS        aggregates — the questions that are about the user, not the corpus.
```

---

## 1. Consult readers — the grounding layer

The consult tool is the one built-in an admin can extend, and what is editable is *only* the
per-node-type readers. The tool surface — the entry point, UID parsing, access and
chat-context checks, and the switch on node type — is generated around your methods at
compile time. So the file declares **no class and no tool attribute**, only methods following
a fixed schema:

```csharp
object Consult<NodeType>(ToolScope scope, UID128 uid) { … }
```

Rules:

- **One reader per node type a user can attach.** A type with no reader is reported to the
  model as *not consultable*, and the model then answers by guessing from the title.
- **Include the revision types.** A comparison screen attaches a revision; without a reader
  the model can only read half the pair.
- **Helper methods must not start with `Consult`** — that prefix is what marks a reader, and a
  helper carrying it fails the schema check at save time.

**What a reader should return** — three blocks, and all three matter:

| Block | Why |
| --- | --- |
| `fields` | Every named field that has a value; skip the empty ones rather than sending nulls |
| `related` | What this record points at, grouped by kind — categories, parts, components, locations, systems, applicability. **This is the half a reader skims past and the half a model cannot infer from prose**, and it is exactly what a "do these two overlap?" question turns on. Cap it per kind: the answer is about the overlap, not the inventory |
| `attachments` | The published files hanging off the node, each with its uid, name, content type and page count, **marked consultable**. Consulting the record then consulting its attachment is how the model reads the document as published rather than as a field dump |

Also set a human-readable display name for the tool call (`"Reading 'Cargo Door Surround
Reinforcement'"`), so the user watching the conversation can see what it is doing.

**The attachments point is worth restating.** Where the published artefact is what a human
actually reads, a model that only ever sees fields will only ever reason about fields, and the
detail that decides the answer usually lives in the paragraph that was rendered, not in the
metadata.

---

## 2. Custom tools — the operational layer

Questions about *the user's own work* ("what is on my desk", "what is urgent", "what could I
pick up") must not go through search. Search ranks documents by text; these read edges.

Write them as a normal class with tool-attributed static methods:

```csharp
[tools: Curiosity.ChatAITools.UID("<random 22-char uid>")]
[tools: Curiosity.ChatAITools.DisplayName("Case Tools")]
[tools: Curiosity.ChatAITools.Description("…when to use this, and what it returns…")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

[ToolSystemPrompt("""
                  Workload questions are answered with these tools, never with a search.
                  A record listed by one of them is not consultable by its id alone: to read
                  one in full, search for its identifier first and consult what comes back.
                  """)]
public class CaseTools
{
    [Tool("Lists the cases open on an engineer's desk … Returns uid, id, title, status, …")]
    public static string ListInbox(ToolScope scope,
        [Parameter("Whose inbox — user name or full name. Empty for the current user.", required: false)] string engineer = null,
        [Parameter("Maximum to return (default 25).", required: false)] int limit = 25) { … }
}
```

Four things this project learned:

- **Compute exactly what the dashboard computes**, from the same edges with the same
  definitions. The user is looking at those counters on the home page while they talk to the
  assistant; an assistant that quietly counts differently is worse than one that cannot count.
- **A UID returned by a custom tool is not consultable.** Consult only reads nodes attached to
  the conversation or turned up by the built-in search. So each row must carry enough to
  answer the question outright, and both the tool description and the `[ToolSystemPrompt]`
  must tell the model how to reach the full text (search by identifier, then consult).
  Returning a UID the model cannot then open is a trap you have set for it.
- **The description is the routing logic.** Say when to use it, when *not* to (i.e. instead of
  search), and list the fields it returns. This is read by the model on every turn.
- **Mint a random UID.** Recognisable hand-shaped ids belong to built-ins that are re-seeded on
  every boot; a tool created by an import is referred to by whatever id it was given.

---

## 3. Agents

An agent is a sub-task with its own prompt, its own tool set and its own reasoning budget,
invoked by name. Use one when the question is a **multi-document judgement** — not for a
lookup the assistant can settle with one consult.

```csharp
[agent: Curiosity.Agents.UID("<uid>")]
[agent: Curiosity.Agents.Name("Impact Analysis Agent")]
[agent: Curiosity.Agents.ChatTask("<provider task uid>")]
[agent: Curiosity.Agents.Tool("<search tool uid>")]
[agent: Curiosity.Agents.Tool("<consult tool uid>")]

…the agent's prompt, in markdown…
```

Give it the consult tool — that is what puts the readers in its hands.

A good agent prompt has five parts, in this order:

1. **The job, in one sentence**, and explicitly what it is *not* (it judges; it does not
   design, cost or approve).
2. **What the corpus holds** — the types and what each carries. The model cannot infer this.
3. **How to work**, as numbered steps. The two that matter most:
   - *consult everything before judging anything* — "a verdict about a document you have not
     read is a guess with a confident tone";
   - *look at the structured overlap first, then read for the conceptual overlap*. Shared
     identifiers are the half a reader skims; shared meaning with no shared words is the half
     a keyword search misses. Naming both stops the model doing only one.
4. **What to report**, in a fixed order: verdict first, then severity, then **the named
   mechanism**, then **the evidence as quotations from each document**, then what has to
   happen next.
5. **How to be uncertain**: say "uncertain", say what would settle it. "A confident wrong
   verdict is worse than an honest gap" — and mean it, because this is what a decision gets
   taken on.

Two agents covered the reference implementation: one that judges impact across candidates,
one that reports what changed between two revisions. That is a good default pair for any
"analyse a change against a corpus" application.

---

## 4. The assistant

One system prompt, and it is the whole method. Sections that earned their place:

| Section | Content |
| --- | --- |
| **Identity + job** | Who it is, and the exact step of the process it supports |
| **What the workspace holds** | A table of record types and what each is. Plus: *never invent an identifier — one you did not read in a tool result does not exist* |
| **Your tools** | Each one: what it does, when to use it, and the trap. Be explicit that attached items are listed with an id and a title but **not** their content, and that ids are exact-length and case-sensitive — copy them verbatim, never guess |
| **The flow, end to end** | **One worked example**, numbered, with real-shaped identifiers, from question to answer. This is the single most effective part of the prompt |
| **In scope** | A table of question shapes → which tool or agent answers it. Cover the workload questions, the read questions and the judgement questions separately |
| **Out of scope** | Named, with the instruction to say so in one sentence and stop — no apology, no length. Include anything safety-, legally- or contractually-determining, with "you inform that decision, you never make it" |
| **How to answer** | Lead with the answer; the user has the documents open. Quote, do not paraphrase. Name the identifier two records share |

**Check what your product version exports.** If assistant templates are outside the
definitions export, keep the prompt in the repository as a markdown file with a note saying it
is pasted in by hand, and cross-reference the assistant's UID from wherever the front end
names it.

---

## 4.1 Sandboxes and artifacts

Verified on 26.10 against a live server (the Aviation News Desk in
`src/AviationIntel.Definitions/code/chat-ai-tools/AVsandboX1111111111111.cs`):

- **A sandbox is an AI tool whose code returns an `ISandbox`** - mounts (`SandboxMount.Lazy`,
  `.Live`, `.Cached`, `.Files`, `.Scratch`), `SandboxCommand.Create(name, usage, call => ...)`, the
  workspace's own `SandboxCommand.Search(types)` / `.Consult(types)`, and `Query =>
  SandboxQueryAccess.For(types)` for the graph query engine. The model calls it as one tool,
  `FunctionName`, with a `script` argument run in an in-process bash/python.
- **`Artifacts => new[] { ArtifactType.Html, ArtifactType.Pdf }`** adds `/artifacts` and `publish`;
  the published page opens in the chat's side pane in a frame that can reach nothing.
- **Put the `using` block first in the file**, before any comment: the workspace injects `N`/`E`
  above the code unless the usings start at index 0, and a using after a declaration does not
  compile.
- **Check it compiled and validated:** `GET /api/chatai/sandboxes` lists every sandbox with its
  mounts, commands and `DefinitionError` (null when valid).
- **A delegated agent run is confined.** When the conversation hands work to an agent, every mount
  is read-only to it except `/agents/<agent>/<run>` (its working directory), and it **cannot
  publish**. Write output there (`call.WorkingDirectory` starts with `/agents/`), return the path,
  and publish from the conversation.
- **The artifact's name is the page's `<title>` at publish time**, read from the file - set it
  server-side; a title the page sets from script is too late.
- **Test without a model:** the Lorem Ipsum provider plus a `:replay` message (`call <tool> <json>`,
  `agent "<name>" <message>`, `say ...`) runs tools for real. An agent's own turn can be scripted
  by making its input message a `:replay` script.

## 5. Grounding rules — the ones that decide whether it works

- **Attach the evidence, do not describe it.** A question typed into a bare chat makes the
  assistant hunt for documents by title. The same question with the nodes attached lets it
  consult them by id. Every AI entry point in the UI should attach the records the question is
  about — see `curiosity-chat-experience`.
- **Attach the subject too, not only the candidates.** A verdict about a change the assistant
  cannot read is a guess.
- **Every node type the user can attach needs a reader.**
- **Prefer identifiers over prose when searching**, and say so in the prompt.
- **Say what the model may not invent.** Identifiers, agent names, tool names.

---

## 6. Checklist

- [ ] A consult reader for every node type a user can attach, revisions included
- [ ] Every reader returns `fields`, `related` and consultable `attachments`
- [ ] No helper method named with the reader prefix
- [ ] Custom tools compute exactly what the dashboard computes, from the same edges
- [ ] Custom-tool rows are self-sufficient, and the prompt says how to reach full text
- [ ] Tool descriptions say when to use **and when not to** use each tool
- [ ] Agents carry the consult tool; prompts state job, corpus, method, report shape, uncertainty
- [ ] The assistant prompt contains one full worked example
- [ ] Out-of-scope and never-invent rules are explicit
- [ ] Custom UIDs are randomly minted and written down once per side

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`consult-readers.cs`](examples/consult-readers.cs) | The fixed reader schema, and the `fields` / `related` / `attachments` response |
| [`workload-tool.cs`](examples/workload-tool.cs) | A custom tool over graph state, with the tool system prompt and self-sufficient rows |
| [`impact-agent.cs`](examples/impact-agent.cs) | An agent definition and the five-part prompt structure |
| [`assistant-system-prompt.md`](examples/assistant-system-prompt.md) | A full assistant prompt, including the worked example |
