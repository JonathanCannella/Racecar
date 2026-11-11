using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

using Unity.MLAgents;

namespace GameAI
{
    /// <summary>
    /// Drives every AIVehicle's physics step in a fixed order, so vehicle controllers and ML-Agents
    /// agents see the same state and act on the same step:
    ///
    ///   (WheelTelemetrySampler -70: wheel contact, before the road can change; road generator -60,
    ///    PathTracker -50 already ran this step)
    ///   1. each vehicle: refresh state; on control steps its own controller decides (FL / player input)
    ///   2. if any vehicle has a RacecarAgent: agents request decisions on control steps, then one
    ///      Academy step (observations -> policy or Heuristic -> OnActionReceived sets controls)
    ///   3. each vehicle: wipeout checks, apply controls to wheels
    ///   (physics simulation)
    ///
    /// Created automatically when the first vehicle is enabled. ML-Agents is only touched (and its
    /// automatic stepping disabled) when an agent is present.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class VehicleStepper : MonoBehaviour
    {
        static VehicleStepper s_Instance;

        readonly List<AIVehicle> vehicles = new List<AIVehicle>();
        readonly List<AIVehicle> stepping = new List<AIVehicle>();

        bool disabledAcademyAutoStepping = false;

        internal static void Register(AIVehicle vehicle)
        {
            if (s_Instance == null)
            {
                var go = new GameObject(nameof(VehicleStepper));
                s_Instance = go.AddComponent<VehicleStepper>();
                go.AddComponent<WheelTelemetrySampler>();
            }

            if (!s_Instance.vehicles.Contains(vehicle))
                s_Instance.vehicles.Add(vehicle);
        }

        internal static void SampleWheelTelemetry()
        {
            if (s_Instance == null) return;
            foreach (var v in s_Instance.vehicles)
            {
                if (v != null && v.INTERNAL_Started)
                    v.INTERNAL_SampleWheelTelemetry();
            }
        }

        internal static void Unregister(AIVehicle vehicle)
        {
            if (s_Instance != null)
                s_Instance.vehicles.Remove(vehicle);
        }

        void FixedUpdate()
        {
            stepping.Clear();
            foreach (var v in vehicles)
            {
                if (v != null && v.INTERNAL_Started)
                    stepping.Add(v);
            }

            bool anyAgent = false;

            foreach (var v in stepping)
            {
                v.INTERNAL_BeginPhysicsStep();

                var agent = v.INTERNAL_Agent;
                if (agent != null && agent.isActiveAndEnabled)
                {
                    anyAgent = true;
                    if (v.IsControlStep)
                        agent.RequestDecision();
                }
            }

            if (anyAgent)
            {
                if (!disabledAcademyAutoStepping)
                {
                    Academy.Instance.AutomaticSteppingEnabled = false;
                    disabledAcademyAutoStepping = true;
                }

                Academy.Instance.EnvironmentStep();
            }

            foreach (var v in stepping)
            {
                v.INTERNAL_EndPhysicsStep();
            }
        }

        void OnDestroy()
        {
            // Restore ML-Agents' own stepping for later scenes without vehicles (e.g. other ML-Agents demos).
            // Not here: enabling it creates a GameObject, which isn't allowed while the scene is closing.
            if (disabledAcademyAutoStepping)
            {
                SceneManager.sceneLoaded -= RestoreAcademyAutoStepping;
                SceneManager.sceneLoaded += RestoreAcademyAutoStepping;
            }

            if (s_Instance == this)
                s_Instance = null;
        }

        static void RestoreAcademyAutoStepping(Scene scene, LoadSceneMode mode)
        {
            SceneManager.sceneLoaded -= RestoreAcademyAutoStepping;

            // A new stepper with agents disables it again on its first physics step
            if (Academy.IsInitialized && (s_Instance == null || !s_Instance.disabledAcademyAutoStepping))
                Academy.Instance.AutomaticSteppingEnabled = true;
        }
    }
}
