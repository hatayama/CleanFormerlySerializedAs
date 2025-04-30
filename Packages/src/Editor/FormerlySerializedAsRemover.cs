using System.Text.RegularExpressions;

namespace io.github.hatayama.CleanFormerlySerializedAs
{
    /// <summary>
    /// Handles the logic for removing FormerlySerializedAs attributes.
    /// </summary>
    public class FormerlySerializedAsRemover
    {
        /// <summary>
        /// Removes FormerlySerializedAs attributes from the script content.
        /// </summary>
        /// <param name="content">The script content to process.</param>
        /// <returns>The processed script content and the number of removed attributes.</returns>
        public (string processedContent, int removedCount) RemoveFormerlySerializedAs(string content)
        {
            int totalRemovedCount = 0;
            // Regular expression to find only the FormerlySerializedAs attribute part:
            // string fsaPattern = @"FormerlySerializedAs\([^)]*\)"; // Used within MatchEvaluator

            // Regular expression to find the entire attribute block and any potential trailing end-of-line comments.
            // Group 1: The entire block including square brackets ([Attr, FSA, Attr2])
            // Group 2: The content inside the square brackets (Attr, FSA, Attr2)
            // Group 3: Whitespace after the closing bracket (includes newlines if any)
            // Group 4: Whitespace and end-of-line comment after the block ( // comment) (optional)
            string pattern = @"(\[([^\]]*)\])(\s*)(\s*//.*)?";
            var regex = new Regex(pattern);

            string processedResult = regex.Replace(content, match =>
            {
                string attributesInside = match.Groups[2].Value; // e.g., Attr, FSA, Attr2
                string whitespaceAfterBracket = match.Groups[3].Value; // Whitespace immediately after ]
                string trailingCommentGroup = match.Groups[4].Value; // The captured comment part including its leading whitespace
                string trailingComment = match.Groups[4].Success ? trailingCommentGroup : ""; // Use the captured group if it exists

                // Count the number of FormerlySerializedAs attributes within the matched attributes.
                int currentMatchRemovedCount = Regex.Matches(attributesInside, @"FormerlySerializedAs\([^)]*\)").Count;
                totalRemovedCount += currentMatchRemovedCount;

                if (currentMatchRemovedCount == 0)
                {
                    // If no FSA is found in this block, don't change anything.
                    return match.Value; // Return the original entire match (block + comment).
                }

                // Remove FormerlySerializedAs from the attribute list.
                string cleanedAttributes = attributesInside;
                // First, remove FSA followed by a comma.
                cleanedAttributes = Regex.Replace(cleanedAttributes, @"FormerlySerializedAs\([^)]*\)\s*,", "");
                // Remove FSA preceded by a comma.
                cleanedAttributes = Regex.Replace(cleanedAttributes, @",\s*FormerlySerializedAs\([^)]*\)", "");
                // Remove standalone FSA.
                cleanedAttributes = Regex.Replace(cleanedAttributes, @"FormerlySerializedAs\([^)]*\)", "");

                // Cleanup syntax.
                cleanedAttributes = Regex.Replace(cleanedAttributes, @"\s*,\s*", ", "); // Normalize spaces around commas.
                cleanedAttributes = cleanedAttributes.Trim(); // Remove leading/trailing whitespace.
                cleanedAttributes = cleanedAttributes.TrimEnd(','); // Remove trailing unnecessary comma (e.g., [A, B,] -> [A, B]).
                cleanedAttributes = cleanedAttributes.TrimStart(','); // Remove leading unnecessary comma (e.g., [, A, B] -> [A, B]).
                cleanedAttributes = cleanedAttributes.Trim(); // Trim again after potential comma removal

                if (string.IsNullOrWhiteSpace(cleanedAttributes))
                {
                    // If all attributes were removed (only FSA existed),
                    // remove the attribute block `[...]` and the whitespace immediately following it,
                    // but keep the trailing comment.
                    return trailingComment; // whitespaceAfterBracket is implicitly removed
                }
                else
                {
                    // If attributes remain, return them formatted, preserving the whitespace after the bracket and the comment.
                    return $"`[{cleanedAttributes}]{whitespaceAfterBracket}{trailingComment}";
                }
            });

            // Final cleanup: Collapse multiple consecutive newlines into a single newline.
            // This might remove intended blank lines, so commenting it out for now.
            // processedResult = Regex.Replace(processedResult, @"(\r?\n){2,}", "\n", RegexOptions.Multiline);

            return (processedResult, totalRemovedCount);
        }
    }
} 