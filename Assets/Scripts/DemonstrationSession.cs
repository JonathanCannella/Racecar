using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

using Unity.MLAgents.Demonstrations;

namespace GameAI
{
    /// <summary>
    /// Automated demonstration recording for imitation learning. Put it on the NN agent object
    /// (next to the RacecarAgent / NNVehicle) with the Behavior Type such that the car's own controller
    /// drives (Default with no model and no trainer, or Heuristic Only), e.g. your FuzzyVehicle.
    ///
    /// While recording, the car is periodically reset to rest at its current place on the track
    /// (sometimes offset sideways / turned a little), and small random noise is added to the applied
    /// controls. The recording always stores the controller's own (noise-free) decision. This gives the
    /// NN examples of standing starts, accelerating, driving at speed, and recovering, which it will
    /// need after wipeouts and drift during training.
    ///
    /// Only good driving is kept, so the NN doesn't learn your controller's mistakes:
    ///  - The recording is written in short chunks (.demo files). When the car wipes out, the chunks
    ///    covering the seconds before it are deleted (the mistake happened before the wipeout registered).
    ///  - Chunks where the car is struggling (stalled, wheels off the road, facing the wrong way) are deleted.
    ///  - At the end, a report says how much was kept and whether the controller is a good teacher. If it
    ///    crashes or struggles often, record with a lower teacherMaxSpeedKph: a slower, safe teacher is better
    ///    for imitation (reinforcement learning can add speed later; it has to unlearn copied crashes).
    ///
    /// Point the trainer's demo_path at the output directory.
    ///
    /// Command line (training builds, e.g. Tools/ml record): -recordDemos DIR [-recordSeconds S]
    /// [-demoPrefix NAME] [-teacherMaxSpeed KPH] [-recordSeed N] turn recording on for that run.
    /// </summary>
    [DefaultExecutionOrder(0)] // after VehicleStepper (-40): acts at the end of each physics step
    public class DemonstrationSession : MonoBehaviour
    {
        [Header("Recording")]
        [Tooltip("Record demonstrations when entering Play mode")]
        public bool record = false;

        [Tooltip("File name prefix (letters/digits, up to 10). Each chunk is <prefix><nnnnn>.demo")]
        public string demonstrationName = "Racecar";

        [Tooltip("Output directory. Empty = Assets/Demonstrations/<prefix>")]
        public string outputDirectory = "";

        [Tooltip("Simulated seconds to record (counting dropped chunks). 0 = until stopped")]
        public float durationSeconds = 600f;

        [Tooltip("Exit Play mode (or the player) when the duration is reached")]
        public bool stopWhenDone = true;

        [Header("Teacher")]
        [Tooltip("Cap the controller's speed while recording (0 = no cap). Above it, the controller's throttle " +
            "decision is reduced, and the reduced decision is what's recorded, so the NN learns the cautious driving.")]
        public float teacherMaxSpeedKph = 0f;

        [Header("Resets")]
        [Tooltip("Periodically reset the car to rest. Off = one continuous drive (e.g. clean full runs)")]
        public bool forcedResets = true;

        [Tooltip("Seconds between forced resets, drawn uniformly from [min, max]")]
        public float minResetInterval = 6f;
        public float maxResetInterval = 30f;

        [Range(0f, 1f)]
        [Tooltip("Fraction of resets that also offset the car sideways and turn it")]
        public float perturbedResetFraction = 0.5f;

        [Tooltip("Max sideways offset for perturbed resets, as a fraction of the half road width")]
        [Range(0f, 1f)]
        public float maxLateralOffsetFraction = 0.5f;

        [Tooltip("Max heading offset for perturbed resets (degrees)")]
        public float maxHeadingOffsetDeg = 15f;

        [Header("Control noise (recorded actions stay noise-free)")]
        [Tooltip("Std. dev. of noise added to applied steering [-1, 1]")]
        public float steeringNoiseStd = 0.1f;

        [Tooltip("Std. dev. of noise added to applied throttle [-1, 1]")]
        public float throttleNoiseStd = 0.1f;

        [Tooltip("Seconds each noise sample is held")]
        public float noiseHoldSeconds = 0.25f;

        [Tooltip("If > 0: smooth noise that drifts instead of jumping (an Ornstein-Uhlenbeck process with this " +
            "correlation time, same standard deviations; noiseHoldSeconds is then unused). A slowly varying bias is " +
            "closer to a learned policy's own errors than a new random push every noiseHoldSeconds.")]
        public float noiseCorrelationSeconds = 0f;

        [Tooltip("Steering noise is full below this speed and scaled by (this / speed) above it")]
        public float steeringNoiseFullUntilKph = 100f;

        [Header("State kicks (controls untouched; off by default)")]
        [Tooltip("Now and then, instantly change the car's motion (a sideways velocity and a yaw rate) and let the " +
            "controller recover with its own controls. Unlike control noise, every recorded decision is then the " +
            "controller's real, closed-loop response: nothing interferes with what its controls do.")]
        public bool stateKicks = false;
        [Tooltip("Seconds between kicks, drawn uniformly from [min, max]; none within 2 s after a reset")]
        public float minKickInterval = 3f;
        public float maxKickInterval = 8f;
        [Tooltip("Largest sideways velocity change (m/s); each kick is 30-100% of it, either direction")]
        public float maxLateralKick = 2.5f;
        [Tooltip("Largest yaw-rate change (rad/s); each kick is 30-100% of it, either direction")]
        public float maxYawKick = 0.5f;

        [Header("Keeping only good driving")]
        [Tooltip("Length of each recorded chunk (.demo file); what gets dropped is decided per chunk")]
        public float chunkSeconds = 5f;

        [Tooltip("When the car wipes out, drop the recording of this many seconds before the wipeout registered " +
            "(a fall registers about 2 s after the car leaves the road). 0 = keep everything")]
        public float dropSecondsBeforeWipeout = 6f;

        [Tooltip("Drop chunks where the car stalls, has wheels off the road, or faces the wrong way")]
        public bool dropStruggling = true;

        [Tooltip("Seconds of struggling in a chunk before it's dropped")]
        public float struggleToleranceSeconds = 0.5f;

        [Tooltip("0 = different every run")]
        public int randomSeed = 0;

        [Header("Stats (read only)")]
        public int SegmentsKept;
        public int SegmentsDropped;
        public int DecisionsKept;
        public int Wipeouts;
        public float SecondsKept;
        public float SecondsDroppedForWipeouts;
        public float SecondsDroppedForStruggling;
        public float ElapsedSeconds;
        public bool Done;

        class Chunk
        {
            public string path;
            public float start, end;
            public int decisions;
            public float struggleSeconds;
            // Inputs sampled in this chunk; they count in the report only if the chunk is kept
            public readonly List<List<float>> observed = new List<List<float>>(), crisp = new List<List<float>>();
        }

        RacecarAgent agent;
        AIVehicle vehicle;
        DemonstrationRecorder recorder;
        Chunk current;
        readonly List<Chunk> pending = new List<Chunk>();   // closed, may still be dropped by a wipeout
        int chunkIndex;
        bool wipedOut;
        float nextResetTime;
        float nextNoiseTime;
        float nextKickTime;
        float lastResetTime;
        Vector2 unitNoise;
        System.Random rng;

        // For the report: the kept decisions' observations (as the NN sees them) and crisp inputs (before scaling)
        readonly List<List<float>> observedValues = new List<List<float>>();
        readonly List<List<float>> crispValues = new List<List<float>>();
        readonly List<float> crispSample = new List<float>();
        int decisionsSeen;

        // A standing start is slow on purpose; only count a stall after this long since a reset
        const float StallGraceSeconds = 3f;
        const float ResetSettleSeconds = 1f;
        const int MinChunkDecisions = 5;
        const float StallKph = 2f;
        const float WrongWayDeg = 60f;

        public string OutputDirectory => string.IsNullOrEmpty(outputDirectory)
            ? Path.Combine(Application.dataPath, "Demonstrations", SanitizedName)
            : outputDirectory;

        string SanitizedName
        {
            get
            {
                // ML-Agents keeps only letters/digits/space/dash and truncates names to 16 characters;
                // keep prefix + 5-digit index within that so the file name is exactly as predicted.
                var n = System.Text.RegularExpressions.Regex.Replace(demonstrationName ?? "", "[^a-zA-Z0-9]", "");
                if (n.Length > 10) n = n.Substring(0, 10);
                return string.IsNullOrEmpty(n) ? "Racecar" : n;
            }
        }

        void Awake()
        {
            // Command-line recording (training builds)
            var dir = Arg("-recordDemos");
            if (dir == null) return;
            record = true;
            outputDirectory = dir;
            stopWhenDone = true;
            if (float.TryParse(Arg("-recordSeconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) durationSeconds = s;
            if (Arg("-demoPrefix") is string prefix) demonstrationName = prefix;
            if (float.TryParse(Arg("-teacherMaxSpeed"), NumberStyles.Float, CultureInfo.InvariantCulture, out var kph)) teacherMaxSpeedKph = kph;
            if (int.TryParse(Arg("-recordSeed"), out var seed)) randomSeed = seed;
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        void Start()
        {
            if (!record)
            {
                enabled = false;
                return;
            }

            agent = GetComponent<RacecarAgent>();
            vehicle = GetComponentInParent<AIVehicle>();
            if (agent == null || vehicle == null)
            {
                Debug.LogError("DemonstrationSession needs a RacecarAgent on this object and an AIVehicle on it or a parent.");
                enabled = false;
                return;
            }

            if (GetComponent<DemonstrationRecorder>() is DemonstrationRecorder existing && existing.Record)
                Debug.LogWarning("DemonstrationSession: disable Record on the DemonstrationRecorder; the session records chunks itself.");

            rng = randomSeed == 0 ? new System.Random() : new System.Random(randomSeed);
            vehicle.WipedOut += OnWipedOut;
            vehicle.INTERNAL_TeacherSpeedCapKph = teacherMaxSpeedKph;

            Directory.CreateDirectory(OutputDirectory);
            Debug.Log($"[DemonstrationSession] Recording to {OutputDirectory}" +
                (teacherMaxSpeedKph > 0f ? $" (teacher max speed {teacherMaxSpeedKph:0} kph)" : ""));

            ScheduleNextReset();
            BeginChunk();
        }

        void OnDestroy()
        {
            if (vehicle != null)
            {
                vehicle.WipedOut -= OnWipedOut;
                vehicle.INTERNAL_ControlNoise = Vector2.zero;
                vehicle.INTERNAL_TeacherSpeedCapKph = 0f;
            }

            if (!Done && recorder != null)
            {
                EndChunk();
                FinalizePending(all: true);
            }
        }

        void OnWipedOut()
        {
            ++Wipeouts;
            wipedOut = true;
        }

        // Runs after VehicleStepper each physics step: controls for this step are decided and applied
        void FixedUpdate()
        {
            if (Done)
                return;

            ElapsedSeconds += Time.fixedDeltaTime;

            if (current != null)
            {
                if (vehicle.IsControlStep)
                {
                    ++current.decisions;
                    SampleInputs();
                }
                if (dropStruggling && IsStruggling()) current.struggleSeconds += Time.fixedDeltaTime;
            }

            if (wipedOut)
            {
                // The vehicle has already reset itself; drop the run-up and start a new chunk from there
                wipedOut = false;
                EndChunk();
                DropBefore(ElapsedSeconds - dropSecondsBeforeWipeout);
                lastResetTime = ElapsedSeconds;
                ScheduleNextReset();
                BeginChunk();
            }
            else if (forcedResets && ElapsedSeconds >= nextResetTime)
            {
                agent.EndEpisode();
                EndChunk();
                ForceReset();
                lastResetTime = ElapsedSeconds;
                ScheduleNextReset();
                BeginChunk();
            }
            else if (ElapsedSeconds - current.start >= Mathf.Max(0.5f, chunkSeconds) &&
                     !(forcedResets && nextResetTime - ElapsedSeconds < 0.5f * chunkSeconds))   // avoid a tiny chunk before a reset
            {
                // New file, same drive (no reset, episode continues)
                EndChunk();
                BeginChunk();
            }

            FinalizePending(all: false);

            if (noiseCorrelationSeconds > 0f)
            {
                // Ornstein-Uhlenbeck: mean-reverting with unit stationary standard deviation
                var dt = Time.fixedDeltaTime;
                var decay = dt / noiseCorrelationSeconds;
                var diffusion = Mathf.Sqrt(2f * decay);
                unitNoise += -unitNoise * decay + diffusion * new Vector2(Gaussian(), Gaussian());
            }
            else if (ElapsedSeconds >= nextNoiseTime)
            {
                unitNoise = new Vector2(Gaussian(), Gaussian());
                nextNoiseTime = ElapsedSeconds + Mathf.Max(Time.fixedDeltaTime, noiseHoldSeconds);
            }

            // Less steering noise at high speed (the same steering error matters much more there)
            var kph = Mathf.Abs(vehicle.Speed_kph);
            var steerScale = kph > steeringNoiseFullUntilKph ? steeringNoiseFullUntilKph / kph : 1f;
            vehicle.INTERNAL_ControlNoise = new Vector2(
                unitNoise.x * steeringNoiseStd * steerScale,
                unitNoise.y * throttleNoiseStd);

            if (stateKicks)
                Kick();

            if (durationSeconds > 0f && ElapsedSeconds >= durationSeconds)
                Finish();
        }

        /// <summary>Kicks recorded (stateKicks).</summary>
        public int Kicks;

        void Kick()
        {
            if (ElapsedSeconds < nextKickTime) return;
            if (ElapsedSeconds - lastResetTime < ResetSettleSeconds + 1f)
            {
                nextKickTime = lastResetTime + ResetSettleSeconds + 1f;
                return;
            }
            var rb = vehicle.INTERNAL_Rigidbody;
            float Signed(float max) => (rng.NextDouble() < 0.5 ? -1f : 1f) * max * (0.3f + 0.7f * (float)rng.NextDouble());
            rb.linearVelocity += vehicle.transform.right * Signed(maxLateralKick);
            rb.angularVelocity += vehicle.transform.up * Signed(maxYawKick);
            ++Kicks;
            nextKickTime = ElapsedSeconds + Mathf.Lerp(minKickInterval, maxKickInterval, (float)rng.NextDouble());
        }

        bool IsStruggling()
        {
            var rt = vehicle.Racetrack;
            if (rt == null || !rt.IsInitialized) return false;

            var sinceReset = ElapsedSeconds - lastResetTime;
            if (sinceReset < ResetSettleSeconds) return false;   // dropped onto the road at a reset

            if (sinceReset > StallGraceSeconds && Mathf.Abs(vehicle.Speed_kph) < StallKph)
                return true;

            for (int i = 0; i < 4; i++)
                if (vehicle.TryGetWheelTelemetry(i, out var w) && !w.grounded)
                    return true;

            var heading = Vector3.Angle(Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up), rt.ClosestPointDirectionOnPath);
            return heading > WrongWayDeg;
        }

        void ForceReset()
        {
            float lateral = 0f, heading = 0f;
            if (rng.NextDouble() < perturbedResetFraction)
            {
                var halfWidth = vehicle.Racetrack != null ? vehicle.Racetrack.HalfRoadWidth : 0f;
                lateral = Uniform(-1f, 1f) * maxLateralOffsetFraction * halfWidth;
                heading = Uniform(-1f, 1f) * maxHeadingOffsetDeg;
            }

            vehicle.INTERNAL_ForceReset(lateral, heading);
        }

        void BeginChunk()
        {
            var name = $"{SanitizedName}{chunkIndex++:D5}";
            current = new Chunk { path = Path.Combine(OutputDirectory, name + ".demo"), start = ElapsedSeconds };
            if (File.Exists(current.path))
                File.Delete(current.path);

            recorder = gameObject.AddComponent<DemonstrationRecorder>();
            recorder.DemonstrationName = name;
            recorder.DemonstrationDirectory = OutputDirectory;
            recorder.Record = true;
        }

        void EndChunk()
        {
            if (recorder == null)
                return;

            recorder.Close();
            Destroy(recorder);
            recorder = null;

            current.end = ElapsedSeconds;
            pending.Add(current);
            current = null;
        }

        // A wipeout: drop pending chunks that end after `from` (they lead up to it)
        void DropBefore(float from)
        {
            if (dropSecondsBeforeWipeout <= 0f) return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var c = pending[i];
                if (c.end <= from) continue;
                Delete(c);
                SecondsDroppedForWipeouts += c.end - c.start;
                pending.RemoveAt(i);
            }
        }

        // Chunks older than the wipeout window are final: keep, or drop for struggling
        void FinalizePending(bool all)
        {
            for (int i = 0; i < pending.Count; )
            {
                var c = pending[i];
                if (!all && ElapsedSeconds - c.end <= dropSecondsBeforeWipeout) { i++; continue; }
                pending.RemoveAt(i);

                if (c.decisions < MinChunkDecisions)
                    Delete(c);
                else if (dropStruggling && c.struggleSeconds > struggleToleranceSeconds)
                {
                    Delete(c);
                    SecondsDroppedForStruggling += c.end - c.start;
                }
                else
                {
                    ++SegmentsKept;
                    DecisionsKept += c.decisions;
                    SecondsKept += c.end - c.start;
                    Merge(observedValues, c.observed);
                    Merge(crispValues, c.crisp);
                }
            }
        }

        void Delete(Chunk c)
        {
            ++SegmentsDropped;
            if (File.Exists(c.path))
                File.Delete(c.path);
        }

        void Finish()
        {
            agent.EndEpisode();
            EndChunk();
            FinalizePending(all: true);
            vehicle.INTERNAL_ControlNoise = Vector2.zero;
            vehicle.INTERNAL_TeacherSpeedCapKph = 0f;
            Done = true;

            var report = Report();
            Debug.Log("[DemonstrationSession] " + report.Replace("\n", "\n[DemonstrationSession] "));
            try { File.WriteAllText(Path.Combine(OutputDirectory, SanitizedName + "_report.txt"), report + "\n"); }
            catch (IOException e) { Debug.LogWarning($"[DemonstrationSession] Could not write the report: {e.Message}"); }

            if (stopWhenDone)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(0);
#endif
            }
        }

        /// <summary>What was recorded, what was dropped and why, and whether the controller makes a good teacher.</summary>
        public string Report()
        {
            var minutes = Mathf.Max(ElapsedSeconds / 60f, 1e-3f);
            var total = Mathf.Max(SecondsKept + SecondsDroppedForWipeouts + SecondsDroppedForStruggling, 1e-3f);
            var wipeoutsPer10Min = 10f * Wipeouts / minutes;
            var keptFraction = SecondsKept / total;

            var sb = new StringBuilder();
            sb.Append($"Recorded {ElapsedSeconds:0} s on {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            if (teacherMaxSpeedKph > 0f) sb.Append($" (teacher max speed {teacherMaxSpeedKph:0} kph)");
            sb.Append($"\nKept {SecondsKept:0} s ({100f * keptFraction:0}%, {DecisionsKept} decisions in {SegmentsKept} files)");
            sb.Append($"\nDropped {SecondsDroppedForWipeouts:0} s before {Wipeouts} wipeouts ({wipeoutsPer10Min:0.0} per 10 min) and {SecondsDroppedForStruggling:0} s of struggling");
            sb.Append($"\nFiles in {OutputDirectory}");

            // Perturbed resets and control noise make recording harder than a race, so some wipeouts are normal
            if (wipeoutsPer10Min > 6f || keptFraction < 0.85f)
                sb.Append("\nTEACHER WARNING: your controller wipes out or struggles often while recording. Imitation copies its driving, " +
                    "so consider recording with a lower teacherMaxSpeedKph (a slower, safe teacher is better; reinforcement learning " +
                    "can add speed later), less noise/perturbation, or improving the controller.");
            else
                sb.Append("\nTeacher looks good.");

            AppendInputRanges(sb);
            return sb.ToString();
        }

        void SampleInputs()
        {
            // Skip the first second: the agent may not have collected observations yet
            if (++decisionsSeen <= 30 || current == null)
                return;
            Sample(current.observed, agent.GetObservations());
            crispSample.Clear();
            vehicle.CollectCrispInputs(crispSample);
            Sample(current.crisp, crispSample);
        }

        static void Merge(List<List<float>> into, List<List<float>> values)
        {
            while (into.Count < values.Count)
                into.Add(new List<float>());
            for (int i = 0; i < values.Count; i++)
                into[i].AddRange(values[i]);
        }

        static void Sample(List<List<float>> values, IReadOnlyList<float> sample)
        {
            while (values.Count < sample.Count)
                values.Add(new List<float>());
            for (int i = 0; i < sample.Count; i++)
                values[i].Add(sample[i]);
        }

        // 95% of the values have a magnitude at most this: a "typical largest value", ignoring rare extremes
        static float TypicalLargest(List<float> values)
        {
            var magnitudes = new List<float>(values.Count);
            foreach (var v in values) magnitudes.Add(Mathf.Abs(v));
            magnitudes.Sort();
            return magnitudes.Count == 0 ? 0f : magnitudes[Mathf.Clamp(Mathf.RoundToInt(0.95f * (magnitudes.Count - 1)), 0, magnitudes.Count - 1)];
        }

        static string Range(List<float> values)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in values) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
            return $"min {min:0.###}, max {max:0.###}, 95% within +-{TypicalLargest(values):0.###}";
        }

        // What the NN saw while recording, so the observation scales can be chosen from data
        void AppendInputRanges(StringBuilder sb)
        {
            sb.Append("\n\nCrisp inputs from your CollectCrispInputs() in the kept recording, before scaling. Each one's " +
                "observation scale should be about its typical largest value (the \"95% within\" number):");
            if (crispValues.Count == 0)
                sb.Append("\n  (none: CollectCrispInputs() adds nothing)");
            for (int i = 0; i < crispValues.Count; i++)
                sb.Append($"\n  {i}: {Range(crispValues[i])}");

            sb.Append("\nObservations in the kept recording as the NN sees them (after NNVehicle's scaling), aim for roughly [-1, 1]:");
            if (observedValues.Count == 0)
                sb.Append("\n  (none)");
            for (int i = 0; i < observedValues.Count; i++)
            {
                var typical = TypicalLargest(observedValues[i]);
                sb.Append($"\n  {i}: {Range(observedValues[i])}");
                if (typical > 1.5f)
                    sb.Append("  <- mostly outside [-1, 1]: raise its scale");
                else if (typical < 0.05f)
                    sb.Append("  <- barely varies: lower its scale (or does it carry information?)");
            }
            if (observedValues.Count > 0)
                sb.Append("\nIf you change what the NN observes or how it is scaled, rebuild and record again: the recordings store observations.");
        }

        void ScheduleNextReset()
        {
            nextResetTime = ElapsedSeconds + Uniform(minResetInterval, Mathf.Max(minResetInterval, maxResetInterval));
        }

        float Uniform(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        float Gaussian()
        {
            // Box-Muller
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }
    }
}
