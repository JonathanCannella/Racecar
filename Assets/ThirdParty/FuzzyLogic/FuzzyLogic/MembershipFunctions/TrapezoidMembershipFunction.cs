using System;

namespace Tochas.FuzzyLogic.MembershipFunctions
{
    /// <summary>
    /// Trapezoid-style membership function with explicit ramp and plateau bounds.
    /// Parameters use valley/plateau terminology and ordered X positions.
    ///
    /// X parameters (must be non-decreasing):
    ///   minX               : support lower bound (left valley anchor)
    ///   plateauBeginX      : start of the flat top (end of left ramp)
    ///   plateauEndX        : end of the flat top (start of right ramp)
    ///   maxX               : support upper bound (right valley anchor)
    ///
    /// Y parameters:
    ///   leftValleyHeight   : y-value at the left valley (often 0)
    ///   plateauHeight      : y-value on the plateau (often 1)
    ///   rightValleyHeight  : y-value at the right valley (often 0)
    ///
    /// Piecewise definition:
    ///   x <= plateauBeginX         -> linear from (minX,leftValley) to (plateauBeginX,plateau)
    ///   plateauBeginX <= x <= plateauEndX -> plateauHeight
    ///   x >= plateauEndX           -> linear from (plateauEndX,plateau) to (maxX,rightValley)
    ///
    /// Notes:
    /// - A degenerate top (plateauBeginX == plateauEndX) reduces to a triangle peak at that X.
    /// - Plateau height must be >= both valley heights.
    /// </summary>
    public partial class TrapezoidMembershipFunction : IMembershipFunction
    {
        public float MinX { get; private set; }
        public float PlateauBeginX { get; private set; }
        public float PlateauEndX { get; private set; }
        public float MaxX { get; private set; }

        public float LeftValleyHeight { get; private set; }
        public float PlateauHeight { get; private set; }
        public float RightValleyHeight { get; private set; }

        public TrapezoidMembershipFunction(
            float minX,
            float plateauBeginX,
            float plateauEndX,
            float maxX,
            float leftValleyHeight = 0,
            float plateauHeight = 1,
            float rightValleyHeight = 0)
        {
            if (minX > plateauBeginX || plateauBeginX > plateauEndX || plateauEndX > maxX)
                throw new ArgumentException("X parameters must satisfy: minX <= plateauBeginX <= plateauEndX <= maxX");
            if (plateauHeight < leftValleyHeight || plateauHeight < rightValleyHeight)
                throw new ArgumentException("plateauHeight must be >= both valley heights");

            MinX = minX;
            PlateauBeginX = plateauBeginX;
            PlateauEndX = plateauEndX;
            MaxX = maxX;
            LeftValleyHeight = leftValleyHeight;
            PlateauHeight = plateauHeight;
            RightValleyHeight = rightValleyHeight;
        }


        public float fX(float x)
        {
            if (x <= PlateauBeginX)
            {
                if (PlateauBeginX == MinX) return PlateauHeight; // step up
                float t = (x - MinX) / (PlateauBeginX - MinX);
                return LeftValleyHeight + t * (PlateauHeight - LeftValleyHeight);
            }
            if (x >= PlateauEndX)
            {
                if (MaxX == PlateauEndX) return RightValleyHeight; // step down
                float t = (x - PlateauEndX) / (MaxX - PlateauEndX);
                return PlateauHeight + t * (RightValleyHeight - PlateauHeight);
            }
            // On the top
            return PlateauHeight;
        }

        public float RepresentativeValue
        {
            get
            {
                return 0.5f * (PlateauBeginX + PlateauEndX);
            }
        }
    }
}
