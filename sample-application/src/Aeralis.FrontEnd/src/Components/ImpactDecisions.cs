using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Helpers;
using Tesserae;
using Aeralis.FrontEnd.Api;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace Aeralis.FrontEnd.Components
{
    /// <summary>
    /// What the engineer has decided about the candidates a work order surfaced, and the
    /// controls that let them decide.
    ///
    /// A pre-analysis is a sweep: dozens of suggestions, each of which is either a real impact
    /// or is not. Two things follow, and both are what this type exists for.
    ///
    /// **A rejection has to be recorded.** Before, only confirmation was — a dismissed
    /// candidate was simply one without a <c>ConfirmedImpact</c> edge, which is
    /// indistinguishable from one nobody has looked at. So the sweep could not be paused: on
    /// the next pass every rejected suggestion came back looking untouched. <c>RejectedImpact</c>
    /// is the other half, and the row says which it got.
    ///
    /// **The decision is taken about a selection, not a row.** The lists are multi-select, so
    /// the commands are registered with <c>ShowForMultiSelection</c> and the whole selection
    /// goes to the endpoint in one request.
    /// </summary>
    internal sealed class ImpactDecisions
    {
        private readonly UID128 _pawoUID;
        private readonly Task<UID128> _subjectMod;
        private readonly HashSet<string> _confirmed = new HashSet<string>();
        private readonly HashSet<string> _rejected = new HashSet<string>();

        private ImpactDecisions(UID128 pawoUID, Task<UID128> subjectMod)
        {
            _pawoUID = pawoUID;
            _subjectMod = subjectMod;
        }

        /// <summary>
        /// Loads both decision sets before any row is drawn. A row badged from a half-loaded
        /// answer is worse than one badged from none: it says "not reviewed" about something
        /// the engineer settled last week.
        /// </summary>
        public static async Task<ImpactDecisions> LoadAsync(UID128 pawoUID, Task<UID128> subjectMod)
        {
            var decisions = new ImpactDecisions(pawoUID, subjectMod);
            var response = await Api.Api.ImpactDecisionsAsync(pawoUID);

            foreach (var uid in response?.ConfirmedUids ?? new UID128[0]) decisions._confirmed.Add(uid.ToString());
            foreach (var uid in response?.RejectedUids ?? new UID128[0]) decisions._rejected.Add(uid.ToString());

            return decisions;
        }

        public int ConfirmedCount => _confirmed.Count;

        public int RejectedCount => _rejected.Count;

        private bool IsConfirmed(UID128 uid) => _confirmed.Contains(uid.ToString());

        private bool IsRejected(UID128 uid) => _rejected.Contains(uid.ToString());

        /// <summary>
        /// Dresses one candidate row: the decision already taken shows as a badge in its header,
        /// and the two commands are offered whether one row is selected or twenty.
        ///
        /// The badge is set rather than appended, so a row can only ever carry one of the two —
        /// which mirrors the edges, where confirming clears a rejection and the reverse.
        /// </summary>
        public OmniResult<Node> Customize(OmniResult<Node> result, System.Action onChanged)
        {
            var node = result.Result;
            var uid = node.UID;

            if (IsConfirmed(uid)) result.SetBadge(Badge("Confirmed impact".t()).Success());
            else if (IsRejected(uid)) result.SetBadge(Badge("Rejected".t()).Danger());

            return result.WithExtraCommands(
                Command("Confirm impact".t(), "c", UIcons.CheckCircle, node, ImpactDecision.Confirmed, onChanged),
                Command("Reject impact".t(), "r", UIcons.CircleXmark, node, ImpactDecision.Rejected, onChanged),
                Command("Clear decision".t(), "c+l", UIcons.Eraser, node, ImpactDecision.None, onChanged),
                AskAboutImpact(node));
        }

        /// <summary>
        /// Hands a selection of candidates to the assistant and asks whether the change under
        /// analysis actually touches them.
        ///
        /// This is the judgement the engineer makes dozens of times per work order, and the one
        /// the similarity engine cannot make: the engine says "these look related", and somebody
        /// still has to read both documents and decide whether the change breaks one. The command
        /// puts the modification and the candidates in one conversation, with their published
        /// PDFs attached, and asks that question directly.
        ///
        /// It is on the **action bar** and multi-select because it is worth more over a selection
        /// than over a row: asking about eight candidates at once lets the assistant rank them
        /// against each other, which is exactly what an engineer working through a page wants.
        /// </summary>
        private CommandDefinition AskAboutImpact(Node node)
        {
            return new CommandDefinition("Ask about impact".t(), "a+i", UIcons.Sparkles,
                                         () => AskAsync(new[] { node }).FireAndForget())
                .ShowOnActionBar()
                .IsAICommand()
                .ShowForMultiSelection("aeralis-ask-impact", node, nodes => AskAsync(nodes).FireAndForget());
        }

        /// <summary>
        /// Builds the question. The candidates are named by identifier and type so the assistant
        /// can match them against what <c>consult</c> returns, and the modification under analysis
        /// goes into the context alongside them — without it the assistant would be asked to judge
        /// an impact against a change it cannot read.
        /// </summary>
        private async Task AskAsync(Node[] nodes)
        {
            var candidates = nodes.Where(n => n is object).ToArray();
            if (candidates.Length == 0) return;

            var modUID = await _subjectMod;

            var lines = candidates
                .Select(n => "- " + Describe(n))
                .ToArray();

            var subject = modUID == UID128.Empty
                ? "the modification this work order analyses"
                : "the modification attached to this conversation";

            var question =
                "I am running an Aeralis MOD pre-analysis and I need to know whether "
                + subject + " actually impacts the following document(s):\n\n"
                + string.Join("\n", lines) + "\n\n"
                + "Everything named above is attached to this conversation, together with the "
                + "modification itself and the published PDFs. For each document, tell me whether it is "
                + "impacted, why, and how severely — a shared part number, FIN, drawing or zone; an "
                + "effectivity or embodiment condition that no longer holds; a repair or manual procedure "
                + "the change invalidates; or no impact at all. Order them so the ones I have to act on "
                + "come first, and say plainly where you are not sure."
                + AiAssist.UseAgent("Aeralis Pre-Analysis");

            var context = candidates.Select(n => n.UID).ToList();
            if (modUID != UID128.Empty) context.Insert(0, modUID);

            await AiAssist.AskAsync(question, context.ToArray());
        }

        /// <summary>
        /// One candidate, named the way the graph names it. The label comes from the workspace's
        /// own <c>Labels.Get</c>, so it is whatever that document type's renderer would show.
        /// </summary>
        private static string Describe(Node node)
        {
            var label = Labels.Get(node);

            return string.IsNullOrEmpty(label)
                ? node.Type + " " + node.UID
                : node.Type + " — " + label;
        }

        /// <summary>
        /// One command, offered for the row it was built from and for any multi-selection that
        /// includes it. <c>ShowForMultiSelection</c>'s unique id is what lets the command bar
        /// collapse the same command across every selected row into one entry — without it the
        /// selection would offer twenty identical "Confirm impact" commands.
        /// </summary>
        private CommandDefinition Command(string name, string shortCode, UIcons icon, Node node,
                                          string decision, System.Action onChanged)
        {
            return new CommandDefinition(name, shortCode, icon, () => ApplyAsync(new[] { node }, decision, onChanged).FireAndForget())
                          .ShowOnActionBar()
                          .ShowForMultiSelection("aeralis-impact-" + decision.ToLowerInvariant(), node, nodes => ApplyAsync(nodes, decision, onChanged).FireAndForget());
        }

        /// <summary>
        /// Sends the whole selection in one request, then refreshes what the caller drew from
        /// these sets. The local sets are updated from the same values that were sent rather
        /// than re-read, so the badges change with the click instead of a round trip later.
        /// </summary>
        private async Task ApplyAsync(Node[] nodes, string decision, System.Action onChanged)
        {
            var uids = nodes.Where(n => n is object).Select(n => n.UID).ToArray();
            if (uids.Length == 0) return;

            var result = await Api.Api.SetImpactDecisionAsync(_pawoUID, uids, decision);

            if (result is object && !result.Success)
            {
                Toast().Warning("Nothing was recorded".t(),
                                "None of the selected documents can carry an impact decision.".t());
                return;
            }

            foreach (var uid in uids)
            {
                var key = uid.ToString();
                _confirmed.Remove(key);
                _rejected.Remove(key);

                if (decision == ImpactDecision.Confirmed) _confirmed.Add(key);
                else if (decision == ImpactDecision.Rejected) _rejected.Add(key);
            }

            Toast().Success(Verb(decision), string.Format("{0} document(s).".t(), uids.Length));

            onChanged?.Invoke();
        }

        private static string Verb(string decision)
            => decision == ImpactDecision.Confirmed ? "Confirmed as impacted".t()
             : decision == ImpactDecision.Rejected ? "Rejected".t()
             : "Decision cleared".t();
    }
}
