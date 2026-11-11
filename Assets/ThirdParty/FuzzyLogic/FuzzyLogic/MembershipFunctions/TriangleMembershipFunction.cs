using System;
using System.Linq;

namespace Tochas.FuzzyLogic.MembershipFunctions
{
    public partial class TriangleMembershipFunction : IMembershipFunction
    {
        public float LeftX { get; private set; }
        public float PeakX { get; private set; }
        public float RightX { get; private set; }
        public float LeftHeight { get; private set; }
        public float PeakHeight { get; private set; }
        public float RightHeight { get; private set; }

        public TriangleMembershipFunction(float leftX, float peakX, float rightX, float leftHeight = 0f, float peakHeight = 1f, float rightHeight = 0f)
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
}
