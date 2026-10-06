using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Game.Shared.UI
{
    /// <summary>
    /// Arabic presentation for the existing TMP labels. Tables and presenter values stay in
    /// logical Unicode order. TMP performs RTL layout and wrapping; this component supplies
    /// contextual glyphs and preserves left-to-right number/product-name runs.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
    [DefaultExecutionOrder(10000)]
    public sealed class LocalizedArabicText : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        private string logicalText;
        private string displayedText;
        private HorizontalAlignmentOptions originalAlignment;
        private bool originalRtl;
        private bool initialized;

        private static readonly Regex Arabic = new Regex("[\\u0621-\\u064A]");
        // Tags are separate tokens so attributes, sprite indices and formatting stay intact.
        private static readonly Regex Tokens = new Regex(@"<[^>]+>|[^<]+");
        private static readonly Regex LatinRuns = new Regex(@"[A-Za-z0-9\u00C0-\u024F\u0660-\u0669\u06F0-\u06F9]+(?:[ .,:/%+\-$€£()\-]*[A-Za-z0-9\u00C0-\u024F\u0660-\u0669\u06F0-\u06F9]+)*");
        // Unicode Arabic Presentation Forms-B: isolated, final, initial, medial.
        private static readonly Dictionary<char, string> Forms = new Dictionary<char, string>
        {
            ['ء']="ﺀ", ['آ']="ﺁﺂ", ['أ']="ﺃﺄ", ['ؤ']="ﺅﺆ", ['إ']="ﺇﺈ", ['ئ']="ﺉﺊﺋﺌ",
            ['ا']="ﺍﺎ", ['ب']="ﺏﺐﺑﺒ", ['ة']="ﺓﺔ", ['ت']="ﺕﺖﺗﺘ", ['ث']="ﺙﺚﺛﺜ", ['ج']="ﺝﺞﺟﺠ",
            ['ح']="ﺡﺢﺣﺤ", ['خ']="ﺥﺦﺧﺨ", ['د']="ﺩﺪ", ['ذ']="ﺫﺬ", ['ر']="ﺭﺮ", ['ز']="ﺯﺰ",
            ['س']="ﺱﺲﺳﺴ", ['ش']="ﺵﺶﺷﺸ", ['ص']="ﺹﺺﺻﺼ", ['ض']="ﺽﺾﺿﻀ", ['ط']="ﻁﻂﻃﻄ",
            ['ظ']="ﻅﻆﻇﻈ", ['ع']="ﻉﻊﻋﻌ", ['غ']="ﻍﻎﻏﻐ", ['ف']="ﻑﻒﻓﻔ", ['ق']="ﻕﻖﻗﻘ",
            ['ك']="ﻙﻚﻛﻜ", ['ل']="ﻝﻞﻟﻠ", ['م']="ﻡﻢﻣﻤ", ['ن']="ﻥﻦﻧﻨ", ['ه']="ﻩﻪﻫﻬ",
            ['و']="ﻭﻮ", ['ى']="ﻯﻰ", ['ي']="ﻱﻲﻳﻴ", ['ـ']="ــــ"
        };

        private void Awake()
        {
            if (label == null) label = GetComponent<TMP_Text>();
            originalAlignment = label.horizontalAlignment;
            originalRtl = label.isRightToLeftText;
            initialized = true;
        }

        private void OnEnable() => Refresh();
        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (!initialized || label == null || label.text == displayedText) return;
            logicalText = label.text;
            bool rtl = ContainsArabic(logicalText);
            displayedText = rtl ? Prepare(logicalText) : logicalText;
            label.isRightToLeftText = rtl || originalRtl;
            label.horizontalAlignment = rtl && originalAlignment == HorizontalAlignmentOptions.Left
                ? HorizontalAlignmentOptions.Right : originalAlignment;
            label.text = displayedText;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            if (label.text == displayedText) label.text = logicalText;
            label.isRightToLeftText = originalRtl;
            label.horizontalAlignment = originalAlignment;
            displayedText = null;
        }

        public static bool ContainsArabic(string value) => !string.IsNullOrEmpty(value) && Arabic.IsMatch(value);

        public static string Prepare(string value)
        {
            if (!ContainsArabic(value)) return value;
            var result = new StringBuilder(value.Length);
            foreach (Match token in Tokens.Matches(value))
            {
                if (token.Value[0] == '<') { result.Append(token.Value); continue; }
                string input = token.Value;
                var shaped = new StringBuilder(input.Length);
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if (!Forms.TryGetValue(c, out string forms)) { shaped.Append(c); continue; }
                    int previous = Adjacent(input, i, -1);
                    int next = Adjacent(input, i, 1);
                    bool joinsPrevious = previous >= 0 && Forms.TryGetValue(input[previous], out string before)
                        && before.Length == 4 && forms.Length > 1;
                    bool joinsNext = next < input.Length && Forms.TryGetValue(input[next], out string after)
                        && after.Length > 1 && forms.Length == 4;
                    // Lam-alef must be one glyph, including its connected final form.
                    if (c == 'ل' && i + 1 < input.Length)
                    {
                        int ligature = input[i + 1] == 'آ' ? 0xFEF5 : input[i + 1] == 'أ' ? 0xFEF7 :
                            input[i + 1] == 'إ' ? 0xFEF9 : input[i + 1] == 'ا' ? 0xFEFB : 0;
                        if (ligature != 0) { shaped.Append((char)(ligature + (joinsPrevious ? 1 : 0))); i++; continue; }
                    }
                    shaped.Append(forms[joinsNext ? (joinsPrevious ? 3 : 2) : (joinsPrevious ? 1 : 0)]);
                }
                // TMP lays out these logical characters from right to left, including wrapped
                // lines. Reverse only Latin/number runs, never the paragraph or TMP tags.
                result.Append(LatinRuns.Replace(shaped.ToString(), match =>
                {
                    char[] chars = match.Value.ToCharArray();
                    System.Array.Reverse(chars);
                    return new string(chars);
                }));
            }
            return result.ToString();
        }

        private static int Adjacent(string text, int index, int direction)
        {
            index += direction;
            while (index >= 0 && index < text.Length && CharUnicodeInfo.GetUnicodeCategory(text[index]) == UnicodeCategory.NonSpacingMark)
                index += direction;
            return index;
        }
    }
}
