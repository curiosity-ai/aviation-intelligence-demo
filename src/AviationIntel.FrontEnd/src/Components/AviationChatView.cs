using System.Threading.Tasks;
using AviationIntel.FrontEnd.Schema;
using Mosaik;
using Mosaik.Components;
using Mosaik.Schema;
using Tesserae;
using UID;
using static TNT.T;

namespace AviationIntel.FrontEnd.Components
{
    /// <summary>
    /// The chat page: always the Aviation Market Intelligence assistant, always with the news desk,
    /// the workspace's search and reader, and the analyst agent. Installed with
    /// <c>settings.AssistantView</c>.
    /// </summary>
    /// <remarks>
    /// There is one assistant worth talking to here and its capabilities are the same on every turn,
    /// so neither picker is shown - a picker over that set could only be used to break it.
    /// </remarks>
    internal static class AviationChatView
    {
        // The capability set, restated here so an assistant edited in the admin pages to drop a tool
        // does not silently turn this page's answers into guesses. Search and consult are workspace
        // built-ins (stable UIDs across deployments); the rest are this application's definitions.

        /// <summary>Built-in: finds articles, feeds and entities that are not in the conversation.</summary>
        private static readonly UID128 Tool_Search  = UID128.Parse("LLMSearCh1111111111111");

        /// <summary>Built-in: reads one record by uid.</summary>
        private static readonly UID128 Tool_Consult = UID128.Parse("ConsuLt111111111111111");

        private static readonly UID128[] PinnedTools  = { AskAssistant.NewsDeskToolUID, Tool_Search, Tool_Consult };
        private static readonly UID128[] PinnedAgents = { AskAssistant.AnalystAgentUID };

        /// <summary>
        /// <c>nlp/pipelines/Aviation-Entities.AVpipeLinE111111111111.json</c> - the pipeline the article
        /// fields are parsed with. Parsing a reply through it turns every organisation, aircraft type,
        /// engine type and airport the assistant names into the node, opening beside the chat.
        /// </summary>
        private static readonly UID128 EntitiesPipeline = UID128.Parse("AVpipeLinE111111111111");

        /// <summary>
        /// Questions an empty chat can answer on its own, phrased the way an analyst asks them. The
        /// reports have their own buttons on the home page.
        /// </summary>
        private static readonly string[] WelcomePrompts =
        {
            "What were the most important aviation stories in the last 24 hours?".t(),
            "Which airlines, manufacturers and programmes were mentioned most this week?".t(),
            "What did regulators and authorities announce this week?".t(),
            "Summarise this week's news about Boeing 737 MAX production.".t(),
        };

        private static readonly ChatViewConfiguration Configuration = new ChatViewConfiguration
        {
            Title                    = "Aviation Market Intelligence".t(),
            WelcomeTitle             = "What do you want to know about the market?".t(),
            WelcomePrompts           = WelcomePrompts,
            ChatBoxPlaceholderText   = "Ask about an airline, a programme, a supplier or a regulator".t(),

            DisableAssistantSelector = true,
            DisableTools             = true,

            // A news answer is only as good as the articles behind it.
            ShowCitationsPanel       = true,

            // Linked as an article body: the text of a reply is prose about the market, like a body.
            ParseAs = new ChatViewParseAs
            {
                PipelineUID = EntitiesPipeline,
                NodeType    = N.Article.Type,
                FieldName   = N.Article.Body,
            },

            ChatRoute      = chatUID => DefaultRoutes.ChatAI.OpenChat(chatUID, default),

            NewChat        = NewChatAsync,
            PostMessage    = PostMessageAsync,
            ReplaceMessage = ReplaceMessageAsync,
        };

        // No Category: the chats the home page's report buttons open are uncategorised, and this page
        // has to list them too.

        public static IComponent Create(Parameters state) => new ChatView(Configuration, state);

        /// <summary>Every chat starts on this assistant; the provider (which model answers) stays the user's.</summary>
        private static Task<ChatMetadata> NewChatAsync(ChatViewConfiguration.NewChatRequest request)
            => Mosaik.API.ChatAI.NewChat(
                request.ProviderUID,
                AskAssistant.AssistantUID,
                request.Category);

        /// <summary>
        /// The pinned tools and agents rather than the composer's selection, which is always empty with
        /// the picker hidden. Skills pass through: null lets the assistant's own skills apply.
        /// </summary>
        private static Task<UID128> PostMessageAsync(ChatViewConfiguration.PostMessageRequest request)
            => Mosaik.API.ChatAI.PostMessage(
                chatUID:           request.ActiveChat.UID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                model:             request.Model,
                attachments:       request.AttachmentUIDs,
                cancellationToken: request.CancellationToken);

        /// <summary>The same pinning for edit and regenerate.</summary>
        private static Task<UID128> ReplaceMessageAsync(ChatViewConfiguration.ReplaceMessageRequest request)
            => Mosaik.API.ChatAI.ReplaceMessage(
                chatUID:           request.ActiveChat.UID,
                messageUID:        request.MessageUID,
                message:           request.Message,
                useTools:          PinnedTools,
                useAgents:         PinnedAgents,
                useSkills:         request.ActiveSkills,
                cancellationToken: request.CancellationToken);
    }
}
