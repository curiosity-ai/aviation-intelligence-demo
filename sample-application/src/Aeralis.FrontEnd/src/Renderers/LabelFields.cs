namespace Aeralis.FrontEnd.Renderers
{
    /// <summary>
    /// The interpolated <c>SchemaStyleInfo.LabelField</c> forms, composed from the
    /// generated field constants rather than written out as literals.
    ///
    /// <c>LabelField</c> is normally a <c>|</c>-separated list of field *names* and
    /// <c>Labels.Get</c> takes the first one with a value — so a document labelled
    /// <c>Title|DocId</c> shows its title and never its identifier. That is the wrong way
    /// round for technical data: <c>AMM-C-20-40-11-800-017-A</c> alone does not say what
    /// the task is, and *"Sealant Application at Pressurised Fuselage Joints"* alone does
    /// not say which of the four sealant tasks it is. A link, a search row and a graph
    /// node all need both.
    ///
    /// A label that starts with <c>$</c> is read instead as a template of <c>{Field}</c>
    /// placeholders and every one of them is rendered
    /// (<c>Shared/Mosaik.Shared/NodeLabelInterpolator.cs</c>). It is built here rather
    /// than typed out because the placeholders are field names, and a field name that
    /// does not exist fails silently — the label simply comes out empty. Composed from
    /// <c>N.&lt;Type&gt;.&lt;Field&gt;</c>, a rename is a build error instead.
    ///
    /// A missing value leaves no hole: the interpolator trims separators (<c>&gt;</c>,
    /// <c>-</c>, <c>:</c>, whitespace…) off both ends of the result, so a document with
    /// no title still reads as its identifier alone.
    /// </summary>
    internal static class LabelFields
    {
        /// <summary>"$ {DocId} &gt; {Title}" — the identifier first, then what it is about.</summary>
        public static string IdAndTitle(string idField, string titleField)
            => "${" + idField + "} > {" + titleField + "}";
    }
}
