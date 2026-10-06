using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mosaik;
using Mosaik.Components;
using Mosaik.Components.Nodes;
using Mosaik.Schema;
using Tesserae;
using Aeralis.FrontEnd.Api;
using Aeralis.FrontEnd.Schema;
using TNT;
using UID;
using static Tesserae.UI;
using static Mosaik.UI;
using static TNT.T;
using Node = Mosaik.Schema.Node;

namespace Aeralis.FrontEnd.Renderers
{
    /// <summary>
    /// The pre-analysis work order — the screen this whole demo exists for.
    ///
    /// Six tabs instead of the shared three: the overview the engineer signs off, the
    /// similarity-driven analysis where impacts are found and decided, the lifecycle and its
    /// sources, the rules and the IPC context, an issue comparison, and the 360 view — which is
    /// centred on the **modification under analysis**, not on the work order, because that is
    /// the thing whose neighbourhood matters.
    ///
    /// Everything sent to an endpoint from here is a UID, and everything that comes back is one.
    /// The renderer already holds the work order's node, so the endpoints are never asked to
    /// re-derive it from <c>DocId</c>; and a response of UIDs goes straight into
    /// <see cref="NeighborsLinks"/> or a <see cref="SearchArea"/> restricted with
    /// <c>WithTargetUIDs</c>, which renders each one with its own registered renderer rather
    /// than with a title copied through the wire.
    /// </summary>
    internal sealed class PreAnalysisWorkOrderRenderer : AeralisRendererBase
    {
        public PreAnalysisWorkOrderRenderer() : base(new SchemaStyleInfo
        {
            Name = N.PreAnalysisWorkOrder.Type,
            DisplayName = "PAWO",
            Icon = UIcons.ClipboardList.ToString(),
            LabelField = LabelFields.IdAndTitle(N.PreAnalysisWorkOrder.DocId, N.PreAnalysisWorkOrder.Title),
        },
        N.PreAnalysisWorkOrder.Abstract,
        N.PreAnalysisWorkOrder.PawoStatus, N.PreAnalysisWorkOrder.WorkflowStatus,
        N.PreAnalysisWorkOrder.Recommendation, N.PreAnalysisWorkOrder.Ata6d,
        N.PreAnalysisWorkOrder.ProgramLetter) { }

        private static string Property(Node node, string name)
            => GetValueForProperty(node, name) ?? "";

        /// <summary>
        /// The modification this order analyses, by edge. The <c>SubjectMod</c> property is still
        /// there as a label, but the relationship is what the graph holds and it is the only one
        /// of the two that survives a document being re-identified.
        /// </summary>
        private static async Task<UID128> SubjectModAsync(UID128 pawoUID)
        {
            var mods = await Mosaik.API.Query.StartAt(pawoUID)
                .Out(N.Modification.Type, E.PreAnalysisOf)
                .TakeAll()
                .GetUIDsAsync();

            return (mods?.UIDs is object && mods.UIDs.Length > 0) ? mods.UIDs[0] : UID128.Empty;
        }

        protected override IComponent CreateCompactViewFor(Node node)
        {
            var value = GetValueForProperty(node, N.PreAnalysisWorkOrder.Abstract);
            if (value is null) return null;

            return TextBlock(value).Tiny().Secondary();
        }

        protected override IEnumerable<NodeTab> BuildTabs(Node node, IReadOnlyList<RelationshipGroup> groups, NodeView nodeView)
        {
            var n = nodeView.Node;

            yield return Tab("summary", "Overview", () => RenderOverview(n), scrolls: false);
            yield return Tab("analysis", "Analysis", () => RenderAnalysis(n), scrolls: false);
            yield return Tab("history", "History & Sources", () => RenderHistory(n));
            yield return Tab("rules", "Rules & IPC", () => RenderRulesAndIpc(n), scrolls: false);
            yield return Tab("issues", "Issue Comparison", () => RenderIssueComparison(n), scrolls: false);

            // The work order's *own* published PDF. Everything above shows the modification's
            // dossier — the thing being analysed — and this is the analysis as it was issued.
            yield return PublishedFileTab(n.UID);

            yield return new NodeTab
            {
                Id = "view360",
                Label = "360 View".t(),
                Scrolls = false,
                TabTitle = SegmentTitle("360 View".t()),
                // Centred on the subject modification: the work order itself has few
                // neighbours, the change it analyses has all of them.
                TabContent = () => Components.View360.ForSubjectOf(n.UID, N.All360Types),
            };

            yield return new NodeTab
            {
                Id = "graph",
                Label = "Graph".t(),
                Scrolls = false,
                TabTitle = SegmentTitle("Graph".t()),
                TabContent = () => RenderGraphSegment(n, nodeView.OpenGraphNodePeek),
            };
        }

        private NodeTab Tab(string id, string label, System.Func<IComponent> content, bool scrolls = true)
            => new NodeTab
            {
                Id = id,
                Label = label.t(),
                Scrolls = scrolls,
                TabTitle = SegmentTitle(label.t()),
                TabContent = content,
            };

        // ---------------------------------------------------------------------
        // 1. Overview
        // ---------------------------------------------------------------------

        /// <summary>
        /// What this work order covers, against the change it analyses.
        ///
        /// **Left is the analysis as it stands**: the ATA coverage — what has been confirmed and
        /// what similarity says is still missing, in one table rather than two cards saying
        /// overlapping things — the confirmed impacts themselves, and the aircraft the work order
        /// applies to. **Right is the change**, and only the change: the published dossier of the
        /// modification under analysis, at full height, because every judgement on this screen is
        /// "does this touch *that*" and that is the document you have to be able to read.
        ///
        /// The written report used to live at the bottom of the left column. It is an output, not
        /// something read while deciding coverage, and it made the column scroll past the two
        /// things that are.
        /// </summary>
        private IComponent RenderOverview(Node node)
        {
            var pawoUID = node.UID;

            var left = VStack().WS().Children(
                Card(Defer(() => RenderCoverageAsync(pawoUID)))
                    .SetTitle("ATA coverage".t(), UIcons.Table, Theme.Colors.Teal600).WS().MB(12),

                Card(Defer(async () =>
                {
                    var confirmed = await Api.Api.ConfirmedImpactsAsync(pawoUID);
                    var uids = confirmed?.Uids ?? new UID128[0];

                    // The confirmed documents render as a real result list rather than a grid of
                    // copied titles: each row is whatever its own renderer makes of it, and stays
                    // right when the document is re-titled.
                    return SearchArea()
                        .NoSearchBox()
                        .Renderer(r => r.WithNoResultsMessage(_ => NothingConfirmedYet()))
                        .SearchFor(new SearchRequest()
                            .WithTargetUIDs(uids)
                            .MarkAsNoResultsIf(uids.Length == 0))
                        .S() ;
                }).H(260)).SetTitle("Confirmed impacts".t(), UIcons.CheckCircle, Theme.Colors.Green600).WS().MB(12),

                Card(Applicability(node.UID))
                    .SetTitle("Applicability".t(), UIcons.PlaneTail, Theme.Colors.Blue600).WS());

            var right = Card(RenderModificationDossier(node))
                .SetTitle("Modification under analysis".t(), UIcons.PencilRuler, Theme.Colors.Purple600)
                .S();

            return SplitView().Left(left.ScrollY()).Right(right).S();
        }

        /// <summary>
        /// What an empty confirmed-impacts list should say.
        ///
        /// A work order with nothing confirmed is the *normal* opening state, not a failure —
        /// every pre-analysis starts there — so the default "no results" reads as though
        /// something went wrong. It says what the emptiness means and where the work is done
        /// instead, and it is centred in the card rather than left-aligned at the top, because
        /// an empty state is the only thing in the box and should look deliberate.
        /// </summary>
        private static IComponent NothingConfirmedYet()
        {
            return VStack().S().AlignItemsCenter().JustifyContent(ItemJustify.Center).Children(
                Message("Nothing confirmed yet".t(),
                        "Impacts you confirm in the Analysis tab are collected here.".t())
                    .Icon(UIcons.CheckCircle, Theme.Colors.Green600)
                    .Variant(MessageVariant.Default));
        }

        /// <summary>
        /// Which aircraft this work order applies to, one labelled row per dimension.
        ///
        /// This was two bare <c>NeighborsLinks</c> calls, each asking for several node types at
        /// once and for no particular edge. That form sends the node types to the server as one
        /// <c>";"</c>-joined string with no edge to disambiguate them, and it
        /// gave a card with no labels: when it returned nothing there was nothing to see, and
        /// nothing to say which of the three dimensions was empty either.
        ///
        /// Each dimension is now its own row, asked for over the edge that actually carries it —
        /// <c>AppliesToType</c> and <c>AppliesToModel</c> are different facts and a
        /// programme is reached over <c>InProgram</c>, not over an applicability edge at all —
        /// and every row says "—" when it is genuinely empty rather than collapsing. Most work
        /// orders are scoped by type and model with no MSN list at all, so an empty MSN row is
        /// the normal case and has to read as one.
        /// </summary>
        private static IComponent Applicability(UID128 pawoUID)
        {
            return DetailsGrid(columns: 2).WS().Stacked()
                .Row("Programme".t(), Neighbours(pawoUID, N.Program.Type, E.InProgram, 4))
                .Row("Aircraft types".t(), Neighbours(pawoUID, N.AircraftType.Type, E.AppliesToType, 8))
                .Row("Models".t(), Neighbours(pawoUID, N.AircraftModel.Type, E.AppliesToModel, 12))
                .Row("MSNs".t(), Neighbours(pawoUID, N.Msn.Type, E.AppliesToMsn, 20));
        }

        private static IComponent Neighbours(UID128 uid, string nodeType, string edgeType, int max)
            => NeighborsLinks(uid, nodeType, edgeType, wrap: true, maxItemsToShow: max,
                              showIfNone: () => TextBlock("—".t()).Secondary());

        /// <summary>
        /// One table answering one question: for each ATA chapter, what has been found impacted
        /// and what does similarity still suggest.
        ///
        /// These were two cards. The impact matrix listed chapters with counts per document type;
        /// the suggested-ATA card listed chapters with scores, and put a banner on top counting
        /// the ones the matrix did not cover. The reader had to hold one card in their head while
        /// reading the other to answer the only question either is for — *which chapter have I not
        /// looked at* — and the banner was a third rendering of a fact the rows could state
        /// themselves.
        ///
        /// So it is one row per chapter, carrying both sides, and the chapters similarity ranks
        /// but the matrix does not cover sort to the top and carry the badge. Nothing counts them
        /// in a sentence: they are at the top of the table, which is where an unanswered thing
        /// belongs.
        /// </summary>
        private static async Task<IComponent> RenderCoverageAsync(UID128 pawoUID)
        {
            var matrixTask = Api.Api.ImpactMatrixAsync(pawoUID);
            var suggestedTask = Api.Api.SuggestedAtaAsync(pawoUID);

            var matrix = await matrixTask;
            var suggested = await suggestedTask;

            var cells = matrix?.Cells ?? new ImpactMatrixCellDto[0];
            var chapters = suggested?.Chapters ?? new SuggestedAtaDto[0];

            // Keyed by the UID's string form: UID128 is a JS string at run time, so this is the
            // identity the two responses actually share.
            var covered = new Dictionary<string, string>();

            foreach (var ataUID in matrix?.AtaUIDs ?? new UID128[0])
            {
                var counts = cells
                    .Where(c => c.AtaUID == ataUID && c.Count > 0)
                    .Select(c => c.DocType + " " + c.Count)
                    .ToArray();

                covered[ataUID.ToString()] = counts.Length > 0 ? string.Join("   ", counts) : "";
            }

            var scores = new Dictionary<string, SuggestedAtaDto>();
            foreach (var chapter in chapters) scores[chapter.AtaUID.ToString()] = chapter;

            // Suggested-but-uncovered first, then the covered chapters, then the rest. The order
            // is the message: what is at the top is what has not been answered.
            var order = chapters.Where(c => c.IsNew).Select(c => c.AtaUID)
                .Concat((matrix?.AtaUIDs ?? new UID128[0]).Where(u => !scores.ContainsKey(u.ToString()) || !scores[u.ToString()].IsNew))
                .Concat(chapters.Where(c => !c.IsNew).Select(c => c.AtaUID))
                .ToArray();

            var seen = new HashSet<string>();
            var body = VStack().WS();
            var rows = 0;

            foreach (var ataUID in order)
            {
                if (!seen.Add(ataUID.ToString())) continue;

                var key = ataUID.ToString();
                var impacted = covered.ContainsKey(key) ? covered[key] : "";
                var score = scores.ContainsKey(key) ? scores[key] : null;

                body.Add(HStack().WS().AlignItemsCenter().PB(4).Children(
                    // The row header is the chapter itself, so it carries the workspace's ATA
                    // label and opens the chapter — a code string would do neither.
                    NeighborsLinks(new[] { ataUID }, maxItemsToShow: 1).W(200),
                    TextBlock(impacted.Length > 0 ? impacted : "nothing confirmed".t())
                        .Small().Secondary().W(10).Grow(),
                    score is object
                        ? TextBlock(score.Score.ToString("n2")).Tiny().Secondary().PR(8) 
                        : Empty(),
                    score is object && score.IsNew
                        ? Badge("suggested".t()).Warning() 
                        : Empty()));

                rows++;
            }

            if (rows == 0) return TextBlock("Nothing impacted or suggested yet.".t()).Secondary();

            return body;
        }

        /// <summary>
        /// The modification's published dossier — the PDF the connector rendered and hung off the
        /// modification as a <c>_FileEntry</c> — as the real viewer, with the way through to the
        /// modification itself above it.
        ///
        /// Returns the contents of a card rather than a card of its own: it is the whole
        /// right-hand column of the overview, so it stretches to whatever that column is.
        /// </summary>
        private IComponent RenderModificationDossier(Node node)
        {
            return Defer(async () =>
            {
                var modUID = await SubjectModAsync(node.UID);
                if (modUID == UID128.Empty)
                    return TextBlock("This work order analyses no modification.".t()).Secondary() ;

                var open = Button("Open the modification".t())
                    .SetIcon(UIcons.ArrowUpRightFromSquare)
                    .Small()
                    .OnClick((b, _) => Router.Navigate(DefaultRoutes.Node(modUID)));

                var dossier = await AttachedFileBodyAsync(modUID);

                if (dossier is null)
                {
                    return VStack().WS().Children(
                        TextBlock("No published document is attached to this modification yet.".t()).Secondary(),
                        open.MT(12)) ;
                }

                return VStack().S().Children(
                    HStack().WS().Children(open).PB(8),
                    dossier.S().Grow()) ;
            }).S();
        }

        // ---------------------------------------------------------------------
        // 2. Analysis
        // ---------------------------------------------------------------------

        /// <summary>
        /// The two candidate lists, side by side rather than one behind the other.
        ///
        /// They answer different halves of the same question — *what does this change touch* and
        /// *who has answered a change like it before* — and the second is only useful while the
        /// first is on screen: a previous analysis is worth opening precisely when it covers a
        /// document the current list has just surfaced.
        ///
        /// Both are multi-select and both carry the confirm / reject commands, because a
        /// previous analysis surfaces the same documents and the engineer's answer about one of
        /// them is the same answer wherever they met it.
        /// </summary>
        private IComponent RenderAnalysis(Node node)
        {
            var pawoUID = node.UID;

            // Started once and handed to both lists: they exclude the same two nodes, and one
            // lookup is one answer rather than two that could differ.
            var subjectMod = SubjectModAsync(pawoUID);

            var left = Card(Components.SimilarityList
                    .For(pawoUID, Endpoints.SimilarityAffectedDocuments, subjectMod).S())
                .SetTitle("Possible affected documents".t(), UIcons.SearchAlt, Theme.Colors.Blue600).S();

            var right = Card(Components.SimilarityList
                    .For(pawoUID, Endpoints.SimilarityPreviousAnalysis, subjectMod).S())
                .SetTitle("Similar previous analysis".t(), UIcons.ClipboardListCheck, Theme.Colors.Purple600).S();

            return SplitView().Left(left).Right(right).S();
        }

        // ---------------------------------------------------------------------
        // 3. History & Sources
        // ---------------------------------------------------------------------

        private IComponent RenderHistory(Node node)
        {
            var pawoId = Property(node, N.PreAnalysisWorkOrder.DocId);
            var opened = Property(node, N.PreAnalysisWorkOrder.OpenedDate);

            var plan = Plan("MOD / pre-analysis lifecycle".t())
                .Progress(4, 6);

            // Each step of the lifecycle is the node it happened on, so the row links to the
            // internal order and the modification instead of naming them.
            var steps = VStack().WS().Children(
                Step("IO raised".t(),
                     NeighborsLinks(node.UID, N.InternalOrder.Type, E.FromOrder,
                                    showIfNone: () => TextBlock("— raised internally".t()).Secondary())),
                Step("MOD created".t(),
                     NeighborsLinks(node.UID, N.Modification.Type, E.PreAnalysisOf)),
                Step("PAWO opened".t(),
                     TextBlock(pawoId + (string.IsNullOrEmpty(opened) ? "" : "   " + opened))),
                Step("Pre-Analysis".t(), TextBlock("Active — current step".t())),
                Step("WO created".t(), TextBlock("pending".t()).Secondary()),
                Step("Embodiment (MPD/SB)".t(), TextBlock("pending".t()).Secondary()));

            var left = VStack().WS().Children(
                Card(VStack().WS().Children(plan, steps)).SetTitle("Lifecycle".t()).WS());

            var right = VStack().WS().Children(
                Card(NeighborsLinks(node.UID, new[] { N.PartNumber.Type }, wrap: true, maxItemsToShow: 20))
                    .SetTitle("Part numbers".t()).WS(),
                Card(NeighborsLinks(node.UID, new[] { N.Fin.Type }, wrap: true, maxItemsToShow: 20))
                    .SetTitle("FINs".t()).WS(),
                Card(NeighborsLinks(node.UID, new[] { N.Drawing.Type }, wrap: true, maxItemsToShow: 20))
                    .SetTitle("Drawings".t()).WS(),
                Card(NeighborsLinks(node.UID,
                                    new[] { N.AmmTask.Type, N.IpcFigure.Type, N.SrmRepair.Type, N.WdmSheet.Type },
                                    wrap: true, maxItemsToShow: 20))
                    .SetTitle("Manual content".t()).WS());

            return SplitView().Left(left).Right(right).S();
        }

        private static IComponent Step(string label, IComponent value)
            => HStack().WS().AlignItemsCenter().Children(
                TextBlock(label).SemiBold().W(180),
                value);

        // ---------------------------------------------------------------------
        // 4. Rules & IPC
        // ---------------------------------------------------------------------

        /// <summary>
        /// The rules that apply, and the parts context to read them against.
        ///
        /// Rules are on the left because they are the work: each one is a question the engineer
        /// answers by proposing it or not. The right is what they answer it *with* — the IPC
        /// figures, part numbers, zones and FINs this work order touches — and it is one
        /// <see cref="SearchArea"/> now rather than two cards of link chips.
        ///
        /// The chips were the wrong shape twice over. They could not be searched, so finding a
        /// part among forty meant reading forty; and "IPC figures affected" was a separate card
        /// from "related objects", which also listed part numbers and zones, so the same
        /// neighbourhood was drawn twice in two different ways. As a faceted search area the
        /// reader can filter by type, search by identifier, and open any of them with its own
        /// renderer.
        /// </summary>
        private IComponent RenderRulesAndIpc(Node node)
        {
            var pawoUID = node.UID;

            var left = Card(Defer(async () =>
            {
                var rules = await Api.Api.ApplicableRulesAsync(pawoUID);
                var list = rules?.Rules ?? new RuleDto[0];

                if (list.Length == 0)
                    return TextBlock("No applicable rules found.".t()).Secondary() ;

                var body = VStack().WS();

                foreach (var rule in list)
                {
                    var propose = SaveButton().Configure(save: "Propose".t(), saved: "Proposed".t());
                    if (rule.AlreadyProposed) propose.Saved();

                    var captured = rule;
                    propose.OnClickSpinWhile(async () =>
                    {
                        await Api.Api.ProposeRuleAsync(pawoUID, captured.Uid, "");
                        propose.Saved();
                    });

                    body.Add(Card(VStack().WS().Children(
                        HStack().WS().Children(
                            TextBlock(captured.Title).SemiBold(),
                            Badge(captured.Severity)),
                        TextBlock(captured.Text).Secondary(),
                        TextBlock(captured.Source).Tiny().Secondary(),
                        propose)).WS().MB(8));
                }

                return body;
            }).S()).SetTitle("Applicable rules".t(), UIcons.ShieldCheck, Theme.Colors.Orange500).S();

            var right = Card(RelatedIpc(pawoUID))
                .SetTitle("Related IPC".t(), UIcons.Boxes, Theme.Colors.Teal600).S();

            return SplitView().Left(left).Right(right).S();
        }

        /// <summary>
        /// Everything this work order touches that a parts catalogue would hold: the IPC figures
        /// themselves, and the part numbers, zones and FINs they are made of. One query, so the
        /// facets can split it back apart by type on demand.
        /// </summary>
        private static IComponent RelatedIpc(UID128 pawoUID)
        {
            return SearchArea()
                .WithFacets()
                .OnSearch(sr => sr.WithTargetQuery(
                    Mosaik.API.Query.StartAt(pawoUID)
                        .Out(nodeTypes: new[]
                        {
                            N.IpcFigure.Type, N.IpcItem.Type, N.PartNumber.Type,
                            N.Zone.Type, N.Fin.Type,
                        })
                        .TakeAll()))
                .S();
        }

        // ---------------------------------------------------------------------
        // 5. Issue Comparison
        // ---------------------------------------------------------------------

        /// <summary>
        /// The modification as it stands, beside the issue before it.
        ///
        /// This was a dropdown of hard-coded "Revision A / B / C" and a line of text that nothing
        /// ever changed — a promise the data could already keep. The graph holds one
        /// <c>ModificationIssue</c> node per issue, chained by <c>SupersedesIssue</c>, each with
        /// the PDF that was published for it, so both sides are real documents now: the card the
        /// workspace draws for the node, and under it the whole published file.
        ///
        /// The picker defaults to the newest issue that is not the current one, because "what
        /// changed" is the question this tab is opened with, and answering it needs no clicks.
        /// </summary>
        private IComponent RenderIssueComparison(Node node)
        {
            return Defer(async () =>
            {
                var modUID = await SubjectModAsync(node.UID);

                if (modUID == UID128.Empty)
                    return TextBlock("This work order analyses no modification.".t()).Secondary() ;

                var issues = await Api.Api.ModIssuesAsync(modUID);
                var list = issues?.Issues ?? new IssueDto[0];

                var previousPane = VStack().S();

                // Which previous issue is on the right is a choice the picker makes and the
                // Compare button reads, so it is held in one place both can reach rather than
                // in the picker's own closure.
                var selected = new SelectedIssue();

                var left = Card(VStack().S().Children(
                        NodeCard(modUID),
                        Defer(async () => await AttachedFileBodyAsync(modUID) ?? NoFile()).S().Grow()))
                    .SetTitle(CurrentHeader(modUID, selected))
                    .S();

                var right = Card(previousPane)
                    .SetTitle(PreviousHeader(list, previousPane, selected))
                    .S();

                return SplitView().Left(left).Right(right).S() ;
            }).S();
        }

        /// <summary>Which previous issue the right pane is showing, shared with the Compare button.</summary>
        private sealed class SelectedIssue
        {
            public IssueDto Value { get; set; }
        }

        /// <summary>
        /// The current issue's header, with the button that asks the assistant what changed.
        ///
        /// Comparing two issues of a dossier is reading two PDFs side by side and holding the
        /// differences in your head — which is exactly the work a model does better than a
        /// person, and exactly the work this screen otherwise leaves the engineer to do
        /// unaided. The button hands both documents to the assistant *as context*, so it can
        /// consult them by id rather than hunting for them by title, and asks the question the
        /// tab exists to answer.
        ///
        /// It sits on the current issue's side because the comparison is always "what changed to
        /// get here": the current issue is the subject, the one on the right is what it is being
        /// compared against.
        /// </summary>
        private IComponent CurrentHeader(UID128 modUID, SelectedIssue selected)
        {
            var compare = Button("Compare".t())
                .SetIcon(UIcons.Sparkles)
                .Small()
                .Primary()
                .Tooltip("Ask the assistant to explain what changed between these two issues".t())
                .OnClickSpinWhile(() => CompareAsync(modUID, selected));

            return HStack().WS().AlignItemsCenter().Children(
                IconTile(UIcons.PencilRuler, Theme.Colors.Purple600).Size(28.px()),
                TextBlock("Current issue".t()).SemiBold().PL(8).Grow(),
                compare);
        }

        /// <summary>
        /// Builds the question and opens the chat.
        ///
        /// The identifiers are read off the nodes rather than passed down from the picker, so the
        /// question names the documents the way the graph does — the assistant sees the same
        /// <c>MOD-166204</c> and <c>MOD-166204:B</c> the engineer is looking at, and can match
        /// them against what <c>consult</c> returns.
        /// </summary>
        private static async Task CompareAsync(UID128 modUID, SelectedIssue selected)
        {
            var issue = selected.Value;

            if (issue is null)
            {
                Toast().Warning("Nothing to compare with".t(),
                                "This modification has only ever had one issue.".t());
                return;
            }

            var mod = await Mosaik.API.Nodes.GetAsync(modUID);

            var modId = mod is object ? GetValueForProperty(mod, N.Modification.DocId) : null;
            var modTitle = mod is object ? GetValueForProperty(mod, N.Modification.Title) : null;
            var current = string.IsNullOrEmpty(modId) ? "the current issue" : modId;

            var question =
                "I am comparing two issues of the same Aeralis modification dossier and I need to know what "
                + "changed between them.\n\n"
                + "**Current issue** — " + current
                + (string.IsNullOrEmpty(modTitle) ? "" : ": " + modTitle) + ".\n"
                + "**Previous issue** — " + (string.IsNullOrEmpty(issue.IssueId) ? issue.Uid.ToString() : issue.IssueId)
                + (string.IsNullOrEmpty(issue.IssueLetter) ? "" : " (issue " + issue.IssueLetter + ")")
                + (string.IsNullOrEmpty(issue.IssueReason) ? "" : ", raised because: " + issue.IssueReason)
                + ".\n\n"
                + "Both documents are attached to this conversation, each with its published PDF. "
                + "Explain the differences between them: what changed in the reason for change, the "
                + "description, the effects and the embodiment note; whether the applicability or the "
                + "embodiment status moved; and which of those changes an engineer running a pre-analysis "
                + "would have to act on."
                + Components.AiAssist.UseAgent("Aeralis Document Comparison");

            await Components.AiAssist.AskAsync(question, modUID, issue.Uid);
        }

        /// <summary>
        /// The right pane's header carries its own picker, because which issue is on that side is
        /// the only thing about the pane that is a choice.
        ///
        /// The items are handed to the dropdown in **one** call. <c>Dropdown.Items</c> clears what
        /// is already there before adding, so adding them one at
        /// a time inside a loop leaves a dropdown holding only the last one — which is exactly
        /// what the dashboard's engineer picker used to do.
        /// </summary>
        private IComponent PreviousHeader(IssueDto[] issues, Stack pane, SelectedIssue selected)
        {
            var previous = issues.Where(i => !i.IsLatest).ToArray();

            var title = HStack().WS().AlignItemsCenter().Children(
                IconTile(UIcons.Rewind, Theme.Colors.Blue600).Size(28.px()),
                TextBlock("Previous issue".t()).SemiBold().PL(8).Grow());

            if (previous.Length == 0)
            {
                pane.Add(TextBlock("This modification has only ever had one issue.".t()).Secondary());
                return title;
            }

            var picker = Tesserae.UI.Dropdown().Width(220.px());

            var items = previous.Select((issue, index) =>
            {
                var captured = issue;

                return Tesserae.UI.DropdownItem(Label(captured))
                    .SelectedIf(index == 0)
                    .OnSelected(_ => ShowIssue(pane, captured, selected));
            }).ToArray();

            picker.Items(items);

            // The newest previous issue, shown without waiting to be asked: "what changed since
            // the last issue" is the question this tab exists for.
            ShowIssue(pane, previous[0], selected);

            title.Add(picker);

            return title;
        }

        private static string Label(IssueDto issue)
        {
            var letter = string.IsNullOrEmpty(issue.IssueLetter) ? issue.IssueId : "Issue " + issue.IssueLetter;

            return string.IsNullOrEmpty(issue.Status) ? letter : letter + " · " + issue.Status;
        }

        private void ShowIssue(Stack pane, IssueDto issue, SelectedIssue selected)
        {
            selected.Value = issue;
            pane.Clear();

            pane.Add(NodeCard(issue.Uid));

            if (!string.IsNullOrEmpty(issue.IssueReason))
                pane.Add(TextBlock(issue.IssueReason).Small().Secondary().PB(8));

            pane.Add(Defer(async () => await AttachedFileBodyAsync(issue.Uid) ?? NoFile(isDraft: true)).S().Grow());
        }

        /// <summary>
        /// One node drawn as the card its own renderer makes of it. A search area restricted to a
        /// single UID rather than a hand-built header: the card then carries the type's icon, its
        /// label, its footer and its commands, and keeps carrying them when the renderer changes.
        /// </summary>
        private static IComponent NodeCard(UID128 uid)
        {
            return SearchArea()
                .NoSearchBox()
                .SearchFor(new SearchRequest().WithTargetUIDs(new[] { uid }))
                .WS()
                .H(NODE_CARD_HEIGHT);
        }

        private static IComponent NoFile(bool isDraft = false)
            => TextBlock(isDraft ? "No draft document is attached to this modification yet.".t() : "No published document is attached to this modification issue.".t()).Secondary();

        private const int NODE_CARD_HEIGHT = 150;
    }
}
