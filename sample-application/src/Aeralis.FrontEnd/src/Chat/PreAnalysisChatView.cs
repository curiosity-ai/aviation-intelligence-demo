using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Components;
using Aeralis.FrontEnd.Schema;
using TNT;
using UID;
using static TNT.T;

namespace Aeralis.FrontEnd.Chat
{
    /// <summary>
    /// The workspace's chat page, rebuilt around MOD pre-analysis.
    ///
    /// The stock <see cref="ChatView"/> is a general-purpose chat: the user picks an assistant,
    /// then picks which of the workspace's tools and agents that assistant may use this turn.
    /// Neither choice makes sense here. There is exactly one assistant worth talking to in this
    /// workspace — the Aeralis Pre-Analysis Assistant, whose system prompt is the whole method —
    /// and the capabilities it needs are the same on every turn: read the corpus, read the
    /// engineer's workload, and hand the two hard questions to the two agents that answer them.
    /// A picker over that set can only be used to break it.
    ///
    /// So this view fixes both, and hides both pickers. The assistant is pinned in
    /// <see cref="Configuration.NewChat"/>; the capability set is pinned in
    /// <see cref="Configuration.PostMessage"/> / <see cref="Configuration.ReplaceMessage"/>.
    /// What the engineer is left with is a chat that always answers with everything it has.
    ///
    /// It deliberately does **not** set a <see cref="ChatViewConfiguration.Category"/>: the chats
    /// <see cref="AiAssist"/> opens from a document's "Ask" button are uncategorised, and a
    /// categorised view would list its own chats and not those — the button and this page have to
    /// be two ways into one conversation history.
    /// </summary>
    internal static class PreAnalysisChatView
    {
        // ------------------------------------------------------------------------------------
        // The pinned capability set.
        //
        // These are the UIDs the pre-analysis assistant is configured with, restated here so the
        // page does not depend on that configuration staying right: an assistant edited in the
        // admin UI to drop `consult` would otherwise silently turn every answer into a guess
        // about document titles. `search` and `consult` are the workspace built-ins and their
        // UIDs are stable across deployments; the other three are this demo's own definitions
        // under src/Aeralis.Definitions/code/.
        // ------------------------------------------------------------------------------------

        /// <summary>The built-in <c>search</c> tool: finds documents that are not in the conversation.</summary>
        private static readonly UID128 Tool_Search = new UID128("LLMSearCh1111111111111");

        /// <summary>The built-in <c>consult</c> tool: reads a document, or its published PDF, by id.</summary>
        private static readonly UID128 Tool_Consult = new UID128("ConsuLt111111111111111");

        /// <summary>
        /// This demo's <c>PreAnalysisTools</c> — <c>ListInbox</c>, <c>ListUrgentActions</c>,
        /// <c>ListUnassignedWorkOrders</c>. They read the same assignment edges the home page's
        /// counters read, which is what keeps the assistant and the dashboard from disagreeing.
        /// </summary>
        private static readonly UID128 Tool_PreAnalysisWorkload = new UID128("Ti5KgKu53EQANVi7ZHKyrV");

        /// <summary>The Aeralis Document Comparison Agent: what changed between two issues of a document.</summary>
        private static readonly UID128 Agent_DocumentComparison = new UID128("Ugw5PmHWfnXJPvfv6E3WaK");

        /// <summary>The Aeralis Pre-Analysis Agent: whether a change impacts a set of candidate documents.</summary>
        private static readonly UID128 Agent_PreAnalysis = new UID128("YQVcpstq48yYcQm61rBU2u");

        private static readonly UID128[] PinnedTools = new[] { Tool_Search, Tool_Consult, Tool_PreAnalysisWorkload };

        private static readonly UID128[] PinnedAgents = new[] { Agent_PreAnalysis, Agent_DocumentComparison };

        /// <summary>
        /// The workspace's own data-parsing pipeline, imported with the rest of the definitions
        /// (<c>src/Aeralis.Definitions/nlp/pipelines/</c>). It carries the spotters that index
        /// the corpus — part numbers, FINs, drawing numbers, harness references, and the doc-id
        /// spotter of every manual type — which is exactly the vocabulary an assistant reply is
        /// written in.
        /// </summary>
        private static readonly UID128 DataParsingPipeline = new UID128("cZnyGTpZbhrCY61GfvokSb");

        /// <summary>
        /// Opens on what is answerable from an empty chat. Every one of these is a workload or a
        /// corpus question the assistant settles on its own — the impact and comparison questions
        /// the agents exist for need documents in the conversation, and those arrive through a
        /// document's "Ask" button (<see cref="AiAssist"/>), not through a suggestion pill.
        /// </summary>
        private static readonly string[] WelcomePrompts = new[]
        {
            "What is on my desk right now?".t(),
            "Which of my work orders are urgent?".t(),
            "Are there unassigned pre-analyses I could pick up?".t(),
            "Is there an existing repair on the fwd cargo door surround?".t(),
        };

        /// <summary>
        /// Built once and shared by every visit to the page. The configuration holds no per-chat
        /// state — <see cref="ChatView"/> takes all of that from the <see cref="Parameters"/> it
        /// is constructed with — so rebuilding it per navigation would only cost allocations.
        /// </summary>
        private static readonly ChatViewConfiguration Configuration = new ChatViewConfiguration
        {
            Title                    = "Pre-Analysis Assistant".t(),
            WelcomeTitle             = "What are we analysing?".t(),
            WelcomePrompts           = WelcomePrompts,
            ChatBoxPlaceholderText   = "Ask about a modification, a work order, or what is on your desk".t(),

            // One assistant, one capability set: neither is the engineer's to change, so neither
            // gets a picker. Dropping the tool picker is what makes the pinned set in
            // PostMessageAsync the only answer to "what can it use" - a picker left in place with
            // nothing in it is a control that can only disappoint.
            DisableAssistantSelector = true,
            DisableTools             = true,

            // The sources behind an answer are the answer, in a pre-analysis: a verdict on a
            // candidate document is only worth reading next to the documents it was reached from.
            ShowCitationsPanel       = true,

            // An answer here is written in identifiers - "D3450128437161", "172TR7", "AMM-M-53-10-11".
            // Parsing every reply against the corpus turns each of those into the record it names,
            // opening in the preview pane beside the conversation, so the engineer reads the answer
            // and the evidence without leaving the chat. A mention the parse cannot resolve, or one
            // the reader has no access to, stays plain text.
            ParseAs                  = new ChatViewParseAs
            {
                PipelineUID = DataParsingPipeline,
                NodeType    = N.Modification.Type,
                FieldName   = N.Modification.Title,
            },

            ChatRoute                = chatUID => DefaultRoutes.ChatAI.OpenChat(chatUID, default),

            NewChat                  = NewChatAsync,
            PostMessage              = PostMessageAsync,
            ReplaceMessage           = ReplaceMessageAsync,
        };

        /// <summary>
        /// The page behind <c>#/chat-ai</c>, installed over the stock chat view in
        /// <see cref="AeralisApp.Configure"/>.
        /// </summary>
        public static IComponent Create(Parameters state) => new ChatView(Configuration, state);

        /// <summary>
        /// Starts every chat on the pre-analysis assistant, whatever the workspace's selected
        /// template happens to be. The provider is still the user's — which model answers is a
        /// preference; which assistant answers is what this page is.
        /// </summary>
        private static Task<ChatMetadata> NewChatAsync(ChatViewConfiguration.NewChatRequest request)
        {
            return Mosaik.API.ChatAI.NewChat(
                request.ProviderUID ?? App.InterfaceSettings.ChatAIProvider?.TaskUID,
                UID128.Parse(AiAssist.AssistantUID),
                request.Category);
        }

        /// <summary>
        /// Posts with the pinned tools and agents rather than with the composer's selection.
        /// There is no selection to pass on: <see cref="ChatViewConfiguration.DisableTools"/>
        /// leaves the view without a picker, so <c>request.ActiveTools</c> /
        /// <c>request.ActiveAgents</c> always arrive empty and passing them through would run the
        /// turn with no capabilities at all.
        ///
        /// <para>Skills are passed through untouched: <c>null</c> means "not chosen", which is
        /// what lets the run fall back to the assistant's own skills.</para>
        /// </summary>
        private static Task<UID128> PostMessageAsync(ChatViewConfiguration.PostMessageRequest request)
        {
            return Mosaik.API.ChatAI.PostMessage(
                chatUID:           request.ActiveChat.UID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);
        }

        /// <summary>
        /// The same pinning for an edited or regenerated message. Overriding
        /// <see cref="PostMessageAsync"/> alone would leave the "Edit" and "Regenerate" commands
        /// re-running the turn without tools, which reads as the assistant having got worse.
        /// </summary>
        private static Task<UID128> ReplaceMessageAsync(ChatViewConfiguration.ReplaceMessageRequest request)
        {
            return Mosaik.API.ChatAI.ReplaceMessage(
                chatUID:           request.ActiveChat.UID,
                messageUID:        request.MessageUID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);
        }
    }
}
