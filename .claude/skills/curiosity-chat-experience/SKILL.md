---
name: curiosity-chat-experience
description: Building the conversational surface of a Curiosity application — replacing the stock chat page with a purpose-built ChatView, pinning an assistant and a capability set, welcome prompts, parsing replies against the corpus so identifiers become links, and handing a question to the assistant from a record screen with its evidence already attached. Use when building the chat page or any "ask the assistant about this" button.
---

# The chat experience — a purpose-built conversation

The stock chat page is a general-purpose one: the user picks an assistant, then picks which
tools and agents it may use this turn. In a purpose-built application **neither choice makes
sense**. There is one assistant worth talking to, and the capabilities it needs are the same
every turn. A picker over that set can only be used to break it.

So: pin both, hide both pickers, and give the user a chat that always answers with everything
it has.

---

## 1. Replacing the chat page

```csharp
settings.AssistantView = MyChatView.Create;
```

```csharp
public static IComponent Create(Parameters state) => new ChatView(Configuration, state);

private static readonly ChatViewConfiguration Configuration = new ChatViewConfiguration
{
    Title                    = "Analysis Assistant".t(),
    WelcomeTitle             = "What are we analysing?".t(),
    WelcomePrompts           = WelcomePrompts,
    ChatBoxPlaceholderText   = "Ask about a change, a case, or what is on your desk".t(),

    DisableAssistantSelector = true,
    DisableTools             = true,
    ShowCitationsPanel       = true,

    ParseAs                  = new ChatViewParseAs { PipelineUID = …, NodeType = …, FieldName = … },

    ChatRoute                = chatUID => DefaultRoutes.ChatAI.OpenChat(chatUID, default),

    NewChat                  = NewChatAsync,
    PostMessage              = PostMessageAsync,
    ReplaceMessage           = ReplaceMessageAsync,
};
```

Build the configuration **once** and share it across visits — it holds no per-chat state; the
view takes all of that from its parameters.

**Do not set a category** unless you mean it. Chats opened from a record's "Ask" button are
uncategorised, and a categorised view lists its own chats and not those — the button and the
page have to be two ways into one conversation history.

---

## 2. Pinning the assistant and the capabilities

Three hooks, and **all three** must pin, or the pinning has holes:

```csharp
private static readonly UID128[] PinnedTools  = { Tool_Search, Tool_Consult, Tool_Workload };
private static readonly UID128[] PinnedAgents = { Agent_Analysis, Agent_Comparison };

// Every chat starts on the purpose-built assistant, whatever the workspace's selected
// template happens to be. The *provider* is still the user's — which model answers is a
// preference; which assistant answers is what this page is.
NewChat        → API.ChatAI.NewChat(request.ProviderUID ?? default, AssistantUID, request.Category);

PostMessage    → API.ChatAI.PostMessage(chat, message, useTools: PinnedTools,
                                        useAgents: PinnedAgents, useSkills: request.ActiveSkills, ct);

ReplaceMessage → the same pinning, for edit and regenerate
```

- **`ReplaceMessage` too.** Overriding only `PostMessage` leaves "edit" and "regenerate"
  re-running the turn with no tools, which reads as the assistant having got worse.
- **Do not pass the request's active tools through** when the picker is disabled — they always
  arrive empty, and passing them runs the turn with no capabilities at all.
- **Pass skills through untouched.** `null` means "not chosen", which is what lets the run fall
  back to the assistant's own skills.
- **Restate the capability UIDs here as documented constants.** The assistant is configured
  with them too, but an assistant edited in the admin UI to drop a reader would otherwise
  silently turn every answer into a guess. Say in a comment which UIDs are product built-ins
  (stable across deployments) and which are your own definitions.

---

## 3. Welcome prompts

Open on what is answerable **from an empty chat**: workload questions and corpus questions the
assistant settles on its own. The judgement questions the agents exist for need records in the
conversation, and those arrive through a record's "Ask" button — not through a suggestion pill
that produces a disappointing first experience.

Four is a good number. Make one of them a corpus question phrased the way a user actually
speaks, not the way the schema is named.

---

## 4. Parsing replies against the corpus

If answers are written in identifiers — part numbers, case ids, document references — parse
every reply through the workspace's data-parsing pipeline (`ParseAs`). Each identifier then
becomes the record it names, opening in the preview pane beside the conversation, so the user
reads the answer and the evidence without leaving the chat.

A mention the parse cannot resolve, or one the reader has no access to, stays plain text.

This is a direct payoff from `curiosity-entity-linking-nlp`: the same spotters that link the
corpus make the assistant's prose navigable.

Turn the **citations panel** on where the sources behind an answer *are* the answer.

---

## 5. Handing a question over with its evidence attached

The highest-value AI entry point is not the chat page; it is a button on a record screen that
opens a chat with the relevant nodes **already attached**.

```csharp
var chat = await API.ChatAI.NewChatWithContext(providerTaskUID, AssistantUID, uids);

API.ChatAI.PostMessage(chat.UID, question).FireAndForget();

NodePreview.CloseAll();
NodePreview.CloseAll();
Router.Navigate(DefaultRoutes.ChatAI.OpenChat(chat.UID, null), reload: true);
```

Five things in that sequence are deliberate:

- **The context is the whole point.** A question typed into a bare chat makes the assistant
  hunt for documents by title; the same question with the nodes attached lets it consult them
  by id. That is the difference between an answer about *these* records and an answer about
  records with similar titles.
- **Attach the subject too**, not only the candidates. A verdict about a change the assistant
  cannot read is a guess.
- **Name the assistant by UID**, rather than taking the user's selected template. This is not
  the workspace's general assistant being asked a domain question; it is the one carrying the
  system prompt, the agents and the readers.
- **Do not await the post.** Awaiting holds the user on the old screen until the whole answer
  is generated. Firing first means the request is already with the server, so the chat opens
  onto an answer that is already streaming.
- **Close the preview stack twice.** A record opened from inside another record's preview
  leaves two sheets stacked, and navigating with one still up drops the chat behind it.

Handle the failures visibly: nothing resolvable to attach, or an assistant that cannot be
opened, should raise a toast naming the cause — not silently do nothing.

### Naming the agent in the question

End a generated question with a line saying which agent should answer, and to consult each
document and its attached file first. The assistant still decides and still answers if the
agent is unavailable — it is a hint, not a bypass — but a question arriving through a button
has a known shape, and saying so is free accuracy.

---

## 6. Checklist

- [ ] Stock chat page replaced; configuration built once and shared
- [ ] Assistant pinned in new-chat; tools and agents pinned in **both** post and replace
- [ ] Both pickers hidden, and nothing passed through from the empty selection
- [ ] Skills passed through untouched
- [ ] Capability UIDs restated as documented constants, built-ins distinguished from your own
- [ ] Welcome prompts are answerable from an empty chat
- [ ] Replies parsed against the corpus where answers are written in identifiers
- [ ] Every "Ask" button attaches the candidates **and** the subject
- [ ] The post is fired, not awaited; preview sheets closed before navigating
- [ ] Failures raise a toast rather than doing nothing

---

## Reference implementation

Commented templates in [`examples/`](examples/):

| File | Shows |
| --- | --- |
| [`AppChatView.cs`](examples/AppChatView.cs) | The full `ChatViewConfiguration`: pinned assistant, pinned capabilities in all three hooks, reply parsing, welcome prompts |
| [`AiAssist.cs`](examples/AiAssist.cs) | Handing a question over with its evidence attached, including an example caller |
