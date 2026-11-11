using System.IO;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

using GameAI;

#if UNITY_EDITOR
using UnityEditor;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;
#endif

namespace Tests
{

    public class RacingTest
    {

        const int timeScale = 1; // simulated time per rendered frame. Higher values draw fewer frames for the same
                                 // 60 Hz physics, so a race can take less time in the Editor with the same results.

        const int PlayMatchTimeOutMS = int.MaxValue; // don't mess with this; add it to new tests
                                                     // as [Timeout(PlayMatchTimeOutMS)] (see below for
                                                     // example) It stops early default timeout

        public RacingTest()
        {

        }



        [Timeout(PlayMatchTimeOutMS)]
        private IEnumerator RunRaceFromConfig(string configName, System.Action<AIVehicle> configureCar = null)
        {
            var config = Resources.Load<RaceTestConfig>($"RaceTrackTestConfigs/{configName}");
            Assert.IsNotNull(config, $"Missing grading config {configName}");

            return _TestFuzzyRace(
                configName,
                configureCar,
                config.trackPrefabName,
                config.duration_s,
                config.minAllowedSpeed,
                config.targetSpeed,
                config.extraCreditSpeed,
                config.maxAllowedWipeouts,
                config.maxPartialPenaltyWipeouts,
                config.speedScoreWeight,
                config.wipeoutScoreWeight,
                config.extraCreditWeight,
                config.gradeWeight
            );
        }


        // Your FuzzyVehicle in the graded races (Test Runner category "FL")

        [UnityTest, Category("FL")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator Race_Curvy_5m() => RunRaceFromConfig("Race_Curvy_5m");
 
        [UnityTest, Category("FL")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator Race_Winding_5m() => RunRaceFromConfig("Race_Winding_5m");


        [UnityTest, Category("FL")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator Race_FastSweepers_5m() => RunRaceFromConfig("Race_FastSweepers_5m");


        [UnityTest, Category("FL")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator Race_DragRace_1m() => RunRaceFromConfig("Race_Drag_1m");


#if UNITY_EDITOR
        // Your trained NN in the graded races (Test Runner category "NN"): the RacecarNNAgent prefab is put on the
        // car (your FuzzyVehicle then only provides the crisp inputs), always taking its most likely action.
        // The model is the one assigned to the prefab's Behavior Parameters, or the .onnx file in the NN_MODEL
        // environment variable (e.g. Tools/ml/nntools test --nn --model results/rl/Racecar/Racecar-900000.onnx).

        [UnityTest, Category("NN")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator NN_Race_Curvy_5m() => RunRaceFromConfig("Race_Curvy_5m", AddNNAgent);

        [UnityTest, Category("NN")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator NN_Race_Winding_5m() => RunRaceFromConfig("Race_Winding_5m", AddNNAgent);

        [UnityTest, Category("NN")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator NN_Race_FastSweepers_5m() => RunRaceFromConfig("Race_FastSweepers_5m", AddNNAgent);

        [UnityTest, Category("NN")]
        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator NN_Race_DragRace_1m() => RunRaceFromConfig("Race_Drag_1m", AddNNAgent);

        const string NNAgentPrefabPath = "Assets/Prefab/RacecarNNAgent.prefab";
        const string TestedModelPath = "Assets/NNModels/TestedModel.onnx";

        static void AddNNAgent(AIVehicle car)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NNAgentPrefabPath);
            Assert.IsNotNull(prefab, $"Missing {NNAgentPrefabPath}");
            var agent = Object.Instantiate(prefab, car.transform);
            var behavior = agent.GetComponent<BehaviorParameters>();

            var modelFile = System.Environment.GetEnvironmentVariable("NN_MODEL");
            if (!string.IsNullOrEmpty(modelFile))
            {
                Assert.IsTrue(File.Exists(modelFile), $"NN_MODEL file not found: {modelFile}");
                Directory.CreateDirectory(Path.GetDirectoryName(TestedModelPath));
                File.Copy(modelFile, TestedModelPath, true);
                AssetDatabase.ImportAsset(TestedModelPath, ImportAssetOptions.ForceUpdate);
                behavior.Model = AssetDatabase.LoadAssetAtPath<ModelAsset>(TestedModelPath);
                Debug.Log($"[RacingTest] NN model: {modelFile}");
            }
            Assert.IsNotNull(behavior.Model, "No NN model: assign your trained .onnx to the RacecarNNAgent prefab's " +
                "Behavior Parameters > Model, or set NN_MODEL (nntools test --nn --model FILE).");

            behavior.BehaviorType = BehaviorType.InferenceOnly;
            behavior.DeterministicInference = true;
        }
#endif
 


        private IEnumerator LoadHarnessAndSpawnPrefab(string prefabName, System.Action<AIVehicle> configureCar = null)
        {
            const string HarnessName = "HarnessRuntime";

            Scene harness;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.name == HarnessName)
                {
                    var op = SceneManager.UnloadSceneAsync(s);
                    if (op != null)
                        while (!op.isDone) yield return null;
                    break;
                }
            }

            harness = SceneManager.CreateScene(HarnessName);
            SceneManager.SetActiveScene(harness);

            GameObject prefab = Resources.Load<GameObject>($"Prefab/{prefabName}");

            Assert.IsNotNull(prefab, $"Could not find prefab '{prefabName}' in Resources/Prefab/.");

            Debug.Log($"[RacingTest] Spawning prefab '{prefab.name}' into scene '{harness.name}'.");
            var instance = Object.Instantiate(prefab);
            if (configureCar != null)
            {
                var car = instance.GetComponentInChildren<AIVehicle>(true);
                Assert.IsNotNull(car, $"No car in {prefabName}");
                configureCar(car);
            }
            if (instance.scene != harness)
                SceneManager.MoveGameObjectToScene(instance, harness);

            yield return null;

            Assert.AreEqual(HarnessName, SceneManager.GetActiveScene().name, "Active scene should be the harness.");
            Assert.AreEqual(harness, instance.scene, "Prefab instance should be in the harness scene.");
        }

        [Timeout(PlayMatchTimeOutMS)]
        public IEnumerator _TestFuzzyRace(string raceName, System.Action<AIVehicle> configureCar, string raceTrackPrefabName, float duration_s,
            float minAllowedSpeed, float targetSpeed, float extraCreditSpeed,
            int maxAllowedWipeouts, int maxPartialPenaltyWipeouts,
            float speedScoreWeight, float wipeoutScoreWeight, float extraCreditWeight, float gradeWeight)
        {
            Time.timeScale = timeScale;

            GameManager.INTERNAL_overrideSimulationMode = GameManager.SimulationMode.FPS_60_1X_SimTime;

            yield return LoadHarnessAndSpawnPrefab(raceTrackPrefabName, configureCar);

            yield return new WaitForSeconds(duration_s);

            var gm = GameManager.Instance;

            Debug.Log($"Km/H LTA: {gm.KpHLTA} Num wipeouts: {gm.Wipeouts} meters: {gm.MetersTravelled}");


            var speedPenalty = Mathf.Lerp(speedScoreWeight, 0f,

                Power(
                    Mathf.InverseLerp(minAllowedSpeed, targetSpeed, gm.KpHLTA)
                    , 0.5f)

                    );


            var wipeoutPenalty = Mathf.Lerp(wipeoutScoreWeight, 0f,
                    1f - Mathf.InverseLerp(maxAllowedWipeouts, maxPartialPenaltyWipeouts, gm.Wipeouts)
                );


            float extraCredit = 0f;

            if (gm.Wipeouts <= 0)
            {
                extraCredit = Mathf.Lerp(0f, extraCreditWeight,

                     Power(
                         Mathf.InverseLerp(targetSpeed, extraCreditSpeed, gm.KpHLTA)
                         , 0.5f)

                         );

                Debug.Log($"Extra credit earned: {extraCredit}");
            }
            else
            {
                Debug.Log($"Extra credit only earned if no wipeouts!");
            }

            var totalScore = (speedScoreWeight - speedPenalty) +
                (wipeoutScoreWeight - wipeoutPenalty) +
                extraCredit;

            Debug.Log($"Estimated Total Score: {totalScore * 100}%");
            Debug.Log($"Estimated Weighted Grade Contribution (wt: {gradeWeight}): {gradeWeight * totalScore * 100}");
            Debug.Log($"[RacingTest] {(configureCar != null ? "NN" : "FL")} {raceName}: {gm.KpHLTA:0.0} kph (target {targetSpeed}), " +
                $"wipeouts {gm.Wipeouts} (max {maxAllowedWipeouts}), estimated score {totalScore * 100:0.0}% (weight {gradeWeight})");

        }

        float Power(float t, float strength)
        {
            return Mathf.Pow(t, strength);
        }


    }

}
