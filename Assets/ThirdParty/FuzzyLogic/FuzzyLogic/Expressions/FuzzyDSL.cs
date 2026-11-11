using System;
using System.Reflection;

namespace Tochas.FuzzyLogic.Expressions
{
    /// <summary>
    /// Standalone helpers for composing IFuzzyExpression trees without extensions.
    /// Import with: using static Tochas.FuzzyLogic.Expressions.FuzzyDSL;
    /// These helpers mirror common fuzzy operators: If, And, Or, Not, Very, Fairly, Nor, Nand.
    ///
    /// Credit: Previous OMSCS student Bob Kerner authored the original extension-style API
    /// for the Fuzzy Rules framework. This standalone DSL follows and generalizes that design.
    /// </summary>
    public static class FuzzyDSL
    {
        // Wrap non-expression values as FuzzyVariableExpression<T> via reflection
        private static readonly Type VarExprOpen = typeof(FuzzyVariableExpression<>);

        private static IFuzzyExpression AsExpr(object value)
        {
            if (value is IFuzzyExpression e) return e;
            if (value == null) throw new ArgumentNullException(nameof(value));
            var t = value.GetType();
            var closed = VarExprOpen.MakeGenericType(t);
            var ctor = closed.GetConstructor(new[] { t });
            if (ctor == null)
                throw new InvalidOperationException("No matching ctor for FuzzyVariableExpression<T>(value)");
            return (IFuzzyExpression)ctor.Invoke(new object[] { value });
        }

        // Unary
        public static IFuzzyExpression If(object a) => AsExpr(a);
        public static IFuzzyExpression Not(object a) => AsExpr(a).Not();
        public static IFuzzyExpression Very(object a) => new FuzzyVery(AsExpr(a));
        public static IFuzzyExpression Fairly(object a) => new FuzzyFairly(AsExpr(a));

        // And (binary, small fixed arities, and variadic)
        public static IFuzzyExpression And(object a, object b) => AsExpr(a).And(AsExpr(b));
        public static IFuzzyExpression And(object a, object b, object c) => And(a, And(b, c));
        public static IFuzzyExpression And(object a, object b, object c, object d) => And(a, And(b, c, d));
        public static IFuzzyExpression And(params object[] terms)
        {
            if (terms == null || terms.Length == 0) throw new ArgumentException("And requires at least one term");
            var acc = AsExpr(terms[0]);
            for (int i = 1; i < terms.Length; i++) acc = acc.And(AsExpr(terms[i]));
            return acc;
        }

        // Or (binary, small fixed arities, and variadic)
        public static IFuzzyExpression Or(object a, object b) => AsExpr(a).Or(AsExpr(b));
        public static IFuzzyExpression Or(object a, object b, object c) => Or(a, Or(b, c));
        public static IFuzzyExpression Or(object a, object b, object c, object d) => Or(a, Or(b, c, d));
        public static IFuzzyExpression Or(params object[] terms)
        {
            if (terms == null || terms.Length == 0) throw new ArgumentException("Or requires at least one term");
            var acc = AsExpr(terms[0]);
            for (int i = 1; i < terms.Length; i++) acc = acc.Or(AsExpr(terms[i]));
            return acc;
        }

        // Derived
        public static IFuzzyExpression Nor(object a, object b) => Not(Or(a, b));
        public static IFuzzyExpression Nor(object a, object b, object c) => Not(Or(a, b, c));
        public static IFuzzyExpression Nor(object a, object b, object c, object d) => Not(Or(a, b, c, d));

        public static IFuzzyExpression Nand(object a, object b) => Not(And(a, b));
        public static IFuzzyExpression Nand(object a, object b, object c) => Not(And(a, b, c));
        public static IFuzzyExpression Nand(object a, object b, object c, object d) => Not(And(a, b, c, d));

        // Low-level combinators for custom ops
        public static IFuzzyExpression Op(object a, Func<IFuzzyExpression, IFuzzyExpression> fn)
            => fn(AsExpr(a));
        public static IFuzzyExpression Op(object a, object b, Func<IFuzzyExpression, IFuzzyExpression, IFuzzyExpression> fn)
            => fn(AsExpr(a), AsExpr(b));
    }
}