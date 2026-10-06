[tools: Curiosity.ChatAITools.UID("AiTooLsEarCh1111111111")]
[tools: Curiosity.ChatAITools.DisplayName("Search Files")]
[tools: Curiosity.ChatAITools.Description("Search the user's files, documents, slides, emails, notes and sheets by keyword (to find files) plus similarity (to extract the most relevant snippets). CRITICAL - terms are combined with AND, never with OR: a file comes back only if EVERY term matches it, so each extra word can only shrink the result set. Pass distinctive CONTENT words (names, topics, unique terms) - never a full sentence, filler or question words, or a word you only hope appears. Write 'a OR b OR c' for files matching ANY term, ranked with multi-term matches first. Keep dates, file types and data-source names out of the query - pass them as the source / fromDate / toDate parameters instead. When a search disappoints, do NOT conclude the information is absent - work down this ladder: (1) the word query, (2) drop the least distinctive word, (3) OR between the words, (4) one word per search, (5) SemanticSearchFiles for meaning-based questions. Only once those also come back empty may you say nothing was found. Every response echoes parsedQuery (how the engine read your query), ignoredWords (terms dropped as too short or as stopwords) and, on no or very few matches, a hint - read them before retrying. Returns totalMatches (the page starts at skip) and one result per file, with its uid (usable with ConsultFile) and up to 3 snippets most similar to the query. Optionally sort by recency, tune typo tolerance, restrict to a data source (see ListFileSources) and/or a modified-date range. IMPORTANT: always cite snippets as [snippetId:X] and quote them directly when referencing their information.")]
[tools: Curiosity.ChatAITools.Icon("fi-rr-search")]
[tools: Curiosity.ChatAITools.AccessMode("AllUsers")]

public class SearchTool
{
    private const int CHUNKS_PER_FILE = 3;
    private const int TOP_RESULTS = 10;

    [Tool("Search the user's files, documents, slides, emails, notes and sheets by keyword (to find files) plus similarity (to extract the most relevant snippets). CRITICAL - terms are combined with AND, never with OR: a file comes back only if EVERY term matches it, so each extra word can only shrink the result set. Pass distinctive CONTENT words (names, topics, unique terms) - never a full sentence, filler or question words, or a word you only hope appears. Write 'a OR b OR c' for files matching ANY term, ranked with multi-term matches first. Keep dates, file types and data-source names out of the query - pass them as the source / fromDate / toDate parameters instead. When a search disappoints, do NOT conclude the information is absent - work down this ladder: (1) the word query, (2) drop the least distinctive word, (3) OR between the words, (4) one word per search, (5) SemanticSearchFiles for meaning-based questions. Only once those also come back empty may you say nothing was found. Every response echoes parsedQuery (how the engine read your query), ignoredWords (terms dropped as too short or as stopwords) and, on no or very few matches, a hint - read them before retrying. Returns totalMatches (the page starts at skip) and one result per file, with its uid (usable with ConsultFile) and up to 3 snippets most similar to the query. Optionally sort by recency, tune typo tolerance, restrict to a data source (see ListFileSources) and/or a modified-date range. IMPORTANT: always cite snippets as [snippetId:X] and quote them directly when referencing their information.")]
    public static async Task<string> SearchFiles(ToolScope scope,
          [Parameter("Search query: distinctive content words (names, topics, unique terms). Terms are combined with AND, so every one must match and any word missing from a document excludes it. Syntax (uppercase operators are case-sensitive): 'a OR b' ('a | b') matches either term and ranks files matching more terms first; 'a AND b' ('a & b') is the default spelled out; 'NOT b' ('!b', '-b') excludes; '(' ')' or '[' ']' group; \"double quotes\" require that exact adjacent phrase, so \"annual report\" does NOT match a file titled 'Annual Financial Report'; *asterisks* match a word or phrase as a substring, so *negoti* matches negotiate, negotiation and negotiated. Prefer the word NOT over '-' when a term itself contains a hyphen. Terms also match typo-variants (see fuzziness), configured synonyms and known abbreviations, so exact spelling is not required; terms shorter than 2 characters and workspace stopwords are dropped (listed as ignoredWords in the response). Keep dates, file types and source names out of the query - use the source / fromDate / toDate parameters. Try the plain word query first (it also matches file titles, which an OR query does not), then OR, then one word per search, before concluding that something does not exist.", required: true)] string query,
          [Parameter("Maximum total results to return (default: 10)", required: false)] int limit = 10,
          [Parameter("Number of results to skip for pagination (default: 0)", required: false)] int skip = -1,
          [Parameter("Result order: 'relevance' (default, best match first), 'newest' (most recently modified first) or 'oldest'.", required: false)] string sortBy = "relevance",
          [Parameter("Typo tolerance per word: Automatic (default) picks by word length, ExactMatchOnly disables fuzzy matching (use for codes, IDs and exact names), AllowOneTypo / AllowTwoTypos allow that many letter edits.", required: false)] SearchFuzzinessLevel fuzziness = SearchFuzzinessLevel.Automatic,
          [Parameter("Restrict results to a single data source (exact name, see ListFileSources). Empty for all sources.", required: false)] string source = null,
          [Parameter("Only files modified on/after this date (ISO 8601, e.g. 2024-01-31). Empty for no lower bound.", required: false)] string fromDate = null,
          [Parameter("Only files modified on/before this date (ISO 8601, e.g. 2024-12-31). Empty for no upper bound.", required: false)] string toDate = null)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new { totalMatches = 0, results = Array.Empty<object>() }.ToJson();
        }
        else
        {
            var req = SearchRequest.For(query.Trim());

            req.SortMode = sortBy == "newest" ? SortModeEnum.RecentFirst
                         : sortBy == "oldest" ? SortModeEnum.RecentLast
                         : SortModeEnum.Relevance;

            if (fuzziness != SearchFuzzinessLevel.Automatic)
            {
                req.Fuzziness = (int)fuzziness;
            }

            req.UserUID = scope.CurrentUser;
            req.BeforeTypesFacet = new HashSet<string>
            {
                _FileEntry.Type
            };

            //Optional source filter: SetValueFacet normalises any "*.Source" key to the special "Node.Source" back-end key.
            if (!string.IsNullOrWhiteSpace(source))
            {
                req.SetValueFacet("Node.Source", keepOtherTypes: false, invertedBehaviour: false, applyBefore: true, source);
            }

            //Optional modified-date range filter against the node timestamp; either bound may be omitted.
            var hasFrom = DateTimeOffset.TryParse(fromDate, out var from);
            var hasTo = DateTimeOffset.TryParse(toDate, out var to);

            if (hasFrom || hasTo)
            {
                req.SetTimestampFacet(hasFrom ? from : DateTimeOffset.MinValue, hasTo ? to : DateTimeOffset.UtcNow.AddDays(1), applyBefore: true);
            }

            var search = await scope.Graph.CreateSearchAsUserAsync(req, scope.CurrentUser, scope.CancellationToken);

            search.EmitCount("Count", out var totalMatches);

            //The pipeline writes the tokens it parsed back onto the request, so the model can be told how its query was really read.
            var parsed = DescribeParsedQuery(req);
            var results = new List<object>();

            foreach (var node in search.Skip(skip > 0 ? skip : 0).Take(limit > 0 ? limit : 10).AsEnumerable())
            {
                var chunks = new List<object>();

                //Snippet extraction reads (and, for un-indexed files, encodes) the whole document, so hand it
                //the chat progress: the user sees the "Reading..." progress instead of a silent chat.
                foreach (var chunk in (await scope.ChatAI.GetSimilarChunksAsync(node.UID, query, scope.Progress, scope.CancellationToken)).OrderByDescending(e => e.Score).Take(CHUNKS_PER_FILE))
                {
                    //You can add snippets to the tool calling response so they show as results in the interface
                    var snippetID = scope.AddSnippet(uid: node.UID, text: chunk.Text, page: chunk.Page ?? 1, endPage: chunk.EndPage ?? 1);

                    chunks.Add(new
                    {
                        snippetId = snippetID,
                        page = chunk.Page ?? 1,
                        content = chunk.Text
                    });
                }

                results.Add(new
                {
                    uid = node.UID.ToString(),
                    name = node.GetString(nameof(_FileEntry.OriginalName)),
                    source = node.GetString(nameof(_FileEntry.Source)),
                    chunks = chunks
                });
            }

            scope.SetToolCallDisplayName($"Search for '{query}'");

            return new
            {
                totalMatches = totalMatches,
                skip = skip > 0 ? skip : 0,
                returned = results.Count,
                parsedQuery = parsed.Text,
                ignoredWords = parsed.Ignored.Count > 0 ? parsed.Ignored.ToArray() : null,
                hint = GetSearchHint(parsed, totalMatches),
                results = results
            }.ToJson();
        }
    }

    //Rebuilds how the engine read the query - implicit ANDs between plain words included - from the tokens the search pipeline parsed.
    private static (string Text, List<string> Terms, bool HasOr, List<string> Ignored) DescribeParsedQuery(SearchRequest req)
    {
        var terms = new List<string>();
        var ignored = new List<string>();
        var parts = new List<string>();
        var tokens = req.Query?.Tokens;

        if (tokens is null) return (null, terms, false, ignored);

        var hasOr = false;
        var lastWasTerm = false;

        foreach (var token in tokens)
        {
            if (token.Ignore)
            {
                ignored.Add(token.Text);
                continue;
            }

            switch (token.ParsedAs)
            {
                case QueryTokenType.And: parts.Add("AND"); lastWasTerm = false; continue;
                case QueryTokenType.Or: parts.Add("OR"); lastWasTerm = false; hasOr = true; continue;
                case QueryTokenType.Not:
                case QueryTokenType.NotOrToken: parts.Add("NOT"); lastWasTerm = false; continue;
                case QueryTokenType.OpenParenthesis: parts.Add("("); lastWasTerm = false; continue;
                case QueryTokenType.CloseParenthesis: parts.Add(")"); lastWasTerm = true; continue;
                case QueryTokenType.Null: continue;
            }

            //Two plain words next to each other are ANDed by the engine - spell that out for the model.
            if (lastWasTerm) parts.Add("AND");

            if (token.ParsedAs == QueryTokenType.Time || token.ParsedAs == QueryTokenType.Shortcut)
            {
                parts.Add($"[{token.ParsedAs}: {token.Text}]");
            }
            else
            {
                parts.Add(token.Text);
                terms.Add(token.Text);
            }

            lastWasTerm = true;
        }

        return (parts.Count > 0 ? string.Join(" ", parts) : null, terms, hasOr, ignored);
    }

    //An AND of several terms matching nothing (or almost nothing) is what models most often misread as "it is not in the workspace".
    private static string GetSearchHint((string Text, List<string> Terms, bool HasOr, List<string> Ignored) parsed, int totalMatches)
    {
        if (parsed.HasOr || parsed.Terms.Count < 2) return null;

        var asOr = string.Join(" OR ", parsed.Terms);

        if (totalMatches == 0)
        {
            return $"No file matches ALL of these terms ({parsed.Text}). The terms are combined with AND, so this does NOT mean the content is missing: retry as '{asOr}', or drop the least distinctive terms, or search one term at a time, or use SemanticSearchFiles - and only then conclude that nothing was found.";
        }

        if (totalMatches <= 2 && parsed.Terms.Count >= 3)
        {
            return $"Only {totalMatches} file(s) match ALL of these terms ({parsed.Text}). If they do not answer the question, retry with fewer terms or as '{asOr}' before concluding that the content is missing.";
        }

        return null;
    }

    [Tool("Retrieval-augmented (RAG) semantic search over files: encodes the query and runs a pure vector search against the configured file sentence-embedding index, returning the top 10 most relevant files, each with up to 3 best-matching chunks. Use this for meaning-based questions where keyword search is not enough. IMPORTANT: always cite chunks as [snippetId:X] and quote them directly.")]
    public static async Task<string> SemanticSearchFiles(ToolScope scope,
          [Parameter("Natural-language query to semantically match against file contents", required: true)] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "[]";
        }

        //The configured sentence-embedding index over files. Requires AI Search for files to be enabled.
        var fileIndex = scope.Graph.Internals.Indexes.GetFileSentenceEmbeddingIndex();

        if (fileIndex is null)
        {
            return new { error = "No sentence embedding index for files is configured. Enable AI Search for files first." }.ToJson();
        }

        //Vector search: query text -> top file nodes ranked by embedding similarity (access-scoped to the current user).
        var similarFiles = await fileIndex.FindSimilarAsync(scope.CurrentUser, query, TOP_RESULTS, applyCutoff: false, similarityHint: SimilarityHint.FindAnswers);

        var results = new List<object>();

        foreach (var scored in similarFiles.Take(TOP_RESULTS))
        {
            var fileUID = scored.UID.UID;

            if (!scope.Graph.Internals.TryGetReadOnlyContent<_FileEntry>(fileUID, out var file)) continue;

            var chunks = new List<object>();

            //For each matched file, extract the top matching chunks (up to 3) to ground the answer. The chat
            //progress channel is passed along so the read / encode progress of each file shows while we run.
            foreach (var chunk in (await scope.ChatAI.GetSimilarChunksAsync(fileUID, query, scope.Progress, scope.CancellationToken)).OrderByDescending(e => e.Score).Take(CHUNKS_PER_FILE))
            {
                var snippetID = scope.AddSnippet(uid: fileUID, text: chunk.Text, page: chunk.Page ?? 1, endPage: chunk.EndPage ?? 1);

                chunks.Add(new
                {
                    snippetId = snippetID,
                    page = chunk.Page ?? 1,
                    score = chunk.Score,
                    content = chunk.Text
                });
            }

            results.Add(new
            {
                uid = fileUID.ToString(),
                name = file.OriginalName,
                source = file.Source,
                score = scored.Score,
                chunks = chunks
            });
        }

        scope.SetToolCallDisplayName($"Semantic search for '{query}'");

        return results.ToJson();
    }

    [Tool("Read a single file by its UID: returns its metadata (name, source, content type, size, language, url) plus its extracted text content, or only one page if you pass one. Use this for the full context of a file surfaced by SearchFiles or SemanticSearchFiles. The content is registered as a citable snippet whose number is in the response 'snippetId' field; cite it as [snippetId:X] with that exact number and quote directly from the content.")]
    public static string ConsultFile(ToolScope scope,
          [Parameter("The UID of the file to consult", required: true)] string fileUID,
          [Parameter("Optional 1-based page number to return only that page's content; omit for the whole document", required: false)] int page = -1,
          [Parameter("Maximum number of characters of content to return (default: 8000)", required: false)] int maxCharacters = 8000)
    {
        if (!UID128.TryParse(fileUID, out var uid))
        {
            return new { error = "Invalid file UID." }.ToJson();
        }

        if (!scope.Graph.CanAccess(scope.CurrentUser, uid))
        {
            return new { error = "You don't have access to this file." }.ToJson();
        }

        if (!scope.Graph.Internals.TryGetReadOnlyContent<_FileEntry>(uid, out var file))
        {
            return new { error = "File not found." }.ToJson();
        }

        //The tool exposes 1-based pages; PageNumberToPageIndex converts to the 0-based index GetTextFromNode expects (-1 means the whole document)
        var pageIndex = scope.ChatAI.PageNumberToPageIndex(page);
        var content = scope.ChatAI.GetTextFromNode(uid, maxCharacters > 0 ? maxCharacters : -1, pageIndex);

        scope.SetToolCallDisplayName($"Consult '{file.OriginalName}'");

        //Register the consulted text as a citable snippet so the model has a real [snippetId:X] to cite
        //(SearchFiles/SemanticSearchFiles do the same for their chunks). A single-page consult cites that
        //page; a whole-document consult uses page 0, which the UI treats as a non-paginated citation.
        var snippetPage = pageIndex >= 0 ? pageIndex + 1 : 0;
        int? snippetId = string.IsNullOrWhiteSpace(content)
            ? (int?)null
            : scope.AddSnippet(uid: uid, text: content, page: snippetPage, endPage: snippetPage);

        return new
        {
            uid = uid.ToString(),
            name = file.OriginalName,
            source = file.Source,
            contentType = file.ContentType,
            extension = file.Extension,
            sizeBytes = file.Size,
            language = file.Language.ToString(),
            url = file.Url,
            page = pageIndex >= 0 ? pageIndex + 1 : (int?)null,
            snippetId = snippetId,
            content = content
        }.ToJson();
    }

    [Tool("List all data sources that have files, taken from the file source index. Use the returned values with the 'source' filter of SearchFiles.")]
    public static string ListFileSources(ToolScope scope)
    {
        var sources = scope.Graph.Internals.Indexes
           .OfType<SimpleTextIndex>()
           .Where(i => i.FieldName == nameof(_FileEntry.Source))
           .SelectMany(i => i.Keys)
           .Distinct()
           .OrderBy(s => s)
           .ToArray();

        scope.SetToolCallDisplayName("List file sources");

        return sources.ToJson();
    }
}

return new SearchTool();

