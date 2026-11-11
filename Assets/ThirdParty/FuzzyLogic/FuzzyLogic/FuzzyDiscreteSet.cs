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
    public static class FuzzyDiscreteSet
    {
        /// <summary>
        /// Convert any numeric IConvertible to float. Returns true on success.
        /// Sets shouldWarn=true if source type is not float.
        /// </summary>
        private static bool TryToFloatWithWarning(object obj, out float val, out bool shouldWarn, out string srcType)
        {
            val = 0f; shouldWarn = false; srcType = null;
            if (obj == null || obj is not IConvertible) return false;
            var t = obj.GetType();
            try { val = Convert.ToSingle(obj); } catch { return false; }
            bool isFloat = t == typeof(float) || t == typeof(Single);
            shouldWarn = !isFloat;
            srcType = t.Name;
            return true;
        }

        /// <summary>
        /// Helper: build a FuzzySet for an OUTPUT enum using discrete membership functions.
        /// Provide one representative value (X position) per enum label, in enum declaration order.
        /// Mirrors FuzzyCrossfade positional semantics (no labels).
        /// </summary>
        public static FuzzySet<T> GenerateDiscreteFuzzySet<T>(IList<float> representativeValues)
            where T : struct, IConvertible
        {
            if (representativeValues == null) throw new ArgumentNullException(nameof(representativeValues));
            var labels = (T[])Enum.GetValues(typeof(T));
            if (labels.Length != representativeValues.Count)
                throw new ArgumentException(
                    $"Enum '{typeof(T).Name}' count ({labels.Length}) must match representative values count ({representativeValues.Count})");

            var set = new FuzzySet<T>();
            for (int i = 0; i < labels.Length; i++)
                set.Set(labels[i], new DiscreteMembershipFunction(representativeValues[i]));
            return set;
        }

        /// <summary>
        /// Build a discrete fuzzy set for enum T using positional specs (one numeric representative value per enum label),
        /// mirroring FuzzyCrossfade's params object[] API. Each spec must be a numeric scalar (float/double/int/etc.).
        /// Example: GenerateDiscreteFuzzySet&lt;FzOut&gt;(-12f, 0f, +12f)
        /// </summary>
        public static FuzzySet<T> GenerateDiscreteFuzzySet<T>(params object[] specs)
            where T : struct, IConvertible
        {
            return GenerateDiscreteFuzzySet<T>((IList<object>)specs, null);
        }

        /// <summary>
        /// Build a discrete fuzzy set for enum T using positional specs with optional per-label DoM heights.
        /// The first list defines representative values (X positions) per label by enum order.
        /// The optional heights list provides initial DoM degrees per label in [0,1]. If omitted, defaults to 1.0 for all.
        /// Note: In typical Max-Average defuzzification, RepresentativeValue dominates; rule evaluation will set DoM at runtime.
        /// </summary>
        public static FuzzySet<T> GenerateDiscreteFuzzySet<T>(IList<object> specs, IList<float> heights)
            where T : struct, IConvertible
        {
            if (specs == null) throw new ArgumentNullException(nameof(specs));
            var labels = (T[])Enum.GetValues(typeof(T));
            int N = labels.Length;
            if (specs.Count != N)
                throw new ArgumentException($"Expected {N} representative values for enum '{typeof(T).Name}', got {specs.Count}");

            // Parse representative values from specs (numeric scalars only).
            var reps = new List<float>(N);
            for (int i = 0; i < specs.Count; i++)
            {
                object s = specs[i];
                if (!TryToFloatWithWarning(s, out float val, out bool warn, out string src))
                    throw new ArgumentException($"Invalid discrete spec at index {i}. Use a numeric scalar (float/double/int/etc.).");
                if (warn && s is not float)
                    Debug.LogWarning($"FuzzyDiscreteSet: Converting representative value of type {src} to float; precision may be lost.");
                reps.Add(val);
            }

            // Prepare initial DoM heights (optional). These initialize Degree but may be overwritten by rules at runtime.
            var H = new List<float>(N);
            if (heights != null)
            {
                if (heights.Count != N)
                    throw new ArgumentException($"Heights length ({heights.Count}) must equal enum size ({N})");
                for (int i = 0; i < N; i++)
                {
                    float h = Mathf.Clamp01(heights[i]);
                    H.Add(h);
                }
            }
            else
            {
                for (int i = 0; i < N; i++) H.Add(1f);
            }

            // Construct set in enum declaration order.
            var set = new FuzzySet<T>();
            for (int i = 0; i < N; i++)
            {
                var dm = new DiscreteMembershipFunction(reps[i])
                {
                    Degree = H[i]
                };
                set.Set(labels[i], dm);
            }
            return set;
        }

    }

}