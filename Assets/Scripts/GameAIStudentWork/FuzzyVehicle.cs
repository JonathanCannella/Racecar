// compile_check
// Remove the line above if you are submitting to GradeScope for a grade. But leave it if you only want to check
// that your code compiles and the autograder can access your public methods.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using GameAI;

// All the Fuzz
using Tochas.FuzzyLogic;
using Tochas.FuzzyLogic.MembershipFunctions;
using Tochas.FuzzyLogic.Evaluators;
using Tochas.FuzzyLogic.Mergers;
using Tochas.FuzzyLogic.Defuzzers;
using Tochas.FuzzyLogic.Expressions;
using Tochas.FuzzyLogic.Utils;
using static Tochas.FuzzyLogic.FuzzyCrossfade;
using static Tochas.FuzzyLogic.FuzzyDiscreteSet;
using static Tochas.FuzzyLogic.FuzzyVisualize; 

namespace GameAI
{

    public class FuzzyVehicle : AIVehicle
    {

        // TODO create some Fuzzy Set enumeration types, and member variables for:
        // Fuzzy Sets (input and output), one or more Fuzzy Value Sets, and Fuzzy
        // Rule Sets for each output.
        // Also, create some methods to instantiate each of the member variables

        // Here are some basic examples to get you started
        enum FzOutputThrottle {Brake, Coast, Accelerate }
        enum FzOutputWheel { TurnLeft, GoStraight, TurnRight }

        enum FzInputSpeed { Slow, Medium, Fast }

        FuzzySet<FzInputSpeed> fzSpeedSet;

        FuzzySet<FzOutputThrottle> fzThrottleSet;
        FuzzyRuleSet<FzOutputThrottle> fzThrottleRuleSet;

        FuzzySet<FzOutputWheel> fzWheelSet;
        FuzzyRuleSet<FzOutputWheel> fzWheelRuleSet;

        FuzzyValueSet fzInputValueSet = new FuzzyValueSet();

        // These are used for debugging (see ApplyFuzzyRules() call
        // in UpdateControls()
        FuzzyValueSet mergedThrottle = new FuzzyValueSet();
        FuzzyValueSet mergedWheel = new FuzzyValueSet();



        private FuzzySet<FzInputSpeed> GetSpeedSet()
        {
            FuzzySet<FzInputSpeed> set = null;

            // TODO: Define this fuzzy input variable using GenerateCrossfadeFuzzySet<T>().
            // Each enum label should have overlapping triangular or trapezoidal DoM functions.
            // Example (pseudocode):
            // set = GenerateCrossfadeFuzzySet<FzInputSpeed>( ... );
            // replace the following:

            // Slow
            const float slow = 10f;

            // Medium
            const float medium = 25f;

            // Fast
            const float fast = 50f;

            //set = new FuzzySet<FzInputSpeed>();
            set = GenerateCrossfadeFuzzySet<FzInputSpeed>(slow, medium, fast);

            // You can then print the ASCII visualization for debugging:
            // Debug.Log(RenderFuzzySetAscii(set));

            return set;
        }

        private FuzzySet<FzOutputThrottle> GetThrottleSet()
        {
            FuzzySet<FzOutputThrottle> set = null;

            // TODO: Define this fuzzy output variable using GenerateDiscreteFuzzySet<T>().

            // Example (pseudocode):
            // set = GenerateDiscreteFuzzySet<FzOutputThrottle>( ... );
            // replace the following:

            // Brake
            const float brake = -1f;

            // coast
            const float coast = 0f;

            // accelerate
            const float accelerate = 1f;

            //set = new FuzzySet<FzOutputThrottle>();
            set = GenerateDiscreteFuzzySet<FzOutputThrottle>(brake, coast, accelerate);


            // Use RenderFuzzySetAscii(set) to visualize the shape of your output terms.

            return set;
        }

        private FuzzySet<FzOutputWheel> GetWheelSet()
        {
            FuzzySet<FzOutputWheel> set = null;

            // TODO: Define this fuzzy output variable using GenerateDiscreteFuzzySet<T>().
            // Each enum label should have a representative crisp value for steering direction.

            // Example (pseudocode):
            // set = GenerateDiscreteFuzzySet<FzOutputWheel>( ... );
            // replace the following:

            set = new FuzzySet<FzOutputWheel>();

            // You may test with: Debug.Log(RenderFuzzySetAscii(set));

            return set;
        }


        private FuzzyRuleSet<FzOutputThrottle> GetThrottleRuleSet(FuzzySet<FzOutputThrottle> throttle)
        {

            FuzzyRule<FzOutputThrottle>[] rules =
            {
                // TODO: Add some rules. Here is an example
                // (Note: these aren't necessarily good rules)
                If(FzInputSpeed.Slow).Then(FzOutputThrottle.Accelerate),
                If(FzInputSpeed.Medium).Then(FzOutputThrottle.Coast),
                If(FzInputSpeed.Fast).Then(FzOutputThrottle.Brake),
                // More example syntax
                //If(And(FzInputSpeed.Fast, Not(FzFoo.Bar)).Then(FzOutputThrottle.Accelerate),
            };

            return new FuzzyRuleSet<FzOutputThrottle>(throttle, rules);
        }

        private FuzzyRuleSet<FzOutputWheel> GetWheelRuleSet(FuzzySet<FzOutputWheel> wheel)
        {

            FuzzyRule<FzOutputWheel>[] rules =
            {
                // TODO: Add some rules.
            };

            return new FuzzyRuleSet<FzOutputWheel>(wheel, rules);
        }


        protected override void Awake()
        {
            base.Awake();

            StudentName = "Jonathan Cannella";

            // DO NOT INITIALIZE FUZZY STUFF HERE!!! Use Start() instead.
        }

        protected override void Start()
        {
            base.Start();

            // TODO: You can initialize a bunch of Fuzzy stuff here like more fuzzy inputs
            fzSpeedSet = this.GetSpeedSet();

            fzThrottleSet = this.GetThrottleSet();
            fzThrottleRuleSet = this.GetThrottleRuleSet(fzThrottleSet);

            fzWheelSet = this.GetWheelSet();
            fzWheelRuleSet = this.GetWheelRuleSet(fzWheelSet);
        }

        System.Text.StringBuilder strBldr = new System.Text.StringBuilder();

        // Called at a fixed rate (30 Hz, ControlDeltaTime seconds apart): compute Throttle and Steering here via ApplyFuzzyRules().
        override protected void UpdateControls()
        {

            // TODO Do all your input fuzzification here and then
            // pass your fuzzy rule sets to ApplyFuzzyRules()
            
            // Remove the following hardcode calls once you get your fuzzy rules working.
            // You can leave one hardcoded while you work on the other.
            // Both steering and throttle must be implemented with variable
            // control and not fixed/hardcoded!

            HardCodeSteering(0f);
            HardCodeThrottle(0.5f);
            
            // Simple example of fuzzification of vehicle state
            // The Speed is fuzzified and stored in fzInputValueSet
            fzSpeedSet.Evaluate(Speed, fzInputValueSet);

            // ApplyFuzzyRules evaluates your rules and assigns Thottle and Steering accordingly
            // Also, some intermediate values are passed back for debugging purposes
            // Defuzzification output values as defined below are automatically assigned by ApplyFuzzyRules.
            // Throttle: [-1f, 1f] -1 is full brake, 0 is neutral, 1 is full throttle
            // Steering: [-1f, 1f] -1 if full left, 0 is neutral, 1 is full right
            // Note that you MUST use ApplyFuzzyRules(). You cannot direclty assign Throttle and Steering.

            ApplyFuzzyRules<FzOutputThrottle, FzOutputWheel>(
                fzThrottleRuleSet,
                fzWheelRuleSet,
                fzInputValueSet,
                // access to intermediate state for debugging
                out var throttleRuleOutput,
                out var wheelRuleOutput,
                ref mergedThrottle,
                ref mergedWheel
                );


            // Use vizText for debugging output
            // You might also use Debug.DrawLine() to draw vectors on Scene view
            // When you are done implementing, you can comment out this entire if statement.
            if (vizText != null)
            {
                strBldr.Clear();

                strBldr.AppendLine($"Demo Output");
                strBldr.AppendLine($"Comment out before submission");

                // You will probably want to selectively enable/disable printing
                // of certain fuzzy states or rules as you progress.

                DiagnosticPrintFuzzyValueSet<FzInputSpeed>(fzInputValueSet, strBldr);

                DiagnosticPrintRuleSet<FzOutputThrottle>(fzThrottleRuleSet, throttleRuleOutput, strBldr);
                DiagnosticPrintRuleSet<FzOutputWheel>(fzWheelRuleSet, wheelRuleOutput, strBldr);

                vizText.text = strBldr.ToString();
            }

        }


        // For the optional NN part: add the crisp input values your fuzzy rules use (e.g. speed, etc.),
        // computed the same way as in UpdateControls(), so the NN agent (NNVehicle) can observe them too.
        public override void CollectCrispInputs(List<float> inputs)
        {
            if (Racetrack == null || !Racetrack.IsInitialized)
                return;

            // TODO for the NN part, e.g.:
            // YourCrispValueCalculationHelper();   // the same helper method UpdateControls() calls
            // inputs.Add(Speed_kph);
        }
    }
}
