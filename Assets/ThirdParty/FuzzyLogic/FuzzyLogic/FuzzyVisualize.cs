using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;


// All the Fuzz
using Tochas.FuzzyLogic;
using Tochas.FuzzyLogic.MembershipFunctions;
using Tochas.FuzzyLogic.Evaluators;
using Tochas.FuzzyLogic.Mergers;
using Tochas.FuzzyLogic.Defuzzers;
using Tochas.FuzzyLogic.Expressions;


namespace Tochas.FuzzyLogic
{
    public static class FuzzyVisualize
    {

        /// <summary>
        /// Append a compact, line-oriented dump of a fuzzy value set to the provided StringBuilder.
        /// The first line prints the generic enum type name T. If the set is null, the word "null"
        /// is appended and the method returns. Otherwise, one line per enum label in T is appended
        /// using the format "{label}::{degree}", where degree is rendered with two decimals.
        /// This method does not clear the StringBuilder and does not allocate temporary strings
        /// beyond the appended lines; it is suitable for per-frame diagnostics.
        /// </summary>
        /// <typeparam name="T">Enum type of the fuzzy variable (linguistic labels).</typeparam>
        /// <param name="fzset">FuzzyValueSet containing membership degrees for labels of T.</param>
        /// <param name="sb">Destination StringBuilder to append into.</param>
        /// <remarks>
        /// Output format example:
        ///   TName
        ///   LabelA::0.75
        ///   LabelB::0.10
        ///   LabelC::0.00
        /// Labels are enumerated in the declaration order of T. Degrees are clamped/rounded by
        /// the caller that produced the FuzzyValueSet; this method only formats to 0.00.
        /// </remarks>
        static public void DiagnosticPrintFuzzyValueSet<T>(FuzzyValueSet fzset, System.Text.StringBuilder sb) where T : struct, IConvertible
        {
            Type typ = typeof(T);
            sb.AppendLine(typ.Name);

            if (fzset == null)
            {
                sb.AppendLine("null");
                return;
            }

            foreach (var e in System.Enum.GetValues(typ))
            {
                var v = fzset.Get<T>((T)e);

                //UnityEngine.Debug.Log($"{v.linguisticVariable}::{v.membershipDegree}");
                sb.AppendLine($"{v.linguisticVariable}::{v.membershipDegree:0.00}");

            }

        }

        /// <summary>
        /// Append a diagnostic listing of a fuzzy rule set and its evaluated outputs to the provided StringBuilder.
        /// Prints the generic enum type name T on the first line. If the rule set, its Rules array, or the outputs
        /// array is null or empty, appends "null" and returns. When lengths differ, appends a one-line warning and
        /// prints aligned pairs up to min(rules, outputs). Any unpaired trailing items are listed separately.
        /// Each aligned line uses the format "{rule}::{degree}::{confidence}" with two-decimal formatting.
        /// </summary>
        /// <typeparam name="T">Enum type of the fuzzy OUTPUT variable governed by the rule set.</typeparam>
        /// <param name="fzRuleSet">The rule set whose rules will be printed via ToString().</param>
        /// <param name="fzRuleOutpts">The evaluated outputs expected to align one-to-one with the rules.</param>
        /// <param name="sb">Destination StringBuilder to append into.</param>
        /// <remarks>
        /// Alignment policy: let R = fzRuleSet.Rules.Length and O = fzRuleOutpts.Length.
        /// If R != O, a warning line is appended and only min(R, O) aligned pairs are printed, followed by
        /// any unpaired rules or outputs (tagged as UNPAIRED). Callers should normally ensure R == O by
        /// using the same evaluator that produced the outputs.
        /// Example aligned line:
        ///   If(A And B) Then(Q)::0.62::0.62
        /// Example unpaired line:
        ///   UNPAIRED RULE[7]: If(C) Then(R)
        /// </remarks>
        static public void DiagnosticPrintRuleSet<T>(FuzzyRuleSet<T> fzRuleSet, FuzzyValue<T>[] fzRuleOutpts, System.Text.StringBuilder sb) where T : struct, IConvertible
        {
            Type typ = typeof(T);
            sb.AppendLine(typ.Name);

            if (fzRuleSet == null || fzRuleSet.Rules == null || fzRuleSet.Rules.Count == 0 ||
                fzRuleOutpts == null || fzRuleOutpts.Length == 0)
            {
                sb.AppendLine("null");
                return;
            }

            var rules = fzRuleSet.Rules;
            int R = rules.Count;
            int O = fzRuleOutpts.Length;
            int N = Mathf.Min(R, O);

            if (R != O)
            {
                sb.AppendLine($"warning: length mismatch rules={R}, outputs={O}; printing {N} aligned pairs and listing unpaired items");
            }

            for (int i = 0; i < N; ++i)
            {
                var rule = rules[i];
                var outv = fzRuleOutpts[i];
                string ruleStr = rule != null ? rule.ToString() : "<null-rule>";
                float deg = outv.membershipDegree;
                float conf = outv.Confidence;
                sb.AppendLine($"{ruleStr}::{deg:0.00}::{conf:0.00}");
            }

            // List any unpaired rules
            if (R > N)
            {
                for (int i = N; i < R; ++i)
                {
                    var rule = rules[i];
                    string ruleStr = rule != null ? rule.ToString() : "<null-rule>";
                    sb.AppendLine($"UNPAIRED RULE[{i}]: {ruleStr}");
                }
            }

            // List any unpaired outputs
            if (O > N)
            {
                for (int i = N; i < O; ++i)
                {
                    var outv = fzRuleOutpts[i];
                    sb.AppendLine($"UNPAIRED OUTPUT[{i}]: deg={outv.membershipDegree:0.00}, conf={outv.Confidence:0.00}");
                }
            }
        }



        /// <summary>
        /// Render an ASCII chart for an arbitrary fuzzy set by sampling each membership function.
        /// It infers the x-domain from known membership function types, including discrete terms.
        /// Discrete terms are rendered as vertical bars at their representative value.
        /// 
        /// Example output:
        /// 
        /// __________-                        /-                        /__________
        ///            \-                   /--  \--                   /-           
        ///              \-               /-        \-               /-             
        ///                \--         /--            \--         /--               
        ///                   \-     /-                  \-     /-                  
        ///                     \--/-                      \-/--                    
        ///                     /--\-                      /-\--                    
        ///                   /-     \-                  /-     \-                  
        ///                /--         \--            /--         \--               
        ///              /-               \-        /-               \-             
        ///            /-                   \--  /--                   \-           
        /// ________________________________________________________________________
        /// A        B                         CD                         E        F
        /// Key: A: -2.5, B: -1.875, C: -0.01, D: 0.01, E: 1.875, F: 2.5
        /// Terms:
        /// Negative: Shoulder(A, B, C, F)  H=[1, 0]
        /// Zero: Trapezoid(B, C, D, E)
        /// Positive: Shoulder(A, D, E, F)  H=[0, 1]
        /// Domain: [-2.5, 2.5]
        /// </summary>
        public static string RenderFuzzySetAscii<T>(FuzzySet<T> set, int width = 72, int height = 12)
            where T : struct, IConvertible
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            var labels = (T[])Enum.GetValues(typeof(T));
            int N = labels.Length;
            if (N < 1) throw new ArgumentException("Need at least 1 label for visualization");

            // --- Infer global domain [minX, maxX] from known membership types ---
            bool gotAny = false;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            // Also collect discrete representative positions to ensure they appear in-domain
            var discreteCols = new Dictionary<int, float>(); // index -> repX
            for (int i = 0; i < N; i++)
            {
                var term = set.Get(labels[i]);
                if (term == null || term.MembershipFunction == null) continue;

                var fx = term.MembershipFunction;
                switch (fx)
                {
                    case TriangleMembershipFunction tri:
                        minX = Mathf.Min(minX, tri.LeftX);
                        maxX = Mathf.Max(maxX, tri.RightX);
                        gotAny = true;
                        break;
                    case TrapezoidMembershipFunction trap:
                        minX = Mathf.Min(minX, trap.MinX);
                        maxX = Mathf.Max(maxX, trap.MaxX);
                        gotAny = true;
                        break;
                    case ShoulderMembershipFunction sh:
                        minX = Mathf.Min(minX, sh.MinX);
                        maxX = Mathf.Max(maxX, sh.MaxX);
                        gotAny = true;
                        break;
                    case DiscreteMembershipFunction disc:
                        // Track rep value; we'll ensure domain encompasses it
                        float x0 = disc.RepresentativeValue;
                        minX = Mathf.Min(minX, x0);
                        maxX = Mathf.Max(maxX, x0);
                        gotAny = true;
                        break;
                    default:
                        // Unknown type: fall back later
                        break;
                }
            }
            if (!gotAny || !(maxX > minX))
            {
                // Fallback domain and avoid zero span
                minX = -1f; maxX = 1f;
            }
            if (Mathf.Abs(maxX - minX) < 1e-6f)
            {
                minX -= 0.5f; maxX += 0.5f;
            }

            // Sampling grid
            int w = Mathf.Max(2, width);
            int h = Mathf.Max(6, height);
            float dx = (maxX - minX) / (w - 1);

            // Sample memberships per column; discrete handled specially
            float[,] mu = new float[N, w];
            bool[] isDiscrete = new bool[N];
            int[] discCol = new int[N];

            for (int i = 0; i < N; i++)
            {
                var term = set.Get(labels[i]);
                if (term == null || term.MembershipFunction == null) continue;

                if (term.MembershipFunction is DiscreteMembershipFunction disc)
                {
                    isDiscrete[i] = true;
                    int c = Mathf.RoundToInt((disc.RepresentativeValue - minX) / (maxX - minX) * (w - 1));
                    c = Mathf.Clamp(c, 0, w - 1);
                    discCol[i] = c;
                    float d = Mathf.Clamp01(disc.Degree);
                    for (int xCol = 0; xCol < w; xCol++) mu[i, xCol] = 0f; // zero elsewhere
                    mu[i, c] = d; // vertical impulse will be drawn later
                }
                else
                {
                    for (int xCol = 0; xCol < w; xCol++)
                    {
                        float x = minX + dx * xCol;
                        float val = 0f;
                        try { val = term.MembershipFunction.fX(x); }
                        catch { val = 0f; }
                        mu[i, xCol] = Mathf.Clamp01(val);
                    }
                }
            }

            // Grid init
            var grid = new char[h, w];
            for (int r = 0; r < h; r++) for (int c = 0; c < w; c++) grid[r, c] = ' ';

            // Draw plateaus for continuous functions (top underscores near 1.0)
            const float plateauThresh = 0.995f;
            for (int i = 0; i < N; i++)
            {
                if (isDiscrete[i]) continue;
                int rPlateau = h - 1; // top row
                int runStart = -1;
                for (int c = 0; c < w; c++)
                {
                    bool high = mu[i, c] >= plateauThresh;
                    if (high && runStart < 0) runStart = c;
                    if ((!high || c == w - 1) && runStart >= 0)
                    {
                        int runEnd = high && c == w - 1 ? c : c - 1;
                        for (int k = runStart; k <= runEnd; k++) grid[rPlateau, k] = '_';
                        runStart = -1;
                    }
                }
            }

            // Draw continuous membership functions by rasterizing piecewise-linear segments column-to-column
            // This mirrors the visual style of the crossfade renderer but is robust to sampling resolution.
            Action<int, int, char> Plot = (cc, rr, ch) =>
            {
                if (cc < 0 || cc >= w || rr < 0 || rr >= h) return;
                if (grid[rr, cc] == ' ') grid[rr, cc] = ch;
            };

            for (int i = 0; i < N; i++)
            {
                if (isDiscrete[i]) continue;
                int prevR = Mathf.RoundToInt(mu[i, 0] * (h - 1));
                for (int c = 1; c < w; c++)
                {
                    int currR = Mathf.RoundToInt(mu[i, c] * (h - 1));
                    int x0 = c - 1, y0 = prevR;
                    int x1 = c, y1 = currR;

                    int dxSeg = x1 - x0;
                    int dySeg = y1 - y0;
                    int steps = Mathf.Max(Mathf.Abs(dxSeg), Mathf.Abs(dySeg));
                    if (steps <= 0)
                    {
                        Plot(x1, y1, '-');
                        prevR = currR;
                        continue;
                    }
                    for (int s = 0; s <= steps; s++)
                    {
                        float t = (float)s / steps;
                        int xx = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                        int yy = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                        char ch;
                        if (y1 > y0) ch = '/';
                        else if (y1 < y0) ch = '\\';
                        else ch = '-';
                        Plot(xx, yy, ch);
                    }

                    prevR = currR;
                }
            }

            // Baseline row first so discrete bars can overwrite at their column
            for (int c = 0; c < w; c++) grid[0, c] = '_';

            // Draw discrete terms as vertical bars at their representative column up to Degree
            // Always show at least a 1-cell tick at the baseline for Degree == 0 so position is visible
            for (int i = 0; i < N; i++)
            {
                if (!isDiscrete[i]) continue;
                int c = discCol[i];
                float d = Mathf.Clamp01(mu[i, c]);
                int rTop = Mathf.RoundToInt(d * (h - 1));
                if (rTop <= 0)
                {
                    // Degree == 0 → draw a single tick on the baseline
                    grid[0, c] = '|';
                }
                else
                {
                    // Positive degree → extend to the exact rasterized height
                    for (int r = 0; r <= rTop; r++)
                    {
                        grid[r, c] = '|';
                    }
                }
            }

            // Build output
            var sb = new System.Text.StringBuilder(h * (w + 2) + 256);
            for (int r = h - 1; r >= 0; r--)
            {
                for (int c = 0; c < w; c++) sb.Append(grid[r, c]);
                sb.Append('\n');
            }

            // Build breakpoint list from membership functions so we can label A.. like the crossfade renderer
            List<float> breaks = new List<float>(4 * N);
            void AddBreak(float v)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return;
                breaks.Add(v);
            }
            for (int i = 0; i < N; i++)
            {
                var term = set.Get(labels[i]);
                if (term == null || term.MembershipFunction == null) continue;
                switch (term.MembershipFunction)
                {
                    case TriangleMembershipFunction tri:
                        AddBreak(tri.LeftX); AddBreak(tri.PeakX); AddBreak(tri.RightX);
                        break;
                    case TrapezoidMembershipFunction trap:
                        AddBreak(trap.MinX); AddBreak(trap.PlateauBeginX); AddBreak(trap.PlateauEndX); AddBreak(trap.MaxX);
                        break;
                    case ShoulderMembershipFunction sh:
                        AddBreak(sh.MinX); AddBreak(sh.LeftPlateauEndX); AddBreak(sh.RightPlateauBeginX); AddBreak(sh.MaxX);
                        break;
                    case DiscreteMembershipFunction disc:
                        AddBreak(disc.RepresentativeValue);
                        break;
                }
            }
            // Ensure domain bounds are included
            AddBreak(minX); AddBreak(maxX);
            // Sort and de-duplicate with small epsilon
            breaks.Sort();
            const float eps = 1e-4f;
            List<float> uniq = new List<float>(breaks.Count);
            for (int i = 0; i < breaks.Count; i++)
            {
                if (uniq.Count == 0 || Mathf.Abs(breaks[i] - uniq[uniq.Count - 1]) > eps)
                    uniq.Add(breaks[i]);
            }
            // Map value to letter index
            int IndexFor(float v)
            {
                int best = 0; float bestErr = float.PositiveInfinity;
                for (int i = 0; i < uniq.Count; i++)
                {
                    float err = Mathf.Abs(uniq[i] - v);
                    if (err < bestErr) { bestErr = err; best = i; }
                }
                return best;
            }
            int ColOfVal(float v)
            {
                return Mathf.Clamp(Mathf.RoundToInt((v - minX) / (maxX - minX) * (w - 1)), 0, w - 1);
            }

            // Baseline labels: A.. at each unique breakpoint
            var labelLine = new char[w];
            for (int c = 0; c < w; c++) labelLine[c] = ' ';
            int[] cols = new int[uniq.Count];
            for (int i = 0; i < uniq.Count; i++) cols[i] = ColOfVal(uniq[i]);
            // Resolve collisions stably left-to-right
            for (int i = 1; i < cols.Length; i++)
                if (cols[i] <= cols[i - 1]) cols[i] = Mathf.Min(w - 1, cols[i - 1] + 1);
            for (int i = 0; i < uniq.Count && i < 26; i++)
            {
                labelLine[cols[i]] = (char)('A' + i);
            }
            sb.Append(new string(labelLine)).Append('\n');

            // Key: numeric mapping for letters
            sb.Append("Key: ");
            for (int i = 0; i < uniq.Count && i < 26; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append((char)('A' + i)).Append(": ").Append(uniq[i].ToString("0.###"));
            }
            sb.Append('\n');

            // Extended key: one line per term with type and control letters
            sb.Append("Terms:\n");
            for (int i = 0; i < N; i++)
            {
                var term = set.Get(labels[i]);
                if (term == null || term.MembershipFunction == null) continue;
                string name = labels[i].ToString();
                switch (term.MembershipFunction)
                {
                    case TriangleMembershipFunction tri:
                        char a = (char)('A' + IndexFor(tri.LeftX));
                        char b = (char)('A' + IndexFor(tri.PeakX));
                        char d = (char)('A' + IndexFor(tri.RightX));
                        sb.Append(name).Append(": Triangle(")
                          .Append(a).Append(", ").Append(b).Append(", ").Append(d).Append(")  H=[")
                          .Append(tri.LeftHeight.ToString("0.###")).Append(", ")
                          .Append(tri.PeakHeight.ToString("0.###")).Append(", ")
                          .Append(tri.RightHeight.ToString("0.###")).Append("]  RV=")
                          .Append(tri.RepresentativeValue.ToString("0.###")).Append('\n');
                        break;
                    case TrapezoidMembershipFunction trap:
                        char A1 = (char)('A' + IndexFor(trap.MinX));
                        char B1 = (char)('A' + IndexFor(trap.PlateauBeginX));
                        char C1 = (char)('A' + IndexFor(trap.PlateauEndX));
                        char D1 = (char)('A' + IndexFor(trap.MaxX));
                        sb.Append(name).Append(": Trapezoid(")
                          .Append(A1).Append(", ").Append(B1).Append(", ").Append(C1).Append(", ").Append(D1)
                          .Append(")  H=[")
                          .Append(trap.LeftValleyHeight.ToString("0.###")).Append(", ")
                          .Append(trap.PlateauHeight.ToString("0.###")).Append(", ")
                          .Append(trap.RightValleyHeight.ToString("0.###")).Append("]  RV=")
                          .Append(trap.RepresentativeValue.ToString("0.###")).Append('\n');
                        break;
                    case ShoulderMembershipFunction sh:
                        char As = (char)('A' + IndexFor(sh.MinX));
                        char Bs = (char)('A' + IndexFor(sh.LeftPlateauEndX));
                        char Cs = (char)('A' + IndexFor(sh.RightPlateauBeginX));
                        char Ds = (char)('A' + IndexFor(sh.MaxX));
                        sb.Append(name).Append(": Shoulder(")
                          .Append(As).Append(", ").Append(Bs).Append(", ").Append(Cs).Append(", ").Append(Ds)
                          .Append(")  H=[")
                          .Append(sh.LeftPlateauHeight.ToString("0.###")).Append(", ")
                          .Append(sh.RightPlateauHeight.ToString("0.###")).Append("]  RV=")
                          .Append(sh.RepresentativeValue.ToString("0.###")).Append('\n');
                        break;
                    case DiscreteMembershipFunction disc:
                        char X = (char)('A' + IndexFor(disc.RepresentativeValue));
                        sb.Append(name).Append(": Discrete(")
                          .Append(X).Append(")  deg=")
                          .Append(Mathf.Clamp01(disc.Degree).ToString("0.###")).Append("  RV=")
                          .Append(disc.RepresentativeValue.ToString("0.###")).Append('\n');
                        break;
                    default:
                        sb.Append(name).Append(": Unknown\n");
                        break;
                }
            }

            // Domain at the end for completeness
            sb.Append("Domain: [").Append(minX.ToString("0.###")).Append(", ").Append(maxX.ToString("0.###")).Append("]\n");

            return sb.ToString();
        }

    }

}
