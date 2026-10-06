// Reference template: composing LabelField values. See ../SKILL.md §4.
//
// LabelField is NOT a caption. It is a list of node FIELD NAMES separated by '|', and the label
// is the first one that has a value — or, with a leading '$', an interpolated template whose
// {Placeholders} are field names.
//
// When nothing resolves — including when the field simply does not exist on that type — the
// label falls back to "<uid> [Display Name]" with NO ERROR ANYWHERE. A typo turns every label
// of that type into a raw UID. That is why these are composed from the generated N constants
// here rather than typed out as literals at each call site: a rename then breaks the build.

namespace App.FrontEnd.Renderers
{
    internal static class LabelFields
    {
        /// <summary>
        /// "${Id} > {Title}" — for every type that has both an identifier and a title.
        ///
        /// <para>The '|' form is wrong for these: it takes the FIRST field with a value, so
        /// <c>Title|DocId</c> shows the title and never the identifier. In technical content
        /// the identifier alone does not say what the record is, and the title alone does not
        /// say which of four similar ones it is — the reader needs both.</para>
        ///
        /// <para>A missing value leaves no hole: the interpolator trims separators
        /// (&gt;, -, :, whitespace) off both ends, so a record with no title still reads as its
        /// identifier alone.</para>
        /// </summary>
        public static string IdAndTitle(string idField, string titleField)
            => "$" + Placeholder(idField) + " > " + Placeholder(titleField);

        /// <summary>
        /// The '|' form: the first field with a value wins. Name a fallback after ANY field
        /// that can legitimately be empty.
        /// </summary>
        public static string FirstOf(params string[] fields) => string.Join("|", fields);

        private static string Placeholder(string field) => "{" + field + "}";
    }
}
