using Tochas.FuzzyLogic;
using Tochas.FuzzyLogic.Expressions;
using System;

    /// Credit: Previous OMSCS student Bob Kerner authored the original extension-style API
    /// for the Fuzzy Rules framework. This standalone DSL follows and generalizes that design.

namespace GameAI
{
    public partial class AIVehicle
    {
        // === Fuzzy DSL convenience shims ===
        protected static IFuzzyExpression If(object termOrExpr)
            => FuzzyDSL.If(termOrExpr);

        protected static IFuzzyExpression And(object a, object b)
            => FuzzyDSL.And(a, b);
        protected static IFuzzyExpression And(object a, object b, object c)
            => FuzzyDSL.And(a, b, c);
        protected static IFuzzyExpression And(params object[] terms)
            => FuzzyDSL.And(terms);

        protected static IFuzzyExpression Or(object a, object b)
            => FuzzyDSL.Or(a, b);
        protected static IFuzzyExpression Or(object a, object b, object c)
            => FuzzyDSL.Or(a, b, c);
        protected static IFuzzyExpression Or(params object[] terms)
            => FuzzyDSL.Or(terms);

        protected static IFuzzyExpression Not(object termOrExpr)
            => FuzzyDSL.Not(termOrExpr);

        protected static IFuzzyExpression Very(object termOrExpr)
            => FuzzyDSL.Very(termOrExpr);

        protected static IFuzzyExpression Fairly(object termOrExpr)
            => FuzzyDSL.Fairly(termOrExpr);

        protected static FuzzyRule<T> Rule<T>(object antecedent, T consequent)
            where T : struct, IConvertible
            => (FuzzyRule<T>)FuzzyDSL.If(antecedent).Then(consequent);
    }

}