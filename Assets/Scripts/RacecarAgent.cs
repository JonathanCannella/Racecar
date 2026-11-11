using UnityEngine;

using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

namespace GameAI
{
    /// <summary>
    /// ML-Agents agent that drives the AIVehicle on the same GameObject or a parent (e.g. an agent
    /// prefab placed as a child of the car). Subclass it and override CollectObservations()
    /// (and add rewards) for an NN driver.
    ///
    /// Actions: 2 continuous, [0] = steering, [1] = throttle, both in [-1, 1] (same as FL outputs).
    /// Decisions happen on the vehicle's control steps (AIVehicle.ControlStepInterval), driven by VehicleStepper.
    ///
    /// In heuristic mode (Behavior Type "Heuristic Only", or "Default" with no model and no trainer),
    /// the vehicle's own controller decides (e.g. a FuzzyVehicle's UpdateControls(), or player input)
    /// and Heuristic() reports that decision, so a DemonstrationRecorder records the FL (or human)
    /// driving exactly as it would run without the agent. Otherwise the agent's policy drives.
    /// </summary>
    public class RacecarAgent : Agent
    {
        public const int NumActions = 2;
        public const int SteeringActionIndex = 0;
        public const int ThrottleActionIndex = 1;

        // ML-Agents sends the game clamp(raw, -3, 3) / 3 of the policy's raw continuous output, but behavioral
        // cloning fits the raw output to the recorded actions. Demonstrations therefore record the controller's
        // [-1, 1] controls multiplied by 3, so a cloned policy reproduces the demonstrated controls.
        public const float DemonstrationActionScale = 3f;

        AIVehicle vehicle;
        protected AIVehicle Vehicle => vehicle != null ? vehicle : (vehicle = GetComponentInParent<AIVehicle>());

        protected IRacetrackData Racetrack => Vehicle != null ? Vehicle.Racetrack : null;

        BehaviorParameters behaviorParameters;
        BehaviorParameters BehaviorParams =>
            behaviorParameters != null ? behaviorParameters : (behaviorParameters = GetComponent<BehaviorParameters>());

        // True when the vehicle's own controller makes the decisions and this agent observes/records them
        // (heuristic mode: Behavior Type "Heuristic Only", or "Default" with no model and no trainer)
        protected virtual bool VehicleControllerDecides =>
            BehaviorParams == null || BehaviorParams.IsInHeuristicMode();

        internal bool INTERNAL_VehicleControllerDecides => VehicleControllerDecides;

        // If you override Initialize(), call base.Initialize().
        public override void Initialize()
        {
            if (Vehicle != null)
                Vehicle.INTERNAL_Agent = this;
        }

        // Reports the vehicle's own decision for this step (its controller already ran this step),
        // in the policy's raw action units (see DemonstrationActionScale).
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var continuousActions = actionsOut.ContinuousActions;
            continuousActions[SteeringActionIndex] = Vehicle.Steering * DemonstrationActionScale;
            continuousActions[ThrottleActionIndex] = Vehicle.Throttle * DemonstrationActionScale;
        }

        // Applies the policy's steering and throttle ([-1, 1]) to the car. In heuristic mode the car's own
        // controller has already set its controls this step, so nothing is applied.
        public override void OnActionReceived(ActionBuffers actions)
        {
            if (VehicleControllerDecides)
                return;

            // (AIVehicle has no reverse gear: negative throttle only brakes)
            var continuousActions = actions.ContinuousActions;
            Vehicle.INTERNAL_SetControlsFromAgent(
                Mathf.Clamp(continuousActions[SteeringActionIndex], -1f, 1f),
                Mathf.Clamp(continuousActions[ThrottleActionIndex], -1f, 1f));
        }
    }
}
