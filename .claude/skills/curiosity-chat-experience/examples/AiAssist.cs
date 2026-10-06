// Reference template: handing a question to the assistant with its evidence attached.
// See ../SKILL.md §5.
//
// This is the highest-value AI entry point in the application — higher than the chat page. Both
// AI buttons in this front end are the same three steps (open a chat on the assistant, attach
// the nodes the question is about, post the question) and THE VALUE IS ENTIRELY IN STEP TWO.
//
// A question typed into a bare chat makes the assistant go looking for records by name; the
// same question with the records IN CONTEXT lets it consult them by id — which is the
// difference between an answer about these records and an answer about records with similar
// titles.

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

namespace App.FrontEnd.Components
{
    internal static class AiAssist
    {
        /// <summary>
        /// The application's assistant. Its system prompt lives in the definitions folder and —
        /// where assistant templates are outside the definitions export — is imported by hand.
        /// This constant and that file are the two places its identity is written: change one,
        /// change the other.
        /// </summary>
        internal const string AssistantUID = "<assistant uid>";

        /// <summary>
        /// Opens a new chat with <paramref name="contextUIDs"/> attached, posts the question,
        /// and navigates to it.
        /// </summary>
        public static async Task AskAsync(string question, params UID128[] contextUIDs)
        {
            var uids = (contextUIDs ?? new UID128[0]).Where(u => u != UID128.Empty).Distinct().ToArray();

            if (uids.Length == 0)
            {
                Toast().Warning("Nothing to ask about".t(),
                                "None of the records for this question could be resolved.".t());
                return;
            }

            // Named by UID rather than taken from the user's selected template: this is not the
            // workspace's general assistant being asked a domain question, it is the one
            // carrying the system prompt, the agents and the consult readers that make the
            // answer worth reading.
            var chat = await Mosaik.API.ChatAI.NewChatWithContext(
                App.InterfaceSettings.ChatAIProvider?.TaskUID,
                UID128.Parse(AssistantUID),
                uids);

            if (chat is null)
            {
                Toast().Warning("The assistant could not be opened".t(),
                                "Check that the assistant is present in this workspace.".t());
                return;
            }

            // NOT awaited. Awaiting would hold the user on the screen they came from until the
            // whole answer had been generated; firing it first means the request is already
            // with the server, so the chat opens onto an answer that is already streaming.
            Mosaik.API.ChatAI.PostMessage(chat.UID, question).FireAndForget();

            // Closed TWICE for the same reason the workspace's own "Ask" command does it: a
            // record opened from inside another record's preview leaves two sheets stacked, and
            // navigating with one still up drops the chat behind it.
            NodePreview.CloseAll();
            NodePreview.CloseAll();

            Router.Navigate(DefaultRoutes.ChatAI.OpenChat(chat.UID, null), reload: true);
        }

        /// <summary>
        /// The line every generated question ends with.
        ///
        /// <para>The assistant owns which agent answers what, but a question arriving through a
        /// button rather than through the composer has a KNOWN SHAPE, so it says which agent it
        /// is for. That is a hint, not a bypass — the assistant still invokes the agent, and
        /// still answers if the agent is unavailable.</para>
        /// </summary>
        public static string UseAgent(string agentName)
            => "\n\nUse the " + agentName + " agent for this, and consult each record (and its "
             + "attached file) before answering.";

        // ---------------------------------------------------------------------------------
        // Example caller — note that the SUBJECT is attached, not only the candidates. A
        // verdict about a change the assistant cannot read is a guess.
        // ---------------------------------------------------------------------------------

        public static IComponent AskAboutImpactButton(UID128 subjectUID, UID128[] candidateUIDs)
            => Button("Ask about impact".t())
                .SetIcon(UIcons.Comment)
                .OnClick(() => AskAsync(
                    "Does this change impact these records? For each one: impacted or not, how "
                    + "badly, by what mechanism, and quote the evidence from each."
                    + UseAgent("Impact Analysis"),
                    new[] { subjectUID }.Concat(candidateUIDs).ToArray()).FireAndForget());
    }
}
