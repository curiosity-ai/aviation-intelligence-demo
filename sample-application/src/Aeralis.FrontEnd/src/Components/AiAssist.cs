using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Tesserae;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;

namespace Aeralis.FrontEnd.Components
{
    /// <summary>
    /// Hands a question to the Aeralis Pre-Analysis assistant with the documents it is about
    /// already attached.
    ///
    /// Both AI entry points in this front-end are the same three steps — open a chat on the
    /// pre-analysis assistant, attach the nodes the question is about, post the question — and
    /// the value is entirely in step two. A question typed into a bare chat makes the assistant
    /// go looking for documents by name; a question asked with the documents *in context* lets
    /// it `consult` them by id, which is the difference between an answer about these documents
    /// and an answer about documents with similar titles.
    ///
    /// The assistant is named by UID rather than taken from the user's selected template. This
    /// is not the workspace's general assistant being asked a technical-data question: it is
    /// the pre-analysis assistant, which is the only one carrying the system prompt, the agents
    /// and the consult readers that make the answer worth reading.
    /// </summary>
    internal static class AiAssist
    {
        /// <summary>
        /// The Aeralis Pre-Analysis assistant. Its system prompt lives in
        /// <c>src/Aeralis.Definitions/aeralis-pre-analysis-assistant.md</c> and is imported by
        /// hand — assistant templates are not part of the definitions export.
        /// </summary>
        internal const string AssistantUID = "defaULtAiASSistaNt1111";

        /// <summary>
        /// Opens a new chat with <paramref name="contextUIDs"/> attached, posts
        /// <paramref name="question"/>, and navigates to it.
        ///
        /// The post is deliberately **not** awaited before navigating. Awaiting it would hold the
        /// user on the screen they came from until the whole answer had been generated; firing it
        /// first means the request is already with the server, so the chat opens onto an answer
        /// that is already being written.
        /// </summary>
        public static async Task AskAsync(string question, params UID128[] contextUIDs)
        {
            var uids = (contextUIDs ?? new UID128[0]).Where(u => u != UID128.Empty).Distinct().ToArray();

            if (uids.Length == 0)
            {
                Toast().Warning("Nothing to ask about".t(),
                                "None of the documents for this question could be resolved.".t());
                return;
            }

            var chat = await Mosaik.API.ChatAI.NewChatWithContext(
                App.InterfaceSettings.ChatAIProvider?.TaskUID,
                UID128.Parse(AssistantUID),
                uids);

            if (chat is null)
            {
                Toast().Warning("The assistant could not be opened".t(),
                                "Check that the Aeralis Pre-Analysis assistant is present in this workspace.".t());
                return;
            }

            Mosaik.API.ChatAI.PostMessage(chat.UID, question).FireAndForget();

            // The preview sheet is closed twice for the same reason the workspace's own
            // "Ask" command does it: a node opened from inside another node's preview leaves two
            // sheets stacked, and navigating with one still up drops the chat behind it.
            NodePreview.CloseAll();
            NodePreview.CloseAll();

            Router.Navigate(DefaultRoutes.ChatAI.OpenChat(chat.UID, null), reload: true);
        }

        /// <summary>
        /// The line every generated question ends with.
        ///
        /// The assistant owns which agent answers what, but a question that arrives through a
        /// button rather than through the composer has a known shape, so it says which agent it
        /// is for. That is a hint to the assistant, not a bypass of it — the assistant still
        /// invokes the agent, and still answers if the agent is unavailable.
        /// </summary>
        public static string UseAgent(string agentName)
            => "\n\nUse the " + agentName + " agent for this, and consult each document (and its "
             + "attached file) before answering.";
    }
}
