using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices; 

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
    public static class FuzzyCrossfade
    {

        /// <summary>
        /// Build a fuzzy set for enum T using tuple-or-float specs, one per enum label in order.
        /// Each spec is either:
        ///   - float or double: triangle peak at that x (B == C)
        ///   - (float,float) or compatible numeric tuple: plateau top [left,right]
        /// Notes:
        ///   - The first and last labels are shoulders. If their plateau has zero length (left == right)
        ///     the shoulder collapses to a triangle at that x.
        ///   - Internally converts specs into 2*N breakpoints and stitches shoulders, triangles, and trapezoids so adjacent labels cross-fade without gaps.
        /// Throws:
        ///   - ArgumentNullException if specs is null
        ///   - ArgumentException if the spec count does not match the enum size or if a plateau has right < left
        /// </summary>
        public static FuzzySet<T> GenerateCrossfadeFuzzySet<T>(params object[] specs)
            where T : struct, IConvertible
        {
            return GenerateCrossfadeFuzzySet<T>((IList<object>)specs, null);
        }

        /// <summary>
        /// Build a fuzzy set using tuple-or-float specs, with optional peak heights per label.
        /// The heights list may be null (defaults all to 1). If provided, it must have the same length
        /// as specs and each value must be in [0,1]. Each height sets the plateau/triangle peak level
        /// for the corresponding label.
        /// </summary>
        public static FuzzySet<T> GenerateCrossfadeFuzzySet<T>(IList<object> specs, IList<float> heights)
            where T : struct, IConvertible
        {
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            var labels = (T[])Enum.GetValues(typeof(T));
            int N = labels.Length;
            if (specs.Count != N)
                throw new ArgumentException($"Expected {N} segments for enum '{typeof(T).Name}', got {specs.Count}");
            if (N < 2)
                throw new ArgumentException("At least 2 labels are required (left and right shoulders).");

            // Validate heights if provided
            List<float> H = new List<float>(N);
            if (heights != null)
            {
                if (heights.Count != N)
                    throw new ArgumentException($"Heights length ({heights.Count}) must equal enum size ({N})");
                for (int i = 0; i < N; i++)
                {
                    float h = heights[i];
                    if (h < 0f || h > 1f)
                        throw new ArgumentException($"Height at index {i} out of range [0,1]: {h}");
                    H.Add(h);
                }
            }
            else
            {
                for (int i = 0; i < N; i++) H.Add(1f);
            }

            // Build breakpoints P from specs
            var P = new List<float>(2 * N);
            for (int i = 0; i < specs.Count; i++)
            {
                object s = specs[i];
                if (TryGetNumericPair(s, out float left, out float right))
                {
                    if (right < left)
                        throw new ArgumentException($"Plateau requires left<=right at segment {i}: left={left}, right={right}");
                    P.Add(left);  // B
                    P.Add(right); // C
                }
                else if (TryToFloatWithWarning(s, out float val, out bool warn, out string src))
                {
                    if (warn && s is not float)
                        Debug.LogWarning($"FuzzyCrossfade: Converting scalar of type {src} to float; precision may be lost.");
                    P.Add(val); P.Add(val); // triangle apex at val
                }
                else
                {
                    throw new ArgumentException($"Invalid spec at index {i}. Use a numeric value for a triangle or a 2-tuple of numerics for a plateau.");
                }
            }

            return GenerateCrossfadeByBreaks<T>(P, H);
        }
        /// <summary>
        /// Build a fuzzy set from breakpoints P with per-label peak heights H (length N, values in [0,1]).
        /// Heights set the top value for each label's plateau or triangle apex. See GenerateCrossfadeByBreaks for P rules.
        /// </summary>
        internal static FuzzySet<T> GenerateCrossfadeByBreaks<T>(IList<float> P, IList<float> H)
            where T : struct, IConvertible
        {
            if (H == null) throw new ArgumentNullException(nameof(H));
            var labels = (T[])Enum.GetValues(typeof(T));
            int N = labels.Length;
            if (H.Count != N) throw new ArgumentException($"Heights length ({H.Count}) must equal enum size ({N})");
            // Reuse validation of P by calling the no-heights variant (it throws if invalid)
            // but avoid double work by copying its validation logic minimally if desired.
            // Here we simply replicate the length check and monotonicity to keep independence.
            if (P == null) throw new ArgumentNullException(nameof(P));
            if (P.Count != 2 * N) throw new ArgumentException($"Expected {2 * N} breakpoints for {N} labels, got {P.Count}");
            for (int i = 1; i < P.Count; i++)
            {
                if (P[i] < P[i - 1])
                    throw new ArgumentException("Breakpoints must be non-decreasing");

                if (P[i] == P[i - 1])
                {
                    // Allowed equalities:
                    //  - interior triangle apex pair (B==C): i == 2*k+1 for k=1..N-2
                    //  - zero-length shoulder plateaus: (P0==P1) → i==1, (P[2N-2]==P[2N-1]) → i==2N-1
                    //  - exact junctions where a triangle meets a shoulder: i==2 (left junction), i==2N-2 (right junction)
                    bool allowedEquality = (i == 1) || (i == 2 * N - 1) || (i == 2) || (i == 2 * N - 2);
                    if (!allowedEquality)
                    {
                        for (int k = 1; k <= N - 2; k++)
                        {
                            int apexRightIndex = 2 * k + 1;
                            if (i == apexRightIndex) { allowedEquality = true; break; }
                        }
                    }

                    if (!allowedEquality)
                        throw new ArgumentException($"Equality only allowed at interior triangle apex (B==C), shoulder plateaus, or triangle-shoulder junctions. Offending indices {i - 1} and {i}.");
                }
            }
            for (int i = 0; i < N; i++)
            {
                float h = H[i];
                if (h < 0f || h > 1f) throw new ArgumentException($"Height at index {i} out of range [0,1]: {h}");
            }

            float minX = P[0];
            float maxX = P[2 * N - 1];
            var set = new FuzzySet<T>();

            // Left shoulder uses H[0]
            set.Set(labels[0], new ShoulderMembershipFunction(
                minX,
                P[1],               // leftPlateauEndX
                P[2],               // rightPlateauBeginX
                maxX,
                H[0],               // leftPlateauHeight
                0f                  // rightPlateauHeight
            ));

            // Interiors i = 1..N-2 use H[i]
            for (int i = 1; i <= N - 2; i++)
            {
                int j = i - 1;
                float A = P[2 * j + 1];
                float B = P[2 * j + 2];
                float C = P[2 * j + 3];
                float D = P[2 * j + 4];
                float h = H[i];
                if (B == C)
                {
                    set.Set(labels[i], new TriangleMembershipFunction(
                        A,   // leftX
                        B,   // peakX
                        D,   // rightX
                        0f,  // leftHeight
                        h,   // peakHeight
                        0f   // rightHeight
                    ));
                }
                else
                {
                    set.Set(labels[i], new TrapezoidMembershipFunction(
                        A,  // minX
                        B,  // plateauBeginX
                        C,  // plateauEndX
                        D,  // maxX
                        0f, // leftValleyHeight
                        h,  // plateauHeight
                        0f  // rightValleyHeight
                    ));
                }
            }

            // Right shoulder uses H[N-1]
            set.Set(labels[N - 1], new ShoulderMembershipFunction(
                minX,
                P[2 * N - 3],       // leftPlateauEndX
                P[2 * N - 2],       // rightPlateauBeginX
                maxX,
                0f,                 // leftPlateauHeight
                H[N - 1]            // rightPlateauHeight
            ));

            return set;
        }



        // /// <summary>
        // /// Render an ASCII chart of the cross-fade defined by the given tuple-or-float specs.
        // /// Uses the same spec rules as GenerateCrossfadeFuzzySet.
        // /// Drawing rules:
        // ///   - Top plateaus are drawn at height 1.0 (top row) using breakpoints (never missed by sampling).
        // ///   - Edges are drawn with a single '/' or '\\' per scanline using threshold crossings.
        // ///   - Baseline is a solid '_' row.
        // ///   - Bottom line prints A.. letters at breakpoint columns and a numeric key.
        // /// Example:
        // /// __________                         __                         __________
        // ///             \                    /     \                    /           
        // ///               \                /         \                /             
        // ///                  \           /             \           /                
        // ///                    \      /                   \      /                  
        // ///                      \  /                       \  /                    
        // ///                      /  \                       /  \                    
        // ///                    /      \                   /      \                  
        // ///                  /           \             /           \                
        // ///               /                \         /                \             
        // ///             /                    \     /                    \           
        // /// ________________________________________________________________________
        // /// A        B                         CD                         E        F
        // /// Key: A: -2.5, B: -1.875, C: -0.01, D: 0.01, E: 1.875, F: 2.5
        // /// 
        // /// Throws:
        // ///   - ArgumentNullException if specs is null
        // ///   - ArgumentException if spec count does not match the enum size
        // /// </summary>
        // public static string RenderCrossfadeAscii<T>(object[] specs, int width = 72, int height = 12)
        //     where T : struct, IConvertible
        // {
        //     if (specs == null) throw new ArgumentNullException(nameof(specs));
        //     var P = BuildBreakpointsFromSpecs(specs, typeof(T));
        //     return RenderCrossfadeAscii<T>(P, null, width, height);
        // }

        // /// <summary>
        // /// Render ASCII from tuple-or-float specs with optional per-label heights in [0,1].
        // /// </summary>
        // public static string RenderCrossfadeAscii<T>(object[] specs, IList<float> heights, int width = 72, int height = 12)
        //     where T : struct, IConvertible
        // {
        //     if (specs == null) throw new ArgumentNullException(nameof(specs));
        //     var P = BuildBreakpointsFromSpecs(specs, typeof(T));
        //     return RenderCrossfadeAscii<T>(P, heights, width, height);
        // }





        /// <summary>
        /// Build a fuzzy set from breakpoints P for enum T.
        /// Contract:
        ///   - Let N be the number of labels. P must have length 2*N.
        ///   - P is non-decreasing. Equality is allowed only at interior triangle apex pairs (B == C)
        ///     and at collapsed shoulder plateaus (P0 == P1, P[2N-2] == P[2N-1]).
        /// Construction:
        ///   - Left shoulder: (P0,1), (P1,1), (P2,0)
        ///   - Interiors i=1..N-2: trapezoid (A,0)(B,1)(C,1)(D,0) with A=P[2i-1],B=P[2i],C=P[2i+1],D=P[2i+2];
        ///     emit a triangle if B == C.
        ///   - Right shoulder: (P[2N-3],0), (P[2N-2],1), (P[2N-1],1)
        /// Throws:
        ///   - ArgumentNullException if P is null
        ///   - ArgumentException if P length is wrong or ordering rules are violated
        /// </summary>
        internal static FuzzySet<T> GenerateCrossfadeByBreaks<T>(IList<float> P)
            where T : struct, IConvertible
        {
            if (P == null) throw new ArgumentNullException(nameof(P));
            var labels = (T[])Enum.GetValues(typeof(T));
            int N = labels.Length;
            if (N < 2)
                throw new ArgumentException("At least 2 labels are required (left and right shoulders).");
            if (P.Count != 2 * N)
                throw new ArgumentException($"Expected {2 * N} breakpoints for {N} labels, got {P.Count}");

            // Non-decreasing overall. Allow equality at interior triangle apex pair (B==C) and at shoulder plateaus.
            for (int i = 1; i < P.Count; i++)
            {
                if (P[i] < P[i - 1])
                    throw new ArgumentException("Breakpoints must be non-decreasing");

                if (P[i] == P[i - 1])
                {
                    // Allowed equalities:
                    //  - interior triangle apex pair (B==C): i == 2*k+1 for k=1..N-2
                    //  - zero-length shoulder plateaus: (P0==P1) → i==1, (P[2N-2]==P[2N-1]) → i==2N-1
                    //  - exact junctions where a triangle meets a shoulder: i==2 (left junction), i==2N-2 (right junction)
                    bool allowedEquality = (i == 1) || (i == 2 * N - 1) || (i == 2) || (i == 2 * N - 2);
                    if (!allowedEquality)
                    {
                        for (int k = 1; k <= N - 2; k++)
                        {
                            int apexRightIndex = 2 * k + 1; // triangle apex right index
                            if (i == apexRightIndex) { allowedEquality = true; break; }
                        }
                    }

                    if (!allowedEquality)
                        throw new ArgumentException($"Equality only allowed at interior triangle apex (B==C), shoulder plateaus, or triangle-shoulder junctions. Offending indices {i - 1} and {i}.");
                }
            }

            float minX = P[0];
            float maxX = P[2 * N - 1];
            var set = new FuzzySet<T>();

            // Left shoulder: (P0,1) (P1,1) (P2,0)
            set.Set(labels[0], new ShoulderMembershipFunction(
                minX,
                P[1],               // leftPlateauEndX
                P[2],               // rightPlateauBeginX
                maxX,
                1f,                 // leftPlateauHeight
                0f                  // rightPlateauHeight
            ));

            // Interior labels: i = 1..N-2
            for (int i = 1; i <= N - 2; i++)
            {
                int j = i - 1; // interior index 0..N-3 for convenience
                float A = P[2 * j + 1]; // left base
                float B = P[2 * j + 2]; // left top (or apex)
                float C = P[2 * j + 3]; // right top (or apex)
                float D = P[2 * j + 4]; // right base

                if (B == C)
                {
                    // Triangle (A,0) (B,1) (D,0)
                    set.Set(labels[i], new TriangleMembershipFunction(
                        A,  // leftX
                        B,  // peakX
                        D   // rightX (heights default to 0,1,0)
                    ));
                }
                else
                {
                    // Trapezoid (A,0) (B,1) (C,1) (D,0)
                    set.Set(labels[i], new TrapezoidMembershipFunction(
                        A,  // minX
                        B,  // plateauBeginX
                        C,  // plateauEndX
                        D,  // maxX
                        0f, // leftValleyHeight
                        1f, // plateauHeight
                        0f  // rightValleyHeight
                    ));
                }
            }

            // Right shoulder: (P[2N-3],0) (P[2N-2],1) (P[2N-1],1)
            set.Set(labels[N - 1], new ShoulderMembershipFunction(
                minX,
                P[2 * N - 3],       // leftPlateauEndX
                P[2 * N - 2],       // rightPlateauBeginX
                maxX,
                0f,                 // leftPlateauHeight
                1f                  // rightPlateauHeight
            ));

            return set;
        }

        /// <summary>
        /// Try to read a numeric pair from obj. Supports (float,float), (double,double), (float,double), (double,float).
        /// Returns true and sets left,right on success.
        /// </summary>
        internal static bool TryGetNumericPair(object obj, out float left, out float right)
        {
            left = right = 0f;
            if (obj is ITuple tup && tup.Length == 2)
            {
                object a = tup[0];
                object b = tup[1];
                if (TryToFloatWithWarning(a, out float la, out bool warnA, out string typeA) &&
                    TryToFloatWithWarning(b, out float rb, out bool warnB, out string typeB))
                {
                    if (warnA && a is not float)
                        Debug.LogWarning($"FuzzyCrossfade: Converting tuple element 0 of type {typeA} to float; precision may be lost.");
                    if (warnB && b is not float)
                        Debug.LogWarning($"FuzzyCrossfade: Converting tuple element 1 of type {typeB} to float; precision may be lost.");
                    left = la; right = rb; return true;
                }
            }
            return false;
        }



        // /// <summary>
        // /// Render an ASCII chart from breakpoints P (length 2*N).
        // /// See GenerateCrossfadeByBreaks for the P contract. This overload is intended for internal use.
        // /// </summary>
        // internal static string RenderCrossfadeAscii<T>(IList<float> P, IList<float> H, int width = 72, int height = 12)
        //     where T : struct, IConvertible
        // {
        //     if (P == null) throw new ArgumentNullException(nameof(P));
        //     var labels = (T[])Enum.GetValues(typeof(T));
        //     int N = labels.Length;
        //     if (N < 2) throw new ArgumentException("Need at least 2 labels for visualization");
        //     if (P.Count != 2 * N) throw new ArgumentException($"Expected {2 * N} breakpoints for {N} labels, got {P.Count}");

        //     List<float> heightsVec = null;
        //     if (H != null)
        //     {
        //         if (H.Count != N) throw new ArgumentException($"Heights length ({H.Count}) must equal enum size ({N})");
        //         heightsVec = new List<float>(N);
        //         for (int i = 0; i < N; i++)
        //         {
        //             float hV = Mathf.Clamp01(H[i]);
        //             heightsVec.Add(hV);
        //         }
        //     }

        //     FuzzySet<T> set;
        //     if (heightsVec != null)
        //     {
        //         set = GenerateCrossfadeByBreaks<T>(P, heightsVec);
        //     }
        //     else
        //     {
        //         set = GenerateCrossfadeByBreaks<T>(P);
        //     }

        //     float minX = P[0];
        //     float maxX = P[2 * N - 1];
        //     float dx = (maxX - minX) / (width - 1);
        //     int h = Mathf.Max(6, height); // leave room for baseline and label row

        //     // Sample memberships per column
        //     float[,] mu = new float[N, width];
        //     for (int xCol = 0; xCol < width; xCol++)
        //     {
        //         float x = minX + dx * xCol;
        //         for (int i = 0; i < N; i++)
        //         {
        //             var term = set.Get(labels[i]);
        //             mu[i, xCol] = Mathf.Clamp01(term.MembershipFunction.fX(x));
        //         }
        //     }

        //     // Grid
        //     var grid = new char[h, width];
        //     for (int r = 0; r < h; r++)
        //         for (int c = 0; c < width; c++)
        //             grid[r, c] = ' ';

        //     // Top row: deterministically draw flat tops '_' based on breakpoints so small plateaus always appear
        //     int ColOf(float x) => Mathf.Clamp(Mathf.RoundToInt((x - minX) / (maxX - minX) * (width - 1)), 0, width - 1);

        //     // Left shoulder plateau: [P0, P1] (skip if zero-length)
        //     if (P[0] != P[1])
        //     {
        //         int rPlateau = h - 1;
        //         if (heightsVec != null) rPlateau = Mathf.Clamp(Mathf.RoundToInt(heightsVec[0] * (h - 1)), 0, h - 1);
        //         int c0 = ColOf(P[0]);
        //         int c1 = ColOf(P[1]);
        //         for (int c = Mathf.Min(c0, c1); c <= Mathf.Max(c0, c1); c++) grid[rPlateau, c] = '_';
        //     }

        //     // Interior plateaus for trapezoids: for label i (1..N-2), flat top is [P[2i], P[2i+1]] when B<C
        //     for (int i = 1; i <= N - 2; i++)
        //     {
        //         float B = P[2 * i];
        //         float C = P[2 * i + 1];
        //         if (B < C)
        //         {
        //             int rPlateau = h - 1;
        //             if (heightsVec != null) rPlateau = Mathf.Clamp(Mathf.RoundToInt(heightsVec[i] * (h - 1)), 0, h - 1);
        //             int cB = ColOf(B);
        //             int cC = ColOf(C);
        //             for (int c = Mathf.Min(cB, cC); c <= Mathf.Max(cB, cC); c++) grid[rPlateau, c] = '_';
        //         }
        //     }

        //     // Right shoulder plateau: [P[2N-2], P[2N-1]] (skip if zero-length)
        //     if (P[2 * N - 2] != P[2 * N - 1])
        //     {
        //         int rPlateau = h - 1;
        //         if (heightsVec != null) rPlateau = Mathf.Clamp(Mathf.RoundToInt(heightsVec[N - 1] * (h - 1)), 0, h - 1);
        //         int c0 = ColOf(P[2 * N - 2]);
        //         int c1 = ColOf(P[2 * N - 1]);
        //         for (int c = Mathf.Min(c0, c1); c <= Mathf.Max(c0, c1); c++) grid[rPlateau, c] = '_';
        //     }

        //     // For each scanline y in (0,1), find crossings per function and place one glyph
        //     for (int r = 1; r < h - 1; r++)
        //     {
        //         float y = (float)r / (h - 1); // 0..1
        //         for (int i = 0; i < N; i++)
        //         {
        //             // One crossing per monotone segment → we detect sign change relative to y
        //             for (int c = 1; c < width; c++)
        //             {
        //                 float prev = mu[i, c - 1];
        //                 float curr = mu[i, c];
        //                 if (prev < y && curr >= y)
        //                 {
        //                     // rising edge at c
        //                     int rr = r;
        //                     if (grid[rr, c] == ' ') grid[rr, c] = '/';
        //                 }
        //                 else if (prev >= y && curr < y)
        //                 {
        //                     // falling edge at c
        //                     int rr = r;
        //                     if (grid[rr, c] == ' ') grid[rr, c] = '\\';
        //                 }
        //             }
        //         }
        //     }

        //     // Baseline row r=0: underscores
        //     for (int c = 0; c < width; c++)
        //         grid[0, c] = '_';

        //     // Build string for plot rows
        //     var sb = new System.Text.StringBuilder(h * (width + 2) + 256);
        //     for (int r = h - 1; r >= 0; r--)
        //     {
        //         for (int c = 0; c < width; c++) sb.Append(grid[r, c]);
        //         sb.Append('\n');
        //     }

        //     // Labels row under the baseline
        //     int bpCount = P.Count;
        //     var bpCols = new int[bpCount];
        //     for (int k = 0; k < bpCount; k++)
        //     {
        //         int col = Mathf.RoundToInt((P[k] - minX) / (maxX - minX) * (width - 1));
        //         bpCols[k] = Mathf.Clamp(col, 0, width - 1);
        //     }
        //     // Resolve collisions (stable, left-to-right)
        //     for (int k = 1; k < bpCount; k++)
        //     {
        //         if (bpCols[k] <= bpCols[k - 1])
        //             bpCols[k] = Mathf.Min(width - 1, bpCols[k - 1] + 1);
        //     }

        //     // Compose labels line
        //     var labelLine = new char[width];
        //     for (int c = 0; c < width; c++) labelLine[c] = ' ';
        //     for (int k = 0; k < bpCount; k++)
        //     {
        //         int col = bpCols[k];
        //         labelLine[col] = (char)('A' + k);
        //     }
        //     sb.Append(new string(labelLine)).Append('\n');

        //     // Key
        //     sb.Append("Key: ");
        //     for (int k = 0; k < bpCount; k++)
        //     {
        //         if (k > 0) sb.Append(", ");
        //         sb.Append((char)('A' + k)).Append(": ").Append(P[k].ToString("0.###"));
        //     }
        //     sb.Append('\n');

        //     return sb.ToString();
        // }



        /// <summary>
        /// Convert tuple-or-float specs into breakpoints P for enumType. P has length 2*N.
        /// Validates tuple ordering (left <= right) and spec count.
        /// </summary>
        internal static List<float> BuildBreakpointsFromSpecs(object[] specs, Type enumType)
        {
            var labels = (Array)Enum.GetValues(enumType);
            int N = labels.Length;
            if (specs.Length != N)
                throw new ArgumentException($"Expected {N} segments for enum '{enumType.Name}', got {specs.Length}");

            var P = new List<float>(2 * N);
            for (int i = 0; i < specs.Length; i++)
            {
                object s = specs[i];
                if (TryGetNumericPair(s, out float left, out float right))
                {
                    if (right < left)
                        throw new ArgumentException($"Plateau requires left<=right at segment {i}: left={left}, right={right}");
                    P.Add(left); P.Add(right);
                }
                else if (s is float f)
                {
                    P.Add(f); P.Add(f); // triangle
                }
                else if (s is double d)
                {
                    float v = (float)d;
                    P.Add(v); P.Add(v); // triangle from double literal
                }
                else
                {
                    throw new ArgumentException($"Invalid spec at index {i}. Use a number for triangle or (left,right) tuple for plateau.");
                }
            }
            return P;
        }

        /// <summary>
        /// Convert any numeric IConvertible to float. Returns true on success.
        /// Sets shouldWarn=true if the source type is not float (Single), so precision loss is possible.
        /// </summary>
        private static bool TryToFloatWithWarning(object obj, out float val, out bool shouldWarn, out string srcType)
        {
            val = 0f; shouldWarn = false; srcType = null;
            if (obj == null || obj is not IConvertible) return false;
            var t = obj.GetType();
            try { val = Convert.ToSingle(obj); } catch { return false; }
            bool isFloat = t == typeof(float) || t == typeof(Single);
            shouldWarn = !isFloat; // warn for double, int, long, decimal, etc.
            srcType = t.Name;
            return true;
        }

    }

}