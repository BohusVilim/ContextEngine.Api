namespace ContextEngine.Api.Parsers
{
    /// <summary>
    /// Heading-nesting rule shared by <see cref="DocxParser"/> and <see cref="PdfParser"/>. Each
    /// format derives a heading's outline "level" its own way - <see cref="DocxParser"/> reads it
    /// straight from the "HeadingX" style id, <see cref="PdfParser"/> ranks distinct heading font
    /// sizes largest-first - but once a level is known, the rule for how one heading nests under
    /// another is identical, so both parsers share this one implementation instead of each keeping
    /// their own copy.
    /// </summary>
    internal static class HeadingAncestry
    {
        /// <summary>
        /// Pops any open heading whose level is not strictly less than <paramref name="level"/> - e.g.
        /// hitting another level-2 heading closes out the previous one (same level: it's a sibling,
        /// not a child) but leaves an enclosing level-1 heading open (lower level: still an ancestor) -
        /// then returns what remains on top as the new heading's parent.
        /// </summary>
        /// <param name="openHeadings">
        /// Headings currently open above the point in the document being parsed, outermost first;
        /// mutated in place as headings are popped off.
        /// </param>
        /// <param name="level">Outline level of the heading about to be added.</param>
        /// <returns>Id of the heading <paramref name="level"/> should nest under, or null if it belongs at the top.</returns>
        public static Guid? BuildParentId(Stack<(int Level, Guid Id)> openHeadings, int level)
        {
            while (openHeadings.Count > 0 && openHeadings.Peek().Level >= level)
            {
                openHeadings.Pop();
            }

            return openHeadings.Count > 0 ? openHeadings.Peek().Id : null;
        }
    }
}
