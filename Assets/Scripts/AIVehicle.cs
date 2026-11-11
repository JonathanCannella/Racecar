// Unfortunately Agent is a class rather than an interface, which makes things a pain
//#define USE_MLAGENTS
#undef USE_MLAGENTS
#define USE_FANCY_EFFECTS

/*
 * This code is part of Arcade Car Physics for Unity by Saarg (2018)
 * 
 * This is distributed under the MIT Licence (see LICENSE.md for details)
 * 
 * AIVehicle is based on WheelVehicle
 */
using System;
using System.Collections;
using System.Collections.Generic;

using System.Diagnostics;

using UnityEngine;

using Unity.MLAgents;
using Unity.MLAgents.Actuators;

using PathCreation;
using PathCreation.Examples;

// All the Fuzz
using Tochas.FuzzyLogic;
using Tochas.FuzzyLogic.MembershipFunctions;
using Tochas.FuzzyLogic.Evaluators;
using Tochas.FuzzyLogic.Mergers;
using Tochas.FuzzyLogic.Defuzzers;
using Tochas.FuzzyLogic.Expressions;
using static Tochas.FuzzyLogic.Expressions.FuzzyDSL;


#if MULTIOSCONTROLS
    using MOSC;
#endif

namespace GameAI
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PathTracker))]
    public partial class AIVehicle :

#if USE_MLAGENTS
        Agent
#else
        MonoBehaviour
#endif

    {
        protected string StudentName { get; set; }

        [Header("Wiring")]

        [SerializeField] private EmoteSoundManager EmoteSndMgr;

        [Header("Inputs")]
#if MULTIOSCONTROLS
        [SerializeField] PlayerNumber playerId;
#endif
        // If isPlayer is false inputs are ignored
        [SerializeField] private bool isPlayer = true;
        public bool IsPlayer { get { return isPlayer; } private set { isPlayer = value; } }

        // Input names to read using GetAxis
        [SerializeField] private string throttleInput = "Throttle";
        [SerializeField] private string brakeInput = "Brake";
        [SerializeField] private string turnInput = "Horizontal";
        [SerializeField] private string jumpInput = "Jump";
        [SerializeField] private string driftInput = "Drift";
        [SerializeField] private string boostInput = "Boost";

        /* 
         *  Turn input curve: x real input, y value used
         *  My advice (-1, -1) tangent x, (0, 0) tangent 0 and (1, 1) tangent x
         */
        [SerializeField] private AnimationCurve turnInputCurve = AnimationCurve.Linear(-1.0f, -1.0f, 1.0f, 1.0f);

        [Header("Wheels")]
        [SerializeField] private WheelCollider[] driveWheel;
        // Canonical wheel order and telemetry (0=FL, 1=FR, 2=BL, 3=BR)
        private WheelCollider[] _logicalWheels = new WheelCollider[4];
        private bool _wheelOrderReady = false;
        public struct WheelTelemetry
        {
            public bool grounded;
            public bool isDrive;
            public bool isTurn;
            public float forwardSlip;
            public float sidewaysSlip;
            public float rpm;
            public float steerDeg;
            public Vector3 contactPoint;
            public Vector3 contactNormal;
            public float normalForce;
            public int frame;
        }
        private readonly WheelTelemetry[] _wheelTelem = new WheelTelemetry[4];

        [SerializeField] private WheelCollider[] turnWheel;

        // This code checks if the car is grounded only when needed and the data is old enough
        // (cached per physics step)
        private bool isGrounded = false;
        private long lastGroundCheck = -1;
        public bool IsGrounded
        {
            get
            {
                if (lastGroundCheck == FixedUpdateTicks)
                    return isGrounded;

                lastGroundCheck = FixedUpdateTicks;
                isGrounded = true;
                foreach (WheelCollider wheel in wheels)
                {
                    if (!wheel.gameObject.activeSelf || !wheel.isGrounded)
                        isGrounded = false;
                }
                return isGrounded;
            }
        }

        [Header("Behaviour")]
        /*
         *  Motor torque represent the torque sent to the wheels by the motor with x: speed in km/h and y: torque
         *  The curve should start at x=0 and y>0 and should end with x>topspeed and y<0
         *  The higher the torque the faster it accelerate
         *  the longer the curve the faster it gets
         */
        [SerializeField] private AnimationCurve motorTorque = new AnimationCurve(new Keyframe(0, 200), new Keyframe(50, 300), new Keyframe(200, 0));

        // Differential gearing ratio
        [Range(2, 16)]
        [SerializeField] private float diffGearing = 4.0f;
        public float DiffGearing { get { return diffGearing; } private set { diffGearing = value; } }

        // Basicaly how hard it brakes
        [SerializeField] float brakeForce = 1500.0f;
        public float BrakeForce { get { return brakeForce; } private set { brakeForce = value; } }

        // Max steering hangle, usualy higher for drift car
        [Range(0f, 50.0f)]
        [SerializeField] private float steerAngle = 30.0f;
        public float SteerAngle { get { return steerAngle; } private set { steerAngle = Mathf.Clamp(value, 0.0f, 50.0f); } }

        // The value used in the steering Lerp, 1 is instant (Strong power steering), and 0 is not turning at all
        [Range(0.001f, 1.0f)]
        [SerializeField] private float steerSpeed = 0.2f;
        public float SteerSpeed { get { return steerSpeed; } private set { steerSpeed = Mathf.Clamp(value, 0.001f, 1.0f); } }

        // How hight do you want to jump?
        [Range(1f, 1.5f)]
        [SerializeField] private float jumpVel = 1.3f;
        public float JumpVel { get { return jumpVel; } private set { jumpVel = Mathf.Clamp(value, 1.0f, 1.5f); } }

        // How hard do you want to drift?
        [Range(0.0f, 2f)]
        [SerializeField] private float driftIntensity = 1f;
        public float DriftIntensity { get { return driftIntensity; } private set { driftIntensity = Mathf.Clamp(value, 0.0f, 2.0f); } }

        // Reset Values
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;

        /*
         *  The center of mass is set at the start and changes the car behavior A LOT
         *  I recomment having it between the center of the wheels and the bottom of the car's body
         *  Move it a bit to the from or bottom according to where the engine is
         */
        [SerializeField] private Transform centerOfMass;

        // Force aplied downwards on the car, proportional to the car speed
        [Range(0.5f, 10f)]
        [SerializeField] private float downforce = 1.0f;
        public float Downforce { get { return downforce; } private set { downforce = Mathf.Clamp(value, 0, 5); } }

        // Steering command in [-1, 1] (-1 full left, 1 full right). Converted to a wheel
        // angle (steering * steerAngle) only when applied in FixedUpdate().
        private float steering;
        public float Steering {
            get { return steering; }

#if USE_MLAGENTS
            set { steering = Mathf.Clamp(value, -1f, 1f); }
#else
            set
            {        	
                if(IsPlayer)    
                    steering = Mathf.Clamp(value, -1f, 1f);
            }
        
#endif
		}

        private float InternalSteering
        {
            get { return steering; }
            set { steering = Mathf.Clamp(value, -1f, 1f); }
        }
        
        // When IsPlayer is false you can use this to control the throttle
        private float throttle;
        public float Throttle {
            get { return throttle; }

#if USE_MLAGENTS
			set { throttle = Mathf.Clamp(value, -1f, 1f); }
#else		
			set {
                if(IsPlayer)
                    throttle = Mathf.Clamp(value, -1f, 1f);
            } 
#endif
        }

        private float InternalThrottle
        {
            get { return throttle; }
            set
            {
                throttle = Mathf.Clamp(value, -1f, 1f);
            }
        }

        // Like your own car handbrake, if it's true the car will not move
        [SerializeField] private bool handbrake;
        public bool Handbrake { get { return handbrake; } private set { handbrake = value; } }

        // Use this to disable drifting
        [SerializeField] private bool allowDrift = false;
        private bool drift;
        public bool Drift { get { return drift; } private set { drift = value; } }

        // Use this to disable the handbrake behavior entirely
        [SerializeField] private bool allowHandbrake = false;
        public bool AllowHandbrake { get { return allowHandbrake; } private set { allowHandbrake = value; } }

        // Use this to read the current car speed (you'll need this to make a speedometer)
        [SerializeField] private float speed = 0.0f;
        public float Speed_kph { get { return speed; } }

        public float Speed_mph { get { return speed * 0.621371f; }}

        public float Speed { get { return speed / 3.6f; } }

        [SerializeField] private float averageSpeed = 0.0f;
        public float AverageSpeed_kph { get => averageSpeed / 3.6f; }

        public float AverageSpeed { get { return averageSpeed ; }}

        [Header("Particles")]
        // Exhaust fumes
        [SerializeField] private ParticleSystem[] gasParticles;

        [Header("Boost")]
        // Disable boost
        [SerializeField] private bool allowBoost = false;

        // Maximum boost available
        [SerializeField] private float maxBoost = 10f;
        public float MaxBoost { get { return maxBoost; } private set { maxBoost = value; } }

        // Current boost available
        [SerializeField] private float boost = 10f;
        public float Boost { get { return boost; } private set { boost = Mathf.Clamp(value, 0f, maxBoost); } }

        // Regen boostRegen per second until it's back to maxBoost
        [Range(0f, 1f)]
        [SerializeField] private float boostRegen = 0.2f;
        public float BoostRegen { get { return boostRegen; } private set { boostRegen = Mathf.Clamp01(value); } }

        /*
         *  The force applied to the car when boosting
         *  NOTE: the boost does not care if the car is grounded or not
         */
        [SerializeField] private float boostForce = 5000;
        public float BoostForce { get { return boostForce; } private set { boostForce = value; } }

        // Use this to boost when IsPlayer is set to false
        private  bool boosting = false;
        // Use this to jump when IsPlayer is set to false
        private  bool jumping = false;

        // Boost particles and sound
        [SerializeField] private ParticleSystem[] boostParticles;
        [SerializeField] private AudioClip boostClip;
        [SerializeField] private AudioSource boostSource;


        [Header("HUD")]
        [SerializeField] private bool outputToHUD;

        [SerializeField] private bool freezeHUDAtTime;

        [SerializeField] private  int freezeHUDSeconds = 5 * 60;

        [SerializeField] private  RectTransform vizInputMarker;

        [SerializeField] protected  TMPro.TextMeshProUGUI vizText;


        public bool OutputToHUD { get => outputToHUD; set => outputToHUD = value; }

        public bool FreezeHUDAtTime { get => freezeHUDAtTime; set => freezeHUDAtTime = value; }

        public int FreezeHUDSeconds { get => freezeHUDSeconds; set => freezeHUDSeconds = value; }

        public RectTransform VizInputMarker { get => vizInputMarker;
            set {
                vizInputMarker = value;
            } }

        public TMPro.TextMeshProUGUI VizText { get => vizText; set => vizText = value; }



        [Header("Death and Dismemberment")]

        [SerializeField] private float BeginFallPos = -0.5f;
        [SerializeField] private float FallYPos = -20f;

        private bool IsFalling = false;

        [SerializeField] private float SpawnYPos = 1.5f;

        [Header("DEBUG")]


        // Private variables set at the start
        private Rigidbody _rb;
        private WheelCollider[] wheels;

        public Vector3 AngularVelocity { get => _rb.angularVelocity; }

        public Vector3 Velocity { get => _rb.linearVelocity; }


        private PathTracker _pathTracker;

        // Read-only track data (also used by an attached RacecarAgent for observations)
        public IRacetrackData Racetrack { get; private set; }

        private float startDist = 0f;

        private bool _isResetting = false;

        //Stopwatch stopwatch;
        private float startTime;

        public long UpdateTicks { get; private set; }
        public long FixedUpdateTicks { get; private set; }

        // UpdateControls() runs every ControlStepInterval physics steps; controls are held in between.
        // With the 60 Hz physics step (1/60 s) this is 30 Hz. (NN agents use the same interval as their decision period.)
        public const int ControlStepInterval = 2;

        // Seconds of game time between UpdateControls() calls (1/30 s). Use it for rates of change between calls:
        // Time.deltaTime there is one physics step, only part of this interval.
        public static float ControlDeltaTime => ControlStepInterval * Time.fixedDeltaTime;

        // True on physics steps where controls are decided (the first step, then every ControlStepInterval)
        public bool IsControlStep => (FixedUpdateTicks - 1) % ControlStepInterval == 0;

        [Header("DEBUG")]
        public float DB_Throttle;
        public float DB_Steering;
        //public float DB_internal_steering;

        public void ApplyFuzzyRules<T, S>(
            FuzzyRuleSet<T> throttleFRS, 
            FuzzyRuleSet<S> steerFRS,
            FuzzyValueSet fuzzyValueSet,
            out FuzzyValue<T>[] throttleRuleOutput,
            out FuzzyValue<S>[] steeringRuleOutput,
            ref FuzzyValueSet mergedThrottle,
            ref FuzzyValueSet mergedSteering
            ) 
            where T: struct, IConvertible where S: struct, IConvertible
        {

            // Manually evaluate so we can probe each step for debugging purposes

            if (!hardCodeThrottle)
            {
                throttleRuleOutput = throttleFRS.RuleEvaluator.EvaluateRules(throttleFRS.Rules, fuzzyValueSet);
                var throttleMerger = throttleFRS.OutputsMerger;//new CachedOutputsFuzzyValuesMerger<T>();
                throttleMerger.MergeValues(throttleRuleOutput, mergedThrottle);
                var throttleDefuzz = throttleFRS.Defuzzer;
                {
                    float _candThrottle = throttleDefuzz.Defuzze(throttleFRS.OutputVarSet, mergedThrottle);
                    if (ContainsNaNorInf(_candThrottle))
                    {
                        UnityEngine.Debug.LogError($"ApplyFuzzyRules: Defuzzed throttle is invalid (NaN/Inf). Refusing to assign. Value={_candThrottle:0.###}");
                    }
                    else
                    {
                        InternalThrottle = _candThrottle;
                    }
                }
            }
            else
            {
                throttleRuleOutput = null;
                InternalThrottle = hardCodedThrottleVal;
            }

            if (!hardCodeSteering)
            {
                steeringRuleOutput = steerFRS.RuleEvaluator.EvaluateRules(steerFRS.Rules, fuzzyValueSet);
                var steeringMerger = steerFRS.OutputsMerger;//new CachedOutputsFuzzyValuesMerger<T>();
                steeringMerger.MergeValues(steeringRuleOutput, mergedSteering);
                var steeringDefuzz = steerFRS.Defuzzer;
                {
                    float _candSteer = steeringDefuzz.Defuzze(steerFRS.OutputVarSet, mergedSteering);
                    if (ContainsNaNorInf(_candSteer))
                    {
                        UnityEngine.Debug.LogError($"ApplyFuzzyRules: Defuzzed steering is invalid (NaN/Inf). Refusing to assign. Value={_candSteer:0.###}");
                    }
                    else
                    {
                        InternalSteering = _candSteer;
                    }
                }
            }
            else
            {
                steeringRuleOutput = null;
                InternalSteering = hardCodedSteeringVal;
            }

        }


        virtual protected void Awake()
        {
            _pathTracker = GetComponent<PathTracker>();

            // Initialize read-only facade; keep direct tracker access during migration
            if (_pathTracker != null)
            {
                Racetrack = new RacetrackData(_pathTracker);
            }
            else
            {
                Racetrack = null;
            }

            if (_pathTracker == null) UnityEngine.Debug.LogError("No path tracker");

            //stopwatch = new Stopwatch();

            //if (vizInputMarker == null)
            //    UnityEngine.Debug.LogError("No input viz marker");
        }


        private bool wasPlayer = false;

        private bool hardCodeSteering = false;
        private bool reportedHardCodeSteering = false;
        private float hardCodedSteeringVal = 0f;
        private bool hardCodeThrottle = false;
        private bool reportedHardCodeThrottle = false;
        private float hardCodedThrottleVal = 0f;

        public bool HardCodedValueUsed { get => reportedHardCodeSteering || reportedHardCodeThrottle; }

        /// <summary>
        /// Wire this vehicle's internal PathTracker to a given PathCreator and optionally
        /// register it as the follower on a GenerateDynamicPathExample (road generator).
        /// Keeps the tracker encapsulated while allowing the autograder to hook up.
        /// </summary>
        public void INTERNAL_WireToPathCreator(PathCreator pathCreator, GenerateDynamicPathExample roadCreator = null)
        {
            if (pathCreator == null)
                throw new UnityException("WireToPathCreator: PathCreator is null");

            if (_pathTracker == null)
                _pathTracker = GetComponent<PathTracker>();
            if (_pathTracker == null)
                throw new UnityException("WireToPathCreator: PathTracker missing on vehicle");

            // Delegate to tracker-specific API; do not expose tracker itself
            _pathTracker.SetPathCreator(pathCreator);

            if (roadCreator != null)
            {
                roadCreator.pathFollower = _pathTracker;
            }

            // Ensure Road facade exists and reflects current tracker
            if (Racetrack == null)
                Racetrack = new RacetrackData(_pathTracker);
        }

        /// <summary>
        /// Convenience wiring: copy PathCreator from a reference tracker and register
        /// this vehicle as the road generator's follower.
        /// </summary>
        public void INTERNAL_WireToRoadCreator(GenerateDynamicPathExample roadCreator, PathTracker referenceTracker)
        {
            if (roadCreator == null)
                throw new UnityException("WireToRoadCreator: roadCreator is null");
            if (referenceTracker == null)
                throw new UnityException("WireToRoadCreator: referenceTracker is null");

            INTERNAL_WireToPathCreator(referenceTracker.pathCreator, roadCreator);
        }



        public Vector3[] vizInputCorners = new Vector3[4];
       

        // Physics steps are driven by VehicleStepper (see INTERNAL_BeginPhysicsStep/INTERNAL_EndPhysicsStep)
        private void OnEnable()
        {
            VehicleStepper.Register(this);
        }

        private void OnDisable()
        {
            VehicleStepper.Unregister(this);
        }

        internal bool INTERNAL_Started { get; private set; }

        // Optional ML-Agents agent on this car (same GameObject or a child). When present, it decides controls unless it is in
        // heuristic mode, in which case this vehicle's own controller decides and the agent records it.
        internal RacecarAgent INTERNAL_Agent { get; set; }

        // Init rigidbody, center of mass, wheels and more
        virtual protected void Start()
        {
            wasPlayer = IsPlayer;

            if (vizInputMarker != null)
            {
                var vizPar = vizInputMarker.parent.GetComponent<RectTransform>();
                vizPar.GetLocalCorners(vizInputCorners);
            }

            GameManager.Instance.StudentNameTMP.text += StudentName + System.Environment.NewLine;

#if MULTIOSCONTROLS
            Debug.Log("[ACP] Using MultiOSControls");
#endif
            // if (boostClip != null)
            // {
            //     boostSource.clip = boostClip;
            // }

            boost = maxBoost;

            _rb = GetComponent<Rigidbody>();
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;

            if (_rb != null && centerOfMass != null)
            {
                _rb.centerOfMass = centerOfMass.localPosition;
            }

            wheels = GetComponentsInChildren<WheelCollider>();
            BuildWheelOrderOrFail();

            // Ensure Road facade exists even if tracker was bound late
            if (Racetrack == null && _pathTracker != null)
            {
                Racetrack = new RacetrackData(_pathTracker);
            }

            // Set the motor torque to a non null value because 0 means the wheels won't turn no matter what
            foreach (WheelCollider wheel in wheels)
            {
                wheel.motorTorque = 0.0001f;
            }


            // UnityEngine.Debug.Log($"ResetCar() called from AIVehicle.Start()");
            INTERNAL_ResetCar(false);


            //stopwatch.Start();
            startTime = Time.timeSinceLevelLoad;

            startDist = _pathTracker.totalDistanceTravelled;

            if (INTERNAL_Agent == null)
                INTERNAL_Agent = GetComponentInChildren<RacecarAgent>();

            INTERNAL_Started = true;
        }

        private void BuildWheelOrderOrFail()
        {
            _wheelOrderReady = false;
            if (turnWheel == null || turnWheel.Length != 2)
                throw new UnityException("Wheel setup invalid: turnWheel must have length 2 (FL, FR)");
            if (driveWheel == null || (driveWheel.Length != 2 && driveWheel.Length != 4))
                throw new UnityException("Wheel setup invalid: driveWheel must have length 2 (BL, BR) or 4 (FL, FR, BL, BR)");

            if (driveWheel.Length == 4)
            {
                // 0:FL 1:FR 2:BL 3:BR
                _logicalWheels[0] = driveWheel[0];
                _logicalWheels[1] = driveWheel[1];
                _logicalWheels[2] = driveWheel[2];
                _logicalWheels[3] = driveWheel[3];
            }
            else // driveWheel.Length == 2
            {
                // turnWheel 0:FL 1:FR, driveWheel 0:BL 1:BR
                _logicalWheels[0] = turnWheel[0];
                _logicalWheels[1] = turnWheel[1];
                _logicalWheels[2] = driveWheel[0];
                _logicalWheels[3] = driveWheel[1];
            }

            for (int i = 0; i < 4; i++)
            {
                if (_logicalWheels[i] == null)
                    throw new UnityException($"Wheel setup invalid: logical slot {i} is null");
            }
            _wheelOrderReady = true;
        }

        private void RefreshWheelTelemetry()
        {
            if (!_wheelOrderReady) return;
            int frame = Time.frameCount;
            for (int i = 0; i < 4; i++)
            {
                var wc = _logicalWheels[i];
                var t = new WheelTelemetry();
                t.frame = frame;
                t.isDrive = (wc != null && Array.IndexOf(driveWheel, wc) >= 0);
                t.isTurn  = (wc != null && Array.IndexOf(turnWheel, wc)  >= 0);
                t.rpm = wc != null ? wc.rpm : 0f;
                t.steerDeg = wc != null ? wc.steerAngle : 0f;
                if (wc != null)
                {
                    WheelHit hit;
                    if (wc.GetGroundHit(out hit))
                    {
                        t.grounded = true;
                        t.forwardSlip = hit.forwardSlip;
                        t.sidewaysSlip = hit.sidewaysSlip;
                        t.contactPoint = hit.point;
                        t.contactNormal = hit.normal;
                        t.normalForce = hit.force;
                    }
                    else
                    {
                        t.grounded = false;
                        t.forwardSlip = 0f;
                        t.sidewaysSlip = 0f;
                        t.contactPoint = wc.transform.position;
                        t.contactNormal = Vector3.up;
                        t.normalForce = 0f;
                    }
                }
                _wheelTelem[i] = t;
            }
        }

        public bool TryGetWheelTelemetry(int logicalIndex, out WheelTelemetry telemetry)
        {
            if (!_wheelOrderReady || logicalIndex < 0 || logicalIndex > 3)
            {
                telemetry = default;
                return false;
            }
            telemetry = _wheelTelem[logicalIndex];
            return true;
        }


        protected void HardCodeThrottle(float v)
        {
            hardCodedThrottleVal = v;
            InternalThrottle = v;
            hardCodeThrottle = true;

            if (!reportedHardCodeThrottle)
            {
                reportedHardCodeThrottle = true;
                throw new UnityException("Hard coded throttle only allowed for testing.");
            }
        }

        protected void HardCodeSteering(float v)
        {
            hardCodedSteeringVal = v;
            InternalSteering = v;
            hardCodeSteering = true;

            if (!reportedHardCodeSteering)
            {
                reportedHardCodeSteering = true;
                throw new UnityException("Hard coded steering only allowed for testing.");
            }
        }


        private bool Achieved88Mph = false;

        private bool YeeHawed = false;

        private int frameCount = 0;

        // Per rendered frame: visual/audio feedback, stats and HUD.
        // Driving decisions and physics run once per physics step in FixedUpdate().
        private void Update()
        {
            ++UpdateTicks;

            ++frameCount;

            DB_Throttle = Throttle;
            DB_Steering = Steering;

            if (IsPlayer && IsPlayer != wasPlayer)
            {
                throw new UnityException("Cheat detected!");
            }


            var elpsSec = Time.timeSinceLevelLoad - startTime;//stopwatch.Elapsed.TotalSeconds;

#if USE_FANCY_EFFECTS
            foreach (ParticleSystem gasParticle in gasParticles)
            {
                gasParticle.Play();
                ParticleSystem.EmissionModule em = gasParticle.emission;
                em.rateOverTime = handbrake ? 0 : Mathf.Lerp(em.rateOverTime.constant, Mathf.Clamp(150.0f * throttle, 30.0f, 100.0f), 0.1f);
            }
#endif
            if (isPlayer && allowBoost)
            {
                boost += Time.deltaTime * boostRegen;
                if (boost > maxBoost) { boost = maxBoost; }
            }


            if (!YeeHawed && frameCount > 20)
            {
                YeeHawed = true;
                EmoteSndMgr.Play(EmoteSoundManager.EmoteSoundType.YeeHaw);
            }

            if (!Achieved88Mph && Speed_mph >= 88f)
                {
                    Achieved88Mph = true;

                    EmoteSndMgr.Play(EmoteSoundManager.EmoteSoundType.Mph88);
                }

            if (!IsFalling)
            {
                if (_rb.transform.position.y < BeginFallPos)
                {
                    IsFalling = true;

                    EmoteSndMgr.Play(EmoteSoundManager.EmoteSoundType.Scream);
                }
            }
            else
            {
                if (_rb.transform.position.y > BeginFallPos)
                {
                    IsFalling = false;
                }
            }


            averageSpeed = (float)(3.6 * (_pathTracker.totalDistanceTravelled - startDist) / elpsSec);


            var gm = GameManager.Instance;

            gm.Wipeouts = numResets;
            gm.KpHLTA = averageSpeed;
            gm.MetersTravelled = _pathTracker.totalDistanceTravelled;
            gm.MinThrottle = Mathf.Min(gm.MinThrottle, Throttle);
            gm.MaxThrottle = Mathf.Max(gm.MaxThrottle, Throttle);

            if (outputToHUD)
            {

                if (freezeHUDAtTime && (elpsSec > (float)freezeHUDSeconds))
                {
                    gm.ElapsedTMP.text = TimeSpan.FromSeconds((double)freezeHUDSeconds).ToString(@"hh\:mm\:ss\.fff");//stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    gm.ElapsedTMP.text = TimeSpan.FromSeconds((double)elpsSec).ToString(@"hh\:mm\:ss\.fff");//stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                    gm.KPH_TMP.text = Speed_kph.ToString("0.0");
                    gm.MPH_TMP.text = Speed_mph.ToString("0.0");

                    //var elpsSec = stopwatch.Elapsed.TotalSeconds;
                    //var avgSpd = 3.6 * pathTracker.totalDistanceTravelled / elpsSec;
                    gm.KPH_LTA_TMP.text = averageSpeed.ToString("0.0");
                    gm.TotalMetersTMP.text = _pathTracker.totalDistanceTravelled.ToString("0.0");
                    gm.WipeoutsTMP.text = numResets.ToString();
                }


                if (vizInputMarker != null)
                {
                    if (!ContainsNaNorInf(Steering) && !ContainsNaNorInf(Throttle))
                        vizInputMarker.localPosition = new Vector3((vizInputCorners[2].x - vizInputCorners[0].x) * 0.5f * Steering, (vizInputCorners[2].y - vizInputCorners[0].y) * 0.5f * Throttle, 0f);

                }
            }

        }

        /// <summary>
        /// Override to compute this step's Throttle and Steering (e.g. with ApplyFuzzyRules()).
        /// Called every ControlStepInterval physics steps (30 Hz, ControlDeltaTime apart), after the vehicle state (Speed,
        /// wheel telemetry, Racetrack) has been refreshed and before the controls are applied to the wheels.
        /// </summary>
        protected virtual void UpdateControls()
        {
        }

        /// <summary>
        /// Optional: add the crisp (pre-fuzzification) inputs your controller uses, computed from the
        /// current state, so an NN agent (RacecarAgent) can observe the same values. Default adds nothing.
        /// </summary>
        public virtual void CollectCrispInputs(List<float> inputs)
        {
        }

        private void ReadPlayerInput()
        {
            // Accelerate & brake
            if (throttleInput != "" && throttleInput != null)
            {
                throttle = GetInput(throttleInput) - GetInput(brakeInput);

#if !USE_MLAGENTS
                throttle = Mathf.Clamp(throttle, -1f, 1f);

                //UnityEngine.Debug.Log($"throttle: {GetInput(throttleInput)} brake: {GetInput(brakeInput)}");
#endif
            }
            // Boost
            boosting = (GetInput(boostInput) > 0.5f);
            // Turn
            steering = turnInputCurve.Evaluate(GetInput(turnInput));
            // Dirft
            drift = GetInput(driftInput) > 0 && _rb.linearVelocity.sqrMagnitude > 100;
            // Jump
            jumping = GetInput(jumpInput) != 0;
        }

        // Reset the car if it falls, flips, stops, or turns around (checked each physics step)
        private void CheckForWipeouts()
        {
            var elpsSec = Time.timeSinceLevelLoad - startTime;

            // Handle the oops #1 (fall) 
            if (_rb.transform.position.y < FallYPos)
            {
                if (!_isResetting)
                {
                    _isResetting = true;
                    UnityEngine.Debug.Log($"OOPS:Fall at {elpsSec} sec");
                    INTERNAL_ResetCar();
                }
            }
            else
            {
                _isResetting = false;
            }

            // Handle the oops #2 (truck flipped, possibly caught on edge)
            var tiltAngle = Vector3.Angle(transform.up, Vector3.up);
            if (tiltAngle > 20f)
            {
                if (!tilted)
                {
                    tilted = true;
                    timeOfTilt = Time.timeSinceLevelLoad;
                }

                //Debug.Log($"tilted by: {tiltAngle} for: {Time.timeSinceLevelLoad - timeOfTilt}");

                if (Time.timeSinceLevelLoad - timeOfTilt > tiltTimeout)
                {
                    tilted = false;
                    UnityEngine.Debug.Log($"OOPS:Tilted at {elpsSec} sec");
                    INTERNAL_ResetCar();
                }
            }
            else
            {
                tilted = false;
            }


            // Handle the oops #3 (truck not moving)

            var distTravelled = _pathTracker.totalDistanceTravelled - prevDist;
            prevDist = _pathTracker.totalDistanceTravelled;

            //Debug.Log($"distTrav: {distTravelled}");
            //distFakeSlidingAvg = distTravelled / fakeTimeLen +
            //    distFakeSlidingAvg * (fakeTimeLen - Time.deltaTime) / fakeTimeLen;

            //Debug.Log($"distfakeslidingavg: {distFakeSlidingAvg}");

            if (Speed_kph < 0.5f)
            {
                if (!stopped)
                {
                    timeOfStop = Time.timeSinceLevelLoad;
                    stopped = true;
                }

                if (Time.timeSinceLevelLoad - timeOfStop > timeOfStopTimeout)
                {
                    stopped = false;
                    UnityEngine.Debug.Log($"OOPS:Stopped at {elpsSec} sec");
                    INTERNAL_ResetCar();
                }
            }
            else
            {
                stopped = false;
            }

            //if(distFakeSlidingAvg < 0.5f)
            //{
            //    if(!stopped)
            //    {
            //        timeOfStop = Time.timeSinceLevelLoad;
            //        stopped = true;
            //    }

            //    if (Time.timeSinceLevelLoad - timeOfStop > timeOfStopTimeout)
            //    {
            //        stopped = false;
            //        UnityEngine.Debug.Log("OOPS:Stopped");
            //        ResetCar();
            //    }
            //}
            //else
            //{
            //    stopped = false;
            //}


            // Handle oops #4 (turned around backwards)

            if (Vector3.Angle(transform.forward, _pathTracker.closestPointDirectionOnPath) > 90f)
            {
                if (!isTurnedBackwards)
                {
                    timeOfBkwds = Time.timeSinceLevelLoad;
                    isTurnedBackwards = true;
                }

                //Debug.Log($"Backwards for: {Time.timeSinceLevelLoad - timeOfBkwds}");

                if (Time.timeSinceLevelLoad - timeOfBkwds > timeOfBkwdsTimeout)
                {
                    isTurnedBackwards = false;
                    UnityEngine.Debug.Log($"OOPS:Backwards at {elpsSec} sec");
                    INTERNAL_ResetCar();
                }
            }
            else
            {
                isTurnedBackwards = false;
            }
        }

        private bool ContainsNaNorInf(float v)
        {
            if (
                float.IsNaN(v) || float.IsInfinity(v) 
                )
            {
                return true;
            }

            return false;
        }

        private bool ContainsNaNorInf(Vector3 v)
        {
            if(
                float.IsNaN(v.x) || float.IsInfinity(v.x) ||
                float.IsNaN(v.y) || float.IsInfinity(v.y) ||
                float.IsNaN(v.z) || float.IsInfinity(v.z) 
                )
            {
                return true;
            }

            return false;
        }


        private int numResets = 0;

        private bool tilted = false;
        private float timeOfTilt = 0f;
        private float tiltTimeout = 5f;


        private bool stopped = false;
        private float timeOfStop = 0f;
        private float timeOfStopTimeout = 5f;

        private float prevDist = 0;

        // public float distFakeSlidingAvg = 10f;

        private float fakeTimeLen = 5f;

        private bool isTurnedBackwards = false;
        private float timeOfBkwds = 0f;
        private float timeOfBkwdsTimeout = 5f;


        protected virtual void INTERNAL_ResetCar()
        {
            INTERNAL_ResetCar(true);
        }

        // Raised when the car wipes out (fell, flipped, stuck, turned around) and is reset by the vehicle.
        // An attached agent can use this to punish and end the episode.
        public event Action WipedOut;

        protected virtual void INTERNAL_ResetCar(bool trackResetCountAndPreserveDist)
        {
            StartCoroutine(INTERNAL_DeferredResetCar(trackResetCountAndPreserveDist));

            if (trackResetCountAndPreserveDist)
                WipedOut?.Invoke();
        }

        protected IEnumerator INTERNAL_DeferredResetCar(bool trackResetCountAndPreserveDist)
        {
            while(!_pathTracker.PathInitialized)
            {
                UnityEngine.Debug.Log("pathTracker NOT init yet, waiting!");
                yield return null;
            }

            // UnityEngine.Debug.Log("reset car");
            if (trackResetCountAndPreserveDist)
                ++numResets;

            tilted = false;
            stopped = false;
            isTurnedBackwards = false;

            // distFakeSlidingAvg = 10f;

            // make room for the car chassis to fit on road
            float minAllowedDist = 3f;

            if (!trackResetCountAndPreserveDist || _pathTracker.distanceTravelled < minAllowedDist)
            {
                // UnityEngine.Debug.Log("reset to min");
                _pathTracker.ResetToDistance(minAllowedDist);
                _pathTracker.ResetTotalDistance();
            }

            if (_pathTracker.distanceTravelled > (_pathTracker.MaxPathDistance - minAllowedDist))
            {
                // UnityEngine.Debug.Log("reset to max");
                _pathTracker.ResetToDistance(_pathTracker.MaxPathDistance - minAllowedDist);
            }

            _rb.MovePosition(_pathTracker.closestPointOnPath + Vector3.up * SpawnYPos);

            var rotPose = Quaternion.LookRotation(_pathTracker.closestPointDirectionOnPath, Vector3.up);

            // UnityEngine.Debug.Log($"ResetCar(): closestPointDirectionOnPath: {pathTracker.closestPointDirectionOnPath}");

            _rb.MoveRotation(rotPose);

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.ResetInertiaTensor();

            yield return null;
        }



        // Phase 1 of a physics step (called by VehicleStepper): refresh vehicle state and, on control steps,
        // let this vehicle's own controller decide (unless an attached agent is deciding instead).
        // PathTracker (and the dynamic road) have already updated for this step (script execution order).
        internal void INTERNAL_BeginPhysicsStep()
        {

            ++FixedUpdateTicks;

            // Provide a velocity hint to PathTracker for coherence-aware tracking
            if (_pathTracker != null)
            {
                var vel = (_rb != null) ? _rb.linearVelocity : Vector3.zero; // world-space
                if (!ContainsNaNorInf(vel))
                    _pathTracker.SetVelocityHint(vel);
            }



            // Mesure current speed
            speed = transform.InverseTransformDirection(_rb.linearVelocity).z * 3.6f;
            // (wheel telemetry was sampled earlier this step, see INTERNAL_SampleWheelTelemetry)

            if (isPlayer)
            {
                ReadPlayerInput();
            }
            else if (IsControlStep && (INTERNAL_Agent == null || INTERNAL_Agent.INTERNAL_VehicleControllerDecides))
            {
                // Log rather than propagate so a controller exception doesn't skip applying physics this step
                try
                {
                    UpdateControls();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e, this);
                }

                // Recording a cautious teacher (DemonstrationSession): above the cap no throttle (coast); the controller's
                // own braking is kept. (Zero throttle clears the drive torque; a negative one brakes but leaves the
                // previous drive torque on, see INTERNAL_EndPhysicsStep.) The limited decision is what gets recorded.
                if (INTERNAL_TeacherSpeedCapKph > 0f && speed > INTERNAL_TeacherSpeedCapKph)
                    throttle = Mathf.Min(throttle, 0f);
            }

        }

        // Recording support (DemonstrationSession only; zero in normal and graded runs).
        // Noise added to the applied (steering, throttle) without changing the decided Throttle/Steering,
        // so demonstrations record the controller's clean decision in slightly perturbed states.
        internal Vector2 INTERNAL_ControlNoise;

        // Below this forward speed (kph), negative throttle would drive backwards rather than brake
        const float ReverseBelowKph = 4f;

        // Recording support (DemonstrationSession only; 0 = off): speed cap applied to the controller's decision
        internal float INTERNAL_TeacherSpeedCapKph;

        // Recording support: put the car back on the track at its current position along the path, at rest,
        // optionally offset sideways (m, + = right) and rotated (deg, + = clockwise). Not counted as a wipeout.
        internal void INTERNAL_ForceReset(float lateralOffset, float headingOffsetDeg)
        {
            tilted = false;
            stopped = false;
            isTurnedBackwards = false;

            var dir = _pathTracker.closestPointDirectionOnPath;
            var right = Vector3.Cross(Vector3.up, dir).normalized;

            _rb.MovePosition(_pathTracker.closestPointOnPath + Vector3.up * SpawnYPos + right * lateralOffset);
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, headingOffsetDeg, 0f));

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.ResetInertiaTensor();
        }

        // Called by WheelTelemetrySampler before the road generator runs this step: when a new segment replaces
        // the road collider, every wheel's last contact refers to the old collider and reads as not grounded.
        internal void INTERNAL_SampleWheelTelemetry() => RefreshWheelTelemetry();

        // Snapshot support (TrackSnapshots only)
        internal Rigidbody INTERNAL_Rigidbody => _rb;
        internal PathTracker INTERNAL_PathTracker => _pathTracker;

        // After the car was placed elsewhere (snapshot restore): restart the wipeout checks from here
        internal void INTERNAL_OnTeleported()
        {
            tilted = false;
            stopped = false;
            isTurnedBackwards = false;
            prevDist = _pathTracker.totalDistanceTravelled;
        }

        // Set by an attached RacecarAgent when it decides (OnActionReceived)
        internal void INTERNAL_SetControlsFromAgent(float steeringCmd, float throttleCmd)
        {
            InternalSteering = steeringCmd;
            InternalThrottle = throttleCmd;
        }

        // Phase 2 of a physics step (called by VehicleStepper, after any agent decisions):
        // check for wipeouts and apply the controls to the wheels.
        internal void INTERNAL_EndPhysicsStep()
        {
            CheckForWipeouts();

            // Controls actually applied this step (the decided Throttle/Steering plus any recording noise)
            float steering = Mathf.Clamp(this.steering + INTERNAL_ControlNoise.x, -1f, 1f);
            float throttle = Mathf.Clamp(this.throttle + INTERNAL_ControlNoise.y, -1f, 1f);

            // No reverse gear: negative throttle only brakes. Near standstill it would otherwise drive the car
            // backwards (see below), which never helps on the track.
            if (throttle < 0f && speed < ReverseBelowKph)
                throttle = 0f;


            // Direction
            foreach (WheelCollider wheel in turnWheel)
            {
                wheel.steerAngle = Mathf.Lerp(wheel.steerAngle, steering * steerAngle, steerSpeed * 100f * Time.fixedDeltaTime);
            }

            foreach (WheelCollider wheel in wheels)
            {
                wheel.brakeTorque = 0;
            }

            // Handbrake
            if (allowHandbrake && handbrake)
            {
                foreach (WheelCollider wheel in wheels)
                {
                    // Don't zero out this value or the wheel completly lock up
                    wheel.motorTorque = 0.0001f;
                    wheel.brakeTorque = brakeForce;
                }
            }
            else if (Mathf.Abs(speed) < 4 || Mathf.Sign(speed) == Mathf.Sign(throttle))
            {
                foreach (WheelCollider wheel in driveWheel)
                {
                    wheel.motorTorque = throttle * motorTorque.Evaluate(speed) * diffGearing / driveWheel.Length;
                }
            }
            else
            {
                foreach (WheelCollider wheel in wheels)
                {
                    wheel.brakeTorque = Mathf.Abs(throttle) * brakeForce;
                }

                // Braking: no drive torque. (Previously the drive wheels kept the last throttle step's motor torque,
                // so braking right after strong throttle fought the engine. Not zero, as for the handbrake above.)
                foreach (WheelCollider wheel in driveWheel)
                {
                    wheel.motorTorque = 0.0001f;
                }
            }

            // Jump
            if (jumping && isPlayer)
            {
                if (!IsGrounded)
                    return;

                _rb.linearVelocity += transform.up * jumpVel;
            }

            // Boost
            if (boosting && allowBoost && boost > 0.1f)
            {
                _rb.AddForce(transform.forward * boostForce);

                boost -= Time.fixedDeltaTime;
                if (boost < 0f) { boost = 0f; }

                if (boostParticles.Length > 0 && !boostParticles[0].isPlaying)
                {
                    foreach (ParticleSystem boostParticle in boostParticles)
                    {
                        boostParticle.Play();
                    }
                }

                // if (boostSource != null && !boostSource.isPlaying)
                // {
                //     boostSource.Play();
                // }
            }
            else
            {
                if (boostParticles.Length > 0 && boostParticles[0].isPlaying)
                {
                    foreach (ParticleSystem boostParticle in boostParticles)
                    {
                        boostParticle.Stop();
                    }
                }

                // if (boostSource != null && boostSource.isPlaying)
                // {
                //     boostSource.Stop();
                // }
            }

            // Drift
            if (drift && allowDrift)
            {
                Vector3 driftForce = -transform.right;
                driftForce.y = 0.0f;
                driftForce.Normalize();

                if (steering != 0)
                    driftForce *= _rb.mass * speed / 7f * throttle * steering;
                Vector3 driftTorque = transform.up * 0.1f * steering;


                _rb.AddForce(driftForce * driftIntensity, ForceMode.Force);
                _rb.AddTorque(driftTorque * driftIntensity, ForceMode.VelocityChange);
            }

            // Downforce
            _rb.AddForce(-transform.up * speed * downforce);
        }

        // Reposition the car to the start position
        private void ResetPos()
        {
            transform.position = spawnPosition;
            transform.rotation = spawnRotation;

            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        private void ToggleHandbrake(bool h)
        {
            handbrake = h;
        }

        // MULTIOSCONTROLS is another package I'm working on ignore it I don't know if it will get a release.
#if MULTIOSCONTROLS
        private static MultiOSControls _controls;
#endif

        // Use this method if you want to use your own input manager
        private float GetInput(string input)
        {
#if MULTIOSCONTROLS
        return MultiOSControls.GetValue(input, playerId);
#else
            return Input.GetAxis(input);
#endif
        }

#if USE_MLAGENTS
        // impl to make warning shut up
        public override void OnActionReceived(ActionBuffers actions)
        {
            // This is just for testing with a human
        }


        // impl to make warning shut up
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            // This is just for testing with a human
        }

#endif

    }
}
