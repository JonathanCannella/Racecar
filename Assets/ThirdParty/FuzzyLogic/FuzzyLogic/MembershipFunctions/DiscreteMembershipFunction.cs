namespace Tochas.FuzzyLogic.MembershipFunctions
{
    public class DiscreteMembershipFunction : IMembershipFunction
    {
        public float RepresentativeValue { get; set; }
        public float Degree { get; set; } 

        public DiscreteMembershipFunction(float representativeValue)
        {
            RepresentativeValue = representativeValue;
            Degree = 1f;
        }

        public float fX(float x) => Degree;
    }
}