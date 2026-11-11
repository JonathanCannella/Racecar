/*
 * This code is part of Arcade Car Physics for Unity by Saarg (2018)
 * 
 * This is distributed under the MIT Licence (see LICENSE.md for details)
 */
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using VehicleBehaviour.Trails;


namespace GameAI
{
    // Created by Edward Kay-Coles a.k.a Hoeloe
    public class TrailEmitter : MonoBehaviour
    {

        //Stores all live trails
        private LinkedList<Trail> trails = new LinkedList<Trail>();

        //Parameters
        public float width = 0.1f;
        public float decayTime = 1f;
        public Material material;
        [Range(0,10)]
        public int roughness = 0;
        public bool softSourceEnd = false;
        public bool trailing = false;

        [Header("Trail thresholds (rubber)")]
        [SerializeField, Range(0f, 2f)] private float trailSideStart = 0.4f;
        [SerializeField, Range(0f, 2f)] private float trailSideStop  = 0.2f;
        [SerializeField, Range(0f, 2f)] private float trailFwdStart  = 0.98f;
        [SerializeField, Range(0f, 2f)] private float trailFwdStop   = 0.98f;

        [Header("Trail thresholds (rubber) — slip ratio (PhysX)")]
        [SerializeField, Range(0f, 2f)] private float trailSlideStart_ratio = 0.20f;
        [SerializeField, Range(0f, 2f)] private float trailSlideStop_ratio  = 0.15f;

        [Header("Behavior")]
        [SerializeField, Tooltip("If true, use combined slip (max of |sideways| and |forward|) for trail start/stop. If false, use axis-specific thresholds.")]
        private bool useCombinedSlipForTrails = true;

        public Transform parent;

        public Vector3 offset;

        WheelCollider wheel;

        [SerializeField]
        private TireScreecher screecher;

        [Header("Config (optional)")]
        [SerializeField] private TireTrailConfig trailConfig;
        [SerializeField] private bool applyConfigOnStart = true;

        private void ApplyConfig(TireTrailConfig cfg)
        {
            if (cfg == null) return;
            // Render params
            width = cfg.width;
            decayTime = cfg.decayTime;
            material = cfg.material ? cfg.material : material;
            roughness = cfg.roughness;
            softSourceEnd = cfg.softSourceEnd;
            // Thresholds
            trailSideStart = cfg.trailSideStart;
            trailSideStop  = cfg.trailSideStop;
            trailFwdStart  = cfg.trailFwdStart;
            trailFwdStop   = cfg.trailFwdStop;
            trailSlideStart_ratio = cfg.trailCombinedStart;
            trailSlideStop_ratio  = cfg.trailCombinedStop;
            useCombinedSlipForTrails = cfg.useCombinedSlipForTrails;
        }


        //Checks if the most recent trail is active or not
        public bool Active
        {
            get { return (trails.Count == 0 ? false : (!trails.Last.Value.Finished)); }
        }

        void Start()
        {
            wheel = GetComponent<WheelCollider>();

            if (screecher == null)
                Debug.LogWarning("No screecher!");

            if (applyConfigOnStart)
            {
                ApplyConfig(trailConfig);
            }
            // vehicle = GetComponentInParent<WheelVehicle>();

                // if (vehicle == null)
                // 	Debug.LogWarning("Tire trail couldn't find parent vehicle");
        }

        // Update is called once per frame
        void Update()
        {
            WheelHit hit;
            wheel.GetGroundHit(out hit);

            // PhysX slip ratios (dimensionless)
            float sideAbs = Mathf.Abs(hit.sidewaysSlip);
            float fwdAbs  = Mathf.Abs(hit.forwardSlip);

            // Choose whether trails use combined slip ratio or individual slips (component-owned setting)
            bool useSlideSpeedForTrails = useCombinedSlipForTrails;

            // Minimal slide metrics sourced directly from PhysX
            float lateral = sideAbs;
            float longitudinal = fwdAbs;
            float slideCombined = Mathf.Max(lateral, longitudinal);

            bool trailStart;
            bool trailStop;
            if (useSlideSpeedForTrails)
            {
                trailStart = wheel.isGrounded && (slideCombined >= trailSlideStart_ratio);
                trailStop  = (!wheel.isGrounded) || (slideCombined <= trailSlideStop_ratio);
            }
            else
            {
                trailStart = wheel.isGrounded && (sideAbs >= trailSideStart || fwdAbs >= trailFwdStart);
                trailStop  = (!wheel.isGrounded) || (sideAbs <= trailSideStop && fwdAbs <= trailFwdStop);
            }

            if (!trailing && trailStart)
            {
                trailing = true;
                NewTrail();
            }
            else if (trailing && trailStop)
            {
                trailing = false;
                EndTrail();
            }

            if (screecher != null)
            {
                // Pass PhysX slip ratios: slideCombined=max(|sidewaysSlip|,|forwardSlip|), lateral=|sidewaysSlip|, longitudinal=|forwardSlip|.
                screecher.ReportWheelSlide(wheel, slideCombined, lateral, longitudinal, wheel.isGrounded);
            }

            //Don't update if there are no trails
            if (trails.Count == 0) return;

            //Essentially a foreach loop, allowing trails to be removed from the list if they are finished
            LinkedListNode<Trail> t = trails.First;
            LinkedListNode<Trail> n;
            do
            {
                n = t.Next;
                t.Value.Update();
                if (t.Value.Dead)
                    trails.Remove(t);
                t = n;
            } while (n != null);
        }

        /// <summary>
        /// Creates a new trail.
        /// </summary>
        public void NewTrail()
        {
            //Stops emitting the last trail and passes the parameters onto a new one
            EndTrail();
            trails.AddLast(new Trail(parent, material, decayTime, roughness, softSourceEnd, offset, width));
        }

        /// <summary>
        /// Deactivate the last trail if it was already active.
        /// </summary>
        public void EndTrail()
        {
            if (!Active) return;
            trails.Last.Value.Finish();
        }

        /// <summary>
        /// Apply a TireTrailConfig at runtime. Pass null to ignore.
        /// </summary>
        public void OverrideWithConfig(TireTrailConfig cfg)
        {
            trailConfig = cfg;
            ApplyConfig(trailConfig);
        }
    }
}