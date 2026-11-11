using System;

namespace Tochas.FuzzyLogic.MembershipFunctions
{
    /// <summary>
    /// Shoulder-style membership function with explicit plateau heights and ramp bounds.
    /// Parameters use left/right plateau terminology and ordered X positions.
    ///
    /// X parameters (must be non-decreasing):
    ///   minX                : support lower bound
    ///   leftPlateauEndX     : end of left plateau (ramp starts after this)
    ///   rightPlateauBeginX  : start of right plateau (ramp ends before this)
    ///   maxX                : support upper bound
    ///
    /// Y parameters:
    ///   leftPlateauHeight   : y-value on the left plateau
    ///   rightPlateauHeight  : y-value on the right plateau
    ///
    /// Piecewise definition:
    ///   x <= leftPlateauEndX          -> leftPlateauHeight
    ///   leftPlateauEndX < x < rightPlateauBeginX -> linear from leftPlateauHeight to rightPlateauHeight
    ///   x >= rightPlateauBeginX       -> rightPlateauHeight
    ///
    /// Notes:
    /// - A flat step can be created by setting leftPlateauEndX == rightPlateauBeginX.
    /// - The left and right plateau heights can be equal or different.
    /// </summary>
    public partial class ShoulderMembershipFunction : IMembershipFunction
    {
        public float MinX { get; private set; }
        public float MaxX { get; private set; }
        public float LeftPlateauEndX { get; private set; }
        public float RightPlateauBeginX { get; private set; }
        public float LeftPlateauHeight { get; private set; }
        public float RightPlateauHeight { get; private set; }

        public ShoulderMembershipFunction(
            float minX,
            float leftPlateauEndX,
            float rightPlateauBeginX,
            float maxX,
            float leftPlateauHeight,
            float rightPlateauHeight)
        {
            if (minX > leftPlateauEndX || leftPlateauEndX > rightPlateauBeginX || rightPlateauBeginX > maxX)
                throw new ArgumentException("X parameters must satisfy: minX <= leftPlateauEndX <= rightPlateauBeginX <= maxX");

            MinX = minX;
            LeftPlateauEndX = leftPlateauEndX;
            RightPlateauBeginX = rightPlateauBeginX;
            MaxX = maxX;
            LeftPlateauHeight = leftPlateauHeight;
            RightPlateauHeight = rightPlateauHeight;
        }

        public static ShoulderMembershipFunction InstantiateLeft(float minX, float leftPlateauEndX, float rightPlateauBeginX, float maxX)
        {
            return new ShoulderMembershipFunction(minX, leftPlateauEndX, rightPlateauBeginX, maxX, 1f, 0f);
        }

        public static ShoulderMembershipFunction InstantiateRight(float minX, float leftPlateauEndX, float rightPlateauBeginX, float maxX)
        {
            return new ShoulderMembershipFunction( minX, leftPlateauEndX, rightPlateauBeginX, maxX, 0f, 1f);
        }

        public float fX(float x)
        {
            if (x <= LeftPlateauEndX) return LeftPlateauHeight;
            if (x >= RightPlateauBeginX) return RightPlateauHeight;
            if (RightPlateauBeginX == LeftPlateauEndX) return RightPlateauHeight; // step
            float t = (x - LeftPlateauEndX) / (RightPlateauBeginX - LeftPlateauEndX);
            return LeftPlateauHeight + t * (RightPlateauHeight - LeftPlateauHeight);
        }

        public float RepresentativeValue
        {
            get
            {
                if (LeftPlateauHeight > RightPlateauHeight)
                {
                    return MinX;
                }
                if (RightPlateauHeight > LeftPlateauHeight)
                {
                    return MaxX;
                }
                return 0.5f * (LeftPlateauEndX + RightPlateauBeginX);
            }
        }
    }
}
