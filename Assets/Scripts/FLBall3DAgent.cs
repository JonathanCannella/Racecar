using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using Random = UnityEngine.Random;

using GameAI;

// All the Fuzz
using Tochas.FuzzyLogic;
using Tochas.FuzzyLogic.MembershipFunctions;
using Tochas.FuzzyLogic.Evaluators;
using Tochas.FuzzyLogic.Mergers;
using Tochas.FuzzyLogic.Defuzzers;
using Tochas.FuzzyLogic.Expressions;
using static Tochas.FuzzyLogic.Expressions.FuzzyDSL;
using static Tochas.FuzzyLogic.FuzzyCrossfade;
using static Tochas.FuzzyLogic.FuzzyDiscreteSet;
using static Tochas.FuzzyLogic.FuzzyVisualize;

namespace GameAI
{

    public class FLBall3DAgent : MonoBehaviour
    {

        public GameObject ball;

        GameObject topCenterOfHead;

        Rigidbody m_BallRb;


        // Some balance head rotation smoothing control
        // This defines the physical movement model.

        [SerializeField]
        protected float RotationSmoothTime = 2f;
        protected Quaternion quaternionDeriv;


        [SerializeField]
        protected bool VisualizeFuzzySetFunctions = false;


        // Fuzzy Stuff

        enum FzInputBallPosX { Negative, Zero, Positive };
        enum FzInputBallPosZ { Negative, Zero, Positive };

        enum FzInputBallVelX { Negative, Zero, Positive };
        enum FzInputBallVelZ { Negative, Zero, Positive };

        enum FzOutputHeadRotX { RotNegDir, NoRot, RotPosDir };
        enum FzOutputHeadRotZ { RotNegDir, NoRot, RotPosDir };

        FuzzySet<FzInputBallPosX> inputBallPosXSet;
        FuzzySet<FzInputBallPosZ> inputBallPosZSet;

        FuzzySet<FzInputBallVelX> inputBallVelXSet;
        FuzzySet<FzInputBallVelZ> inputBallVelZSet;

        FuzzySet<FzOutputHeadRotX> outputHeadRotXSet;
        FuzzySet<FzOutputHeadRotZ> outputHeadRotZSet;

        FuzzyRuleSet<FzOutputHeadRotX> headRotXRuleSet;
        FuzzyRuleSet<FzOutputHeadRotZ> headRotZRuleSet;

        FuzzyValueSet fzInputValueSet = new FuzzyValueSet();

        FuzzyValueSet mergedHeadRotX = new FuzzyValueSet();
        FuzzyValueSet mergedHeadRotZ = new FuzzyValueSet();


        // balance guy's head half-width
        const float halfWidth = 2.5f;


        void Awake()
        {
            m_BallRb = ball.GetComponent<Rigidbody>();

            m_BallRb.sleepThreshold = 0.1f;
        }


        const float deadZone = 0.01f;
        const float maxAdjust = 0.75f;

        private FuzzySet<FzInputBallPosX> GetBallPosXSet()
        {

            var fuzzySet = GenerateCrossfadeFuzzySet<FzInputBallPosX>(
                (-halfWidth, -halfWidth * maxAdjust), (-deadZone, +deadZone), (halfWidth * maxAdjust, halfWidth)
            );

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzInputBallPosX>(fuzzySet));
            }

            return fuzzySet;
        }



        private FuzzySet<FzInputBallPosZ> GetBallPosZSet()
        {
            var fuzzySet = GenerateCrossfadeFuzzySet<FzInputBallPosZ>(
                (-halfWidth, -halfWidth * maxAdjust), (-deadZone, +deadZone), (halfWidth * maxAdjust, halfWidth)
            );

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzInputBallPosZ>(fuzzySet));
            }

            return fuzzySet;
        }


        const float maxSpeed = 1f;

        private FuzzySet<FzInputBallVelX> GetBallVelXSet()
        {

            var fuzzySet = GenerateCrossfadeFuzzySet<FzInputBallVelX>(
                (-maxSpeed, -maxSpeed * maxAdjust),
                (-deadZone, +deadZone),
                (maxSpeed * maxAdjust, maxSpeed)
            );

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzInputBallVelX>(fuzzySet));
            }
            return fuzzySet;
        }


        private FuzzySet<FzInputBallVelZ> GetBallVelZSet()
        {

            var fuzzySet = GenerateCrossfadeFuzzySet<FzInputBallVelZ>(
                (-maxSpeed, -maxSpeed * maxAdjust), (-deadZone, +deadZone), (maxSpeed * maxAdjust, maxSpeed)
            );

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzInputBallVelZ>(fuzzySet));
            }

            return fuzzySet;
        }


        const float absMaxAngle = 20f;

        private FuzzySet<FzOutputHeadRotX> GetHeadRotXSet()
        {
            var fuzzySet = GenerateDiscreteFuzzySet<FzOutputHeadRotX>(-absMaxAngle, 0f, absMaxAngle);

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzOutputHeadRotX>(fuzzySet));
            }

            return fuzzySet;
        }

        private FuzzySet<FzOutputHeadRotZ> GetHeadRotZSet()
        {
            var fuzzySet = GenerateDiscreteFuzzySet<FzOutputHeadRotZ>(-absMaxAngle, 0f, absMaxAngle);

            if (VisualizeFuzzySetFunctions)
            {
                Debug.Log(RenderFuzzySetAscii<FzOutputHeadRotZ>(fuzzySet));
            }

            return fuzzySet;
        }

        private FuzzyRuleSet<FzOutputHeadRotX> GetHeadRotXRuleSet(FuzzySet<FzOutputHeadRotX> headRotX)
        {
            FuzzyRule<FzOutputHeadRotX>[] rules = new FuzzyRule<FzOutputHeadRotX>[]
            {
                If(FzInputBallPosZ.Negative).Then(FzOutputHeadRotX.RotPosDir),
                If(FzInputBallPosZ.Zero).Then(FzOutputHeadRotX.NoRot),
                If(FzInputBallPosZ.Positive).Then(FzOutputHeadRotX.RotNegDir),

                If(FzInputBallVelZ.Negative).Then(FzOutputHeadRotX.RotPosDir),
                If(FzInputBallVelZ.Zero).Then(FzOutputHeadRotX.NoRot),
                If(FzInputBallVelZ.Positive).Then(FzOutputHeadRotX.RotNegDir),
            };

            return new FuzzyRuleSet<FzOutputHeadRotX>(headRotX, rules);
        }

        private FuzzyRuleSet<FzOutputHeadRotZ> GetHeadRotZRuleSet(FuzzySet<FzOutputHeadRotZ> headRotZ)
        {
            FuzzyRule<FzOutputHeadRotZ>[] rules = new FuzzyRule<FzOutputHeadRotZ>[]
            {
                If(FzInputBallPosX.Positive).Then(FzOutputHeadRotZ.RotPosDir),
                If(FzInputBallPosX.Zero).Then(FzOutputHeadRotZ.NoRot),
                If(FzInputBallPosX.Negative).Then(FzOutputHeadRotZ.RotNegDir),

                If(FzInputBallVelX.Positive).Then(FzOutputHeadRotZ.RotPosDir),
                If(FzInputBallVelX.Zero).Then(FzOutputHeadRotZ.NoRot),
                If(FzInputBallVelX.Negative).Then(FzOutputHeadRotZ.RotNegDir),
            };

            return new FuzzyRuleSet<FzOutputHeadRotZ>(headRotZ, rules);
        }


        void ResetBall()
        {
            m_BallRb.linearVelocity = new Vector3(Random.Range(-maxSpeed, maxSpeed), 0f, Random.Range(-maxSpeed, maxSpeed));

            ball.transform.position = Vector3.up * 4.8f;

            const float absOffs = 1.2f;

            ball.transform.position = new Vector3(Random.Range(-absOffs, absOffs), ball.transform.position.y, Random.Range(-absOffs, absOffs)) + gameObject.transform.position;

            // Optionally isolate x or z random positioning:

            // ball.transform.position = new Vector3(Random.Range(-1.5f, 1.5f), 4f, 0f) + gameObject.transform.position;

            // ball.transform.position = new Vector3(0f, 4f, Random.Range(-1.5f, 1.5f)) + gameObject.transform.position;
        }


        void Start()
        {

            topCenterOfHead = new GameObject("TopCenterOfHead");
            topCenterOfHead.transform.SetPositionAndRotation(this.transform.position + Vector3.up * halfWidth, Quaternion.identity);
            topCenterOfHead.transform.SetParent(this.transform);

            // Some randomized start condition
            gameObject.transform.rotation = Quaternion.identity;
            const float maxAbsDegrees = 10f;
            gameObject.transform.Rotate(new Vector3(1, 0, 0), Random.Range(-maxAbsDegrees, maxAbsDegrees));
            gameObject.transform.Rotate(new Vector3(0, 0, 1), Random.Range(-maxAbsDegrees, maxAbsDegrees));

            ResetBall();

            // Fuzzy init
            inputBallPosXSet = GetBallPosXSet();
            inputBallPosZSet = GetBallPosZSet();

            inputBallVelXSet = GetBallVelXSet();
            inputBallVelZSet = GetBallVelZSet();

            outputHeadRotXSet = GetHeadRotXSet();
            outputHeadRotZSet = GetHeadRotZSet();

            headRotXRuleSet = GetHeadRotXRuleSet(outputHeadRotXSet);
            headRotZRuleSet = GetHeadRotZRuleSet(outputHeadRotZSet);

        }


        // Some Debugging variables that will be visible in Inspector Window
        [SerializeField] float DEBUG_ballPosX;
        [SerializeField] float DEBUG_ballPosZ;
        [SerializeField] float DEBUG_ballVelX;
        [SerializeField] float DEBUG_ballVelZ;
        [SerializeField] float DEBUG_HeadRotX;
        [SerializeField] float DEBUG_HeadRotZ;
        [SerializeField] bool DEBUG_ballSleeping = false;
        [SerializeField] int DEBUG_ballDropCount = 0;


        private void Update()
        {
            // did ball stop moving (e.g. fully balanced in middle)?
            if (m_BallRb.IsSleeping())
            {
                DEBUG_ballSleeping = true;

                ResetBall();
            }
            else
            {
                DEBUG_ballSleeping = false;
            }

            if (ball.transform.position.y < -10f)
            {
                Debug.Log("BALL DROPPED!");

                ++DEBUG_ballDropCount;

                ResetBall();
            }

            // relative to top-center position of balance guy's head

            var currBallPosX = ball.transform.position.x - topCenterOfHead.transform.position.x;
            var currBallPosZ = ball.transform.position.z - topCenterOfHead.transform.position.z;

            //var currBallPosX = ball.transform.position.x - gameObject.transform.position.x;
            //var currBallPosZ = ball.transform.position.z - gameObject.transform.position.z;

            // Fuzzification
            inputBallPosXSet.Evaluate(currBallPosX, fzInputValueSet);
            inputBallPosZSet.Evaluate(currBallPosZ, fzInputValueSet);

            inputBallVelXSet.Evaluate(m_BallRb.linearVelocity.x, fzInputValueSet);
            inputBallVelZSet.Evaluate(m_BallRb.linearVelocity.z, fzInputValueSet);

            // Fuzzy Rules evaluation and Defuzzification
            ApplyFuzzyRules<FzOutputHeadRotX, FzOutputHeadRotZ>(
                headRotXRuleSet,
                headRotZRuleSet,
                fzInputValueSet,
                out var headRotXRuleOutput,
                out var headRotZRuleOutput,
                ref mergedHeadRotX,
                ref mergedHeadRotZ,
                out var crispHeadRotXVal,
                out var crispHeadRotZVal
            );

            // Some debugging info visible in Inspector Window
            DEBUG_ballPosX = currBallPosX;
            DEBUG_ballPosZ = currBallPosZ;

            DEBUG_ballVelX = m_BallRb.linearVelocity.x;
            DEBUG_ballVelZ = m_BallRb.linearVelocity.z;

            DEBUG_HeadRotX = crispHeadRotXVal;
            DEBUG_HeadRotZ = crispHeadRotZVal;

        }


        public void ApplyFuzzyRules<T, S>(
            FuzzyRuleSet<T> headRotXRuleSet,
            FuzzyRuleSet<S> headRotZRuleSet,
            FuzzyValueSet fuzzyValueSet,
            out FuzzyValue<T>[] headRotXRuleOutput,
            out FuzzyValue<S>[] headRotZRuleOutput,
            ref FuzzyValueSet mergedHeadRotX,
            ref FuzzyValueSet mergedHeadRotZ,
            out float crispHeadRotXVal,
            out float crispHeadRotZVal
            )
            where T : struct, IConvertible where S : struct, IConvertible
        {
            // Perform rule evaluation one step at a time so we can extract debugging information

            headRotXRuleOutput = headRotXRuleSet.RuleEvaluator.EvaluateRules(headRotXRuleSet.Rules, fuzzyValueSet);
            var headRotXMerger = headRotXRuleSet.OutputsMerger;
            headRotXMerger.MergeValues(headRotXRuleOutput, mergedHeadRotX);
            var headRotXDefuzz = headRotXRuleSet.Defuzzer;
            crispHeadRotXVal = headRotXDefuzz.Defuzze(headRotXRuleSet.OutputVarSet, mergedHeadRotX);

            headRotZRuleOutput = headRotZRuleSet.RuleEvaluator.EvaluateRules(headRotZRuleSet.Rules, fuzzyValueSet);
            var headRotZMerger = headRotZRuleSet.OutputsMerger;
            headRotZMerger.MergeValues(headRotZRuleOutput, mergedHeadRotZ);
            var headRotZDefuzz = headRotZRuleSet.Defuzzer;
            crispHeadRotZVal = headRotZDefuzz.Defuzze(headRotZRuleSet.OutputVarSet, mergedHeadRotZ);

            // Validate crisp outputs before using them
            bool invalidX = float.IsNaN(crispHeadRotXVal) || float.IsInfinity(crispHeadRotXVal);
            bool invalidZ = float.IsNaN(crispHeadRotZVal) || float.IsInfinity(crispHeadRotZVal);
            if (invalidX || invalidZ)
            {
                Debug.LogError($"FLBall3DAgent.ApplyFuzzyRules: invalid defuzzed head rotation value(s). X={crispHeadRotXVal}, Z={crispHeadRotZVal}. Refusing to assign.");
                return; 
            }

            var newAngle = new Vector3(crispHeadRotXVal, 0f, crispHeadRotZVal);

            // Only write a new value if it is different so that rigidbody can go to sleep
            var target = Quaternion.Euler(newAngle);

            if (Quaternion.Angle(transform.rotation, target) > 0.01f)
            {
                // Using smoothDamp to simulate some inertia in control authority
                gameObject.transform.rotation = QuaternionUtil.SmoothDamp(transform.rotation, Quaternion.Euler(newAngle), ref quaternionDeriv, RotationSmoothTime);
            }

        }


    }
    

}
