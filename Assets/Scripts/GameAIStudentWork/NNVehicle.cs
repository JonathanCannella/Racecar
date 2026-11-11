using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

using GameAI;

namespace GameAI
{

    // Neural network driver. Lives on the NN agent object that is a child of the race car
    // (see the RacecarNNAgent prefab). The car itself is your FuzzyVehicle:
    //  - Behavior Type "Default" with no model and no trainer connected (or "Heuristic Only"):
    //    your FuzzyVehicle drives and this agent only observes. Enable "record" on the
    //    DemonstrationSession to record your FL driving as training demonstrations
    //    (or record all tracks at once with Tools/ml/nntools record).
    //  - Training (mlagents-learn connected) or with a trained model: this agent drives.
    // Actions: [0] = steering, [1] = throttle, both in [-1, 1] (handled by RacecarAgent).
    //
    // Your work here: what the NN observes (CollectObservations, and observationScales in the Inspector) and
    // how it is rewarded (the TODO (reward) items). See the NN guide, Guidelines and Common Pitfalls.
    public class NNVehicle : RacecarAgent
    {
        // Settings the trainer config can override without rebuilding the game (environment_parameters at the end
        // of each config), read at the start of every episode in OnEpisodeBegin(). Add your reward settings the
        // same way, so you can tune them between training runs.
        [Header("Episodes (defaults; trainer environment_parameters override)")]
        [Tooltip("End the episode at a wipeout (wipeout_ends_episode). Off: the car resets and the episode continues, " +
            "so a wipeout also costs the time lost getting back up to speed (like in a race).")]
        public bool wipeoutEndsEpisode = false;

        [Tooltip("How long a training episode lasts (seconds)")]
        public float EpisodeLenSec = 360f;

        [Header("Observations")]
        [Tooltip("One per crisp input, in order: a typical largest value of each (e.g. 100 for a speed in kph, " +
            "90 for an angle in degrees). Each input is divided by its scale so the NN sees values roughly in [-1, 1]. " +
            "The report after Tools/ml/nntools record lists the range of each input.")]
        public float[] observationScales = { };

        [Header("Debug")]
        [Tooltip("Total reward this episode (everything given with Reward())")]
        public float DB_RewardTotal = 0f;

        bool endEpisodeOnWipeout;
        float episodeStart = 0f;

        readonly List<float> crisp = new List<float>();
        readonly HashSet<int> warnedScale = new HashSet<int>();
        bool checkedObservationCount;


        public override void Initialize()
        {
            base.Initialize();

            // The car resets itself if it falls off the road, flips over, gets turned around, stuck, etc.
            Vehicle.WipedOut += OnWipedOut;
        }

        protected override void OnDisable()
        {
            if (Vehicle != null)
                Vehicle.WipedOut -= OnWipedOut;

            base.OnDisable();
        }


        // This is called at the beginning of each episode
        public override void OnEpisodeBegin()
        {
            // Settings from the trainer config (environment_parameters), or the defaults above
            var parameters = Academy.Instance.EnvironmentParameters;
            endEpisodeOnWipeout = parameters.GetWithDefault("wipeout_ends_episode", wipeoutEndsEpisode ? 1f : 0f) > 0.5f;

            // TODO (reward): read your reward settings here, e.g.
            //   mySetting = parameters.GetWithDefault("my_setting", mySettingDefault);
            // (with my_setting: 1.0 under environment_parameters in the trainer config), and reset anything your
            // reward keeps track of during an episode. Racetrack may not exist yet for the very first episode,
            // while the scene is loading.

            episodeStart = Time.timeSinceLevelLoad;
            DB_RewardTotal = 0f;
        }


        // The car wiped out (fell off the road, flipped over, got stuck or turned around) and has been reset
        void OnWipedOut()
        {
            // TODO (reward): should a wipeout cost something, and how much compared with what good driving earns?

            if (endEpisodeOnWipeout)
                EndEpisode();
        }


        public override void CollectObservations(VectorSensor sensor)
        {
            // The observations are the game state the NN decides from (and learns from). Start with the same
            // crisp inputs your FuzzyVehicle's rules use: implement CollectCrispInputs() in your FuzzyVehicle.
            // Each is divided by its observationScales entry (set them in the Inspector) so values are roughly
            // in [-1, 1]. Fixed scales matter: the trainer's "normalize" option rescales by running statistics
            // that drift as the driving changes, so a situation can look different to the NN later in training.
            // Then think about what else would help, e.g. where the road goes further ahead
            // (Racetrack.GetPointAndDirectionAhead), expressed relative to the car. Add observations with
            // sensor.AddObservation(...). The count must be the same every step and match the Behavior
            // Parameters "Vector Observation Space Size" on the RacecarNNAgent prefab.
            if (sensor == null)
            {
                // Space Size is still 0, so ML-Agents gives no sensor: report the size to set instead
                crisp.Clear();
                Vehicle.CollectCrispInputs(crisp);
                CheckObservationCount(crisp.Count);
                return;
            }
            crisp.Clear();
            Vehicle.CollectCrispInputs(crisp);
            for (int i = 0; i < crisp.Count; i++)
                sensor.AddObservation(Scaled(i, crisp[i]));


            // TODO (reward): this runs once per decision (30 per second). Reward what you want the NN to do with
            // Reward(...), and penalize what you don't with a negative value. Reinforcement learning optimizes
            // exactly this and nothing else, so read Reward Structure in the NN guide first. The racetrack API
            // (Racetrack, Vehicle) has what you can measure.


            // Keep this last: it counts every observation added above, including any you add yourself
            CheckObservationCount(GetObservations().Count);
        }


        // Gives the NN a reward (negative: a penalty) for its current decision; DB_RewardTotal shows the
        // episode's total in the Inspector.
        protected void Reward(float r)
        {
            AddReward(r);
            DB_RewardTotal += r;
        }


        float Scaled(int i, float value)
        {
            var hasScale = i < observationScales.Length && observationScales[i] != 0f;
            var scale = hasScale ? observationScales[i] : 1f;
            var scaled = value / scale;
            if (!hasScale)
                WarnScaleOnce(i, $"crisp input {i} has no scale (Observation Scales has {observationScales.Length} " +
                    $"entries). Set observationScales[{i}] (Inspector) to a typical largest value of that input.");
            else if (Mathf.Abs(scaled) > 3f)
                WarnScaleOnce(i, $"crisp input {i} is {value:0.###}, {scaled:0.#} after dividing by its scale " +
                    $"{scale}. Set observationScales[{i}] (Inspector) to a typical largest value of that input.");
            return Mathf.Clamp(scaled, -5f, 5f);
        }

        void WarnScaleOnce(int i, string message)
        {
            if (warnedScale.Add(i))
                Debug.LogWarning("NNVehicle: " + message);
        }


        // Mismatched observation counts are silently padded or cut by ML-Agents; say so clearly instead
        void CheckObservationCount(int count)
        {
            // Not until the track is ready: before that, CollectCrispInputs() adds nothing
            if (checkedObservationCount || Racetrack == null || !Racetrack.IsInitialized) return;
            checkedObservationCount = true;
            var expected = GetComponent<BehaviorParameters>().BrainParameters.VectorObservationSize;
            if (count == 0)
                Debug.LogError("NNVehicle: no observations. Implement CollectCrispInputs() in your FuzzyVehicle " +
                    "(add the crisp values your fuzzy rules use), or add your own observations in NNVehicle.");
            else if (count != expected)
                Debug.LogError($"NNVehicle adds {count} observations, but Behavior Parameters > Vector Observation " +
                    $"Space Size is {expected}. Set it to {count} on the RacecarNNAgent prefab (then rebuild and re-record).");
        }


        public override void OnActionReceived(ActionBuffers actions)
        {
            // Applies steering and throttle to the car
            base.OnActionReceived(actions);

            // Time limit: the episode is interrupted (not a failure), and the car keeps driving
            var episodeTime = Time.timeSinceLevelLoad - episodeStart;
            if (episodeTime > EpisodeLenSec)
            {
                EpisodeInterrupted();
            }
        }

    }

}
