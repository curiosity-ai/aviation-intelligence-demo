using System.Diagnostics;
using Curiosity.Library;
using Aeralis.Attachments;
using Aeralis.Dataset;
using Aeralis.Dataset.Documents;

namespace Aeralis.Connector.Ingest;

/// <summary>
/// Renders every document to the file a technical data system would hold for it, uploads it as
/// an attachment of the document's node, and keeps what it was rendered from.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is JSON. What an author, a reviewer or a search result actually wants to open is
/// the published document — the maintenance task as it is printed, the service bulletin as it
/// is issued, the parts figure as the manual page it is on. <see cref="AttachmentBuilder"/>
/// produces those; this handler puts them in the graph.
/// </para>
/// <para>
/// It keeps the input as well as the output. Every publication is rendered from an S1000D CSDB
/// object this pipeline generates, and that markup — with the illustrations it names — is what
/// explains a rendered page. It lands as a <see cref="DocumentationSource"/> node carrying the
/// source itself, with the illustrations attached to it (§5.4 of the connector skill).
/// </para>
/// <para>
/// It is the one expensive pass in the connector: an illustration is drawn per figure, a page
/// is laid out per document, and a document of the printed era is rasterised and scanned page
/// by page. Rendering therefore runs across cores, and everything is behind
/// <see cref="ConnectorContext.Cache"/> — a document whose content has not changed since the
/// last run is not re-rendered and not re-uploaded. Pass <c>--no-attachments</c> to skip the
/// pass entirely while iterating on the rest of the mapping.
/// </para>
/// </remarks>
internal static class AttachmentsIngest
{
    public const int Version = 3;   // bumped: rendered per issue, sources hang off the issue

    /// <summary>
    /// The source name every uploaded file is filed under. The workspace groups files by
    /// source, so one name keeps the whole corpus together and lets it be replaced wholesale.
    /// </summary>
    private const string SourceName = "aeralis-documents";

    /// <summary>
    /// The CSDB object a publication was rendered from: the markup handed to the presentation
    /// layer, keyed by the document type, identifier and issue it was projected from.
    /// </summary>
    /// <remarks>
    /// Keyed by all three because an identifier alone does not say which type it is — a service
    /// bulletin is numbered <c>MRD-53-1042</c> and nothing in that says "SB" — and every issue
    /// of a document was rendered from its own markup. The source text is a property rather than
    /// a file so that it is searchable and renders in place; the illustrations it names are
    /// files, attached to this node.
    /// </remarks>
    [Node]
    internal sealed class DocumentationSource
    {
        [Key] public string SourceId { get; set; } = "";
        [Property] public string DocType { get; set; } = "";
        [Property] public string DocId { get; set; } = "";

        /// <summary>Which issue of that document this is the source of.</summary>
        [Property] public string Issue { get; set; } = "";

        [Property] public string Title { get; set; } = "";
        [Property] public string ProgramLetter { get; set; } = "";

        /// <summary>The S1000D CSDB object type, e.g. <c>Procedure</c>.</summary>
        [Property] public string ObjectType { get; set; } = "";

        /// <summary>The S1000D schema it belongs to, without the suffix, e.g. <c>proced</c>.</summary>
        [Property] public string Schema { get; set; } = "";

        /// <summary>The printed data module code.</summary>
        [Property] public string DataModuleCode { get; set; } = "";

        /// <summary>The markup itself.</summary>
        [Property] public string Source { get; set; } = "";

        [Property] public int SourceLength { get; set; }
        [Property] public int IllustrationCount { get; set; }

        /// <summary>The ICNs the source references, so they can be seen without parsing it.</summary>
        [Property] public string Illustrations { get; set; } = "";

        [Timestamp] public DateTimeOffset Timestamp { get; set; }
    }

    /// <summary>The issue node type that holds one issue of each document type.</summary>
    private static string IssueNodeType(DocType docType) => docType switch
    {
        DocType.MOD => nameof(DocumentIssueSchema.ModificationIssue),
        DocType.SB => nameof(DocumentIssueSchema.ServiceBulletinIssue),
        DocType.IO => nameof(DocumentIssueSchema.InternalOrderIssue),
        DocType.PAWO => nameof(DocumentIssueSchema.PreAnalysisWorkOrderIssue),
        DocType.AMM => nameof(DocumentIssueSchema.AmmTaskIssue),
        DocType.IPC => nameof(DocumentIssueSchema.IpcFigureIssue),
        DocType.SRM => nameof(DocumentIssueSchema.SrmRepairIssue),
        DocType.TSM => nameof(DocumentIssueSchema.TsmProcedureIssue),
        DocType.WDM => nameof(DocumentIssueSchema.WdmSheetIssue),
        DocType.FCOM => nameof(DocumentIssueSchema.FcomEntryIssue),
        _ => "",
    };

    /// <summary>The graph node type a document of each kind was ingested as.</summary>
    private static string NodeType(DocType docType) => docType switch
    {
        DocType.MOD => nameof(DocumentSchema.Modification),
        DocType.SB => nameof(DocumentSchema.ServiceBulletin),
        DocType.IO => nameof(DocumentSchema.InternalOrder),
        DocType.PAWO => nameof(DocumentSchema.PreAnalysisWorkOrder),
        DocType.AMM => nameof(DocumentSchema.AmmTask),
        DocType.IPC => nameof(DocumentSchema.IpcFigure),
        DocType.SRM => nameof(DocumentSchema.SrmRepair),
        DocType.TSM => nameof(DocumentSchema.TsmProcedure),
        DocType.WDM => nameof(DocumentSchema.WdmSheet),
        DocType.FCOM => nameof(DocumentSchema.FcomEntry),
        _ => "",
    };

    /// <param name="ctx">The run.</param>
    /// <param name="parallelism">How many documents to render at once.</param>
    /// <param name="keepAt">
    /// A directory to also write every generated file to, or null to keep them in memory only.
    /// In a dry run this is the only way to see the output, since nothing is uploaded.
    /// </param>
    public static async Task RunAsync(ConnectorContext ctx, int parallelism, string? keepAt)
    {
        var reference = DatasetLoader.LoadReference(ctx.DatasetRoot);
        // Every issue, not only the current one: each was published in its own right and has
        // its own markup, artwork and PDF.
        var documents = DatasetLoader.LoadDocuments(ctx.DatasetRoot).Issues;

        var work = Path.Combine(AppContext.BaseDirectory, "cache", "attachments-work");
        var builder = new AttachmentBuilder(reference, work);

        if (keepAt is not null) Directory.CreateDirectory(keepAt);

        var state = new PassState(ctx, builder, keepAt, Stopwatch.StartNew());

        await Parallel.ForEachAsync(documents,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism) },
            (loaded, _) => new ValueTask(OneAsync(state, loaded)));

        if (state.Failures > 0)
        {
            Console.WriteLine($"    {state.Failures} document(s) produced no attachment and will be "
                              + "retried on the next run");
        }

        await ctx.Graph.CommitPendingAsync();
        await ctx.CommitCacheAsync();
    }

    private static async Task OneAsync(PassState state, LoadedDocument loaded)
    {
        var doc = loaded.Envelope;
        var nodeType = NodeType(doc.DocType);
        if (nodeType.Length == 0) return;

        // Rendering is a pure function of the document and the builder, so the fingerprint is
        // the document's own content: a re-authored document is re-rendered, an untouched one
        // is not.
        var fingerprint = new
        {
            doc.DocId,
            Type = doc.DocType.ToString(),
            Issue = loaded.Issue,
            loaded.IsLatest,
            doc.Revision,
            doc.Title,
            Bytes = loaded.Bytes,
            File = Path.GetFileName(loaded.Path),
        };

        try
        {
            await state.Ctx.Cache.IfNotCachedAsync(Version, fingerprint,
                _ => RenderAndUploadAsync(state, loaded, nodeType));
        }
        catch (AttachmentSkipped)
        {
            // Already reported. The hash was not recorded, so the next run retries it.
        }
    }

    private static async Task RenderAndUploadAsync(PassState state, LoadedDocument loaded, string nodeType)
    {
        var doc = loaded.Envelope;

        DocumentRendering rendering;
        try
        {
            rendering = state.Builder.Build(loaded);
        }
        catch (Exception ex)
        {
            state.Fail(doc.DocId, ex);
            // Throwing rather than returning: the cache records a hash only for an action that
            // completed, so a failed render stays uncached and is retried next run. It is
            // caught one frame up, because letting it out would abandon the whole pass.
            throw new AttachmentSkipped();
        }

        var issueNode = Node.FromKey(IssueNodeType(doc.DocType),
            IssuesIngest.IssueId(doc.DocId, loaded.Issue));

        // The published file hangs off the issue it is of, and — when that is the issue the
        // document is currently at — off the document too, so following a document still
        // reaches its current PDF without knowing the issue letter.
        foreach (var file in rendering.Files)
        {
            await state.AttachAsync(file, issueNode, "attachment");
            if (loaded.IsLatest)
            {
                await state.LinkAsync(file, Node.FromKey(nodeType, doc.DocId));
            }
        }

        if (rendering.Source is not { } source)
        {
            state.Rendered();
            return;
        }

        // What it was rendered from hangs off its own node, with the artwork attached to that.
        await state.KeepSourceAsync(source);

        if (!state.Ctx.DryRun)
        {
            var node = state.Ctx.Graph.AddOrUpdate(new DocumentationSource
            {
                SourceId = source.SourceId,
                DocType = source.DocType,
                DocId = source.DocId,
                Issue = source.Issue,
                Title = doc.Title,
                ProgramLetter = doc.Program.ToString(),
                ObjectType = source.ObjectType,
                Schema = source.Schema,
                DataModuleCode = source.DataModuleCode,
                Source = source.Xml,
                SourceLength = source.Xml.Length,
                IllustrationCount = source.Images.Count,
                Illustrations = string.Join(" ", source.Images.Select(i => i.FileName)),
                Timestamp = Stamp(doc),
            });

            state.Ctx.Graph.Link(node, issueNode, Edges.SourceOf, Edges.HasSource);
        }
        state.Ctx.CountNode(nameof(DocumentationSource));
        state.Ctx.CountEdge(Edges.HasSource);

        var sourceNode = Node.FromKey(nameof(DocumentationSource), source.SourceId);
        foreach (var image in source.Images)
        {
            await state.AttachAsync(image, sourceNode, "images");
        }

        state.Rendered();
    }

    private static DateTimeOffset Stamp(TechDocument doc)
    {
        var value = doc.Dates?.Released ?? doc.Dates?.Created;
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;
    }

    /// <summary>Everything the per-document work shares, and the locks that make it safe to.</summary>
    private sealed class PassState(ConnectorContext ctx, AttachmentBuilder builder, string? keepAt,
        Stopwatch stopwatch)
    {
        private readonly Lock _gate = new();
        private int _rendered;
        private int _failures;

        /// <summary>
        /// Uploads share one <see cref="Graph"/>, whose pending buffer is not something to have
        /// several threads inside at once. Rendering — which is where the time goes — runs on as
        /// many cores as asked for; the upload is serialised behind this.
        /// </summary>
        private readonly SemaphoreSlim _upload = new(1, 1);

        public ConnectorContext Ctx { get; } = ctx;
        public AttachmentBuilder Builder { get; } = builder;

        public int Failures
        {
            get { lock (_gate) return _failures; }
        }

        /// <summary>
        /// Uploads one file and hangs it off <paramref name="owner"/>, under the edge names the
        /// workspace already renders an attachment list from.
        /// </summary>
        /// <param name="subdirectory">Where it also goes on disk, when a directory was asked for.</param>
        public async Task AttachAsync(Attachment attachment, Node owner, string subdirectory)
        {
            if (keepAt is not null)
            {
                var directory = subdirectory == "attachment" ? keepAt : Path.Combine(keepAt, subdirectory);
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, attachment.FileName), attachment.Bytes);
            }

            if (Ctx.DryRun)
            {
                Count();
                return;
            }

            await _upload.WaitAsync();
            try
            {
                using var stream = new MemoryStream(attachment.Bytes, writable: false);
                var file = await Ctx.Graph.UploadFileAsync(stream, attachment.FileName,
                    SourceName, checkHash: true, silent: true);

                Ctx.Graph.Link(file, owner, Edges.AttachmentOf, Edges.HasAttachment);
                Count();
            }
            finally
            {
                _upload.Release();
            }
        }

        /// <summary>
        /// Writes the CSDB source out when a directory was asked for. It goes into the graph as
        /// a property rather than a file, so this is the only way to read it back off a run.
        /// </summary>
        public async Task KeepSourceAsync(CsdbSource source)
        {
            if (keepAt is null) return;

            var directory = Path.Combine(keepAt, "source");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, source.FileName), source.Xml);
        }

        /// <summary>
        /// Hangs an already-uploaded file off a second owner. Used for the current issue's PDF,
        /// which is both the issue's attachment and the document's.
        /// </summary>
        public async Task LinkAsync(Attachment attachment, Node owner)
        {
            if (Ctx.DryRun)
            {
                lock (_gate) Ctx.CountEdge(Edges.HasAttachment);
                return;
            }

            await _upload.WaitAsync();
            try
            {
                // The file is already in the graph under this name and source; TryGetFileNode
                // finds it rather than uploading the bytes a second time.
                var file = await Ctx.Graph.TryGetFileNodeAsync(attachment.FileName, SourceName);
                if (file is not null)
                {
                    Ctx.Graph.Link(file, owner, Edges.AttachmentOf, Edges.HasAttachment);
                    lock (_gate) Ctx.CountEdge(Edges.HasAttachment);
                }
            }
            finally
            {
                _upload.Release();
            }
        }

        private void Count()
        {
            lock (_gate)
            {
                Ctx.CountNode("Attachment");
                Ctx.CountEdge(Edges.HasAttachment);
            }
        }

        public void Fail(string docId, Exception ex)
        {
            lock (_gate)
            {
                if (++_failures <= 5)
                {
                    Console.WriteLine($"    {docId}: {ex.GetType().Name} — {ex.Message}");
                }
            }
        }

        public void Rendered()
        {
            lock (_gate)
            {
                if (++_rendered % 50 == 0)
                {
                    Console.WriteLine($"    {_rendered,5:N0} rendered  {stopwatch.Elapsed.TotalSeconds,6:F0} s");
                }
            }
        }
    }

    /// <summary>Signals a render that failed, so its hash is not recorded. Never escapes the pass.</summary>
    private sealed class AttachmentSkipped : Exception;
}
