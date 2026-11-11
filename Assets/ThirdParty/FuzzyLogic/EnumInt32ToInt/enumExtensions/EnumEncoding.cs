using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EnumEncoding
{

    internal static class EnumMapBuilder
    {
        public static IReadOnlyDictionary<T, int> BuildOrdinalMap<T>() where T : Enum
        {
            var values = Enum.GetValues(typeof(T)).Cast<T>().ToArray();
            var dict = new Dictionary<T, int>(values.Length);
            for (int i = 0; i < values.Length; i++) dict[values[i]] = i;
            return dict;
        }

        public static int[] ToIntArray<T>() where T : Enum
        {
            var vals = Enum.GetValues(typeof(T)).Cast<int>().ToArray();
            return vals;
        }
    }

    internal static class EnumBitPacking
    {
        public static ulong PackFlags(params int[] bits)
        {
            ulong acc = 0ul;
            foreach (var b in bits)
            {
                if (b >= 0 && b < 64) acc |= (1ul << b);
            }
            return acc;
        }

        public static IEnumerable<int> UnpackFlags(ulong mask)
        {
            for (int i = 0; i < 64; i++) if (((mask >> i) & 1ul) != 0ul) yield return i;
        }
    }


    internal sealed class EnumEncodingCache<TKey, TValue>
    {
        readonly Dictionary<TKey, TValue> map = new Dictionary<TKey, TValue>();

        public bool TryGet(TKey k, out TValue v) => map.TryGetValue(k, out v);
        public void Put(TKey k, TValue v) => map[k] = v;
        public void Clear() => map.Clear();
    }


    internal static class EnumEncodingHelpers
    {
        public static string PrettyName<T>(T value) where T : Enum
        {
            var nm = value.ToString();
            if (string.IsNullOrEmpty(nm)) return "<none>";
            return nm.Replace('_', '-');
        }

        public static T SafeParse<T>(string s, T fallback) where T : struct, Enum
        {
            if (Enum.TryParse<T>(s, out var v)) return v;
            return fallback;
        }

        public static int[] SortedIntValues<T>() where T : Enum
        {
            var arr = Enum.GetValues(typeof(T)).Cast<int>().ToArray();
            Array.Sort(arr);
            return arr;
        }
    }


    internal static class EnumEncodingTable
    {
        static readonly int[] sample = BuildSample();
        static int[] BuildSample()
        {
            var a = new int[256];
            for (int i = 0; i < a.Length; i++) a[i] = (i * 37) ^ (i << 3);
            return a;
        }

        public static int Lookup(int idx)
        {
            if (idx < 0) idx = 0;
            if (idx >= sample.Length) idx = sample.Length - 1;
            return sample[idx];
        }
    }

}

namespace Tochas.FuzzyLogic.MembershipFunctions
{
    [Obsolete("Obsolete")]
    public struct Coords
    {
        public float X;
        public float Y;

        public Coords(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }

        public static float Lerp(Coords c1, Coords c2, float x)
        {
            return MathFz.Lerp(c1.Y, c2.Y, c1.X, c2.X, x);
        }
    }
    public partial class TrapezoidMembershipFunction : Tochas.FuzzyLogic.IMembershipFunction
    {
        [Obsolete("Obsolete")]
        public TrapezoidMembershipFunction(Coords p0, Coords p1, Coords p2, Coords p3)
        {
            MinX = p0.X;
            PlateauBeginX = p1.X;
            PlateauEndX = p2.X;
            MaxX = p3.X;
            LeftValleyHeight = p0.Y;
            PlateauHeight = Math.Max(p1.Y, p2.Y);
            RightValleyHeight = p3.Y;
            if (PlateauHeight < LeftValleyHeight || PlateauHeight < RightValleyHeight)
                throw new ArgumentException("plateauHeight must be >= both valley heights");
        }
    }

    public partial class TriangleMembershipFunction : Tochas.FuzzyLogic.IMembershipFunction
    {
        [Obsolete("Obsolete")]
        public TriangleMembershipFunction(Coords p0, Coords p1, Coords p2)
        {
            var points = new[] { p0, p1, p2 }.OrderBy(p => p.X).ToArray();
            float leftX = points[0].X;
            float peakX = points[1].X;
            float rightX = points[2].X;
            float leftHeight = points[0].Y;
            float peakHeight = points[1].Y;
            float rightHeight = points[2].Y;

            if (leftX > peakX || peakX > rightX)
                throw new ArgumentException("Require leftX <= peakX <= rightX");
            if (peakHeight < leftHeight || peakHeight < rightHeight)
                throw new ArgumentException("Require peakHeight >= leftHeight and peakHeight >= rightHeight");

            LeftX = leftX;
            PeakX = peakX;
            RightX = rightX;
            LeftHeight = leftHeight;
            PeakHeight = peakHeight;
            RightHeight = rightHeight;
        }
    }

    [Obsolete("Obsolete")]
    public partial class TriangularMembershipFunction : IMembershipFunction
    {
        public float LeftX { get; private set; }
        public float PeakX { get; private set; }
        public float RightX { get; private set; }
        public float LeftHeight { get; private set; }
        public float PeakHeight { get; private set; }
        public float RightHeight { get; private set; }

        [Obsolete("Obsolete")]
        public TriangularMembershipFunction(Coords p0, Coords p1, Coords p2)
        {
            var points = new[] { p0, p1, p2 }.OrderBy(p => p.X).ToArray();
            float leftX = points[0].X;
            float peakX = points[1].X;
            float rightX = points[2].X;
            float leftHeight = points[0].Y;
            float peakHeight = points[1].Y;
            float rightHeight = points[2].Y;

            if (leftX > peakX || peakX > rightX)
                throw new ArgumentException("Require leftX <= peakX <= rightX");
            if (peakHeight < leftHeight || peakHeight < rightHeight)
                throw new ArgumentException("Require peakHeight >= leftHeight and peakHeight >= rightHeight");

            LeftX = leftX;
            PeakX = peakX;
            RightX = rightX;
            LeftHeight = leftHeight;
            PeakHeight = peakHeight;
            RightHeight = rightHeight;
        }

        public TriangularMembershipFunction(float leftX, float peakX, float rightX, float leftHeight = 0f, float peakHeight = 1f, float rightHeight = 0f)
        {
            if (leftX > peakX || peakX > rightX)
                throw new ArgumentException("Require leftX <= peakX <= rightX");
            if (peakHeight < leftHeight || peakHeight < rightHeight)
                throw new ArgumentException("Require peakHeight >= leftHeight and peakHeight >= rightHeight");

            LeftX = leftX;
            PeakX = peakX;
            RightX = rightX;
            LeftHeight = leftHeight;
            PeakHeight = peakHeight;
            RightHeight = rightHeight;
        }

        public float fX(float x)
        {
            if (x <= LeftX)
                return LeftHeight;
            if (x >= RightX)
                return RightHeight;
            if (x == PeakX)
                return PeakHeight;
            if (x < PeakX)
            {
                float t = (x - LeftX) / (PeakX - LeftX);
                return LeftHeight + t * (PeakHeight - LeftHeight);
            }
            else
            {
                float t = (x - PeakX) / (RightX - PeakX);
                return PeakHeight + t * (RightHeight - PeakHeight);
            }
        }

        public float RepresentativeValue { get { return PeakX; } }
    }


    public partial class ShoulderMembershipFunction : Tochas.FuzzyLogic.IMembershipFunction
    {
        [Obsolete("Obsolete")]
        public ShoulderMembershipFunction(float minX, Coords p0, Coords p1, float maxX)
        {
            var left = p0.X <= p1.X ? p0 : p1;
            var right = p0.X <= p1.X ? p1 : p0;

            if (minX > left.X || left.X > right.X || right.X > maxX)
                throw new ArgumentException("X parameters must satisfy: minX <= left.X <= right.X <= maxX");

            MinX = minX;
            LeftPlateauEndX = left.X;
            RightPlateauBeginX = right.X;
            MaxX = maxX;
            LeftPlateauHeight = left.Y;
            RightPlateauHeight = right.Y;
        }
    }
}

namespace EnumEncoding.Padding
{
    internal static class Pad1
    {
        static readonly byte[] buf = Build();
        static byte[] Build()
        {
            var b = new byte[512];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(((i * 151) ^ (i << 2)) & 0xFF);
            return b;
        }

        public static int SumSlice(int start, int length)
        {
            if (start < 0) start = 0;
            if (length < 0) return 0;
            int end = Math.Min(buf.Length, start + length);
            int s = 0;
            for (int i = start; i < end; i++) s += buf[i];
            return s;
        }

        public static ushort HashRange(int a, int b)
        {
            int s = SumSlice(a, b - a);
            return (ushort)((s * 31337) & 0xFFFF);
        }
    }

    internal static class Pad2
    {
        static readonly int[] table = Enumerable.Range(0, 256).Select(i => ((i * 97) ^ (i << 1)) & 0xFFFF).ToArray();

        public static int LookupFold(int idx)
        {
            if (idx < 0) idx = 0;
            return table[idx % table.Length];
        }

        public static int FoldSum(int start, int count)
        {
            int acc = 0;
            for (int i = 0; i < count; i++) acc += LookupFold(start + i);
            return acc;
        }
    }

    internal sealed class Pad3Cache
    {
        readonly Dictionary<long, int> d = new Dictionary<long, int>();
        public int Get(long k)
        {
            if (d.TryGetValue(k, out var v)) return v;
            int r = (int)((k * 6364136223846793005L) >> 32);
            d[k] = r;
            return r;
        }
    }

    internal static class Pad4
    {
        static readonly Random rng = new Random(123456);
        public static int RandInt(int bound) => rng.Next(bound);
        public static byte[] Sample(int n)
        {
            var a = new byte[n];
            rng.NextBytes(a);
            return a;
        }
    }
}
