using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using TMPro;
using GameAI;

public class GameManager : Singleton<GameManager>
{
    protected GameManager() { }

    public TextMeshProUGUI StudentNameTMP;
    public TextMeshProUGUI KPH_TMP;
    public TextMeshProUGUI MPH_TMP;
    public TextMeshProUGUI KPH_LTA_TMP;
    public TextMeshProUGUI TotalMetersTMP;
    public TextMeshProUGUI ElapsedTMP;
    public TextMeshProUGUI WipeoutsTMP;

    public int Wipeouts = 0;
    public float KpHLTA = 0f;
    public float MetersTravelled = 0f;
    public float MinThrottle = float.MaxValue;
    public float MaxThrottle = 0f;

    public enum SimulationMode
    {
        FPS_60_1X_RealTime,
        FPS_60_1X_SimTime,
    };

    [SerializeField]
    protected SimulationMode simulationMode = SimulationMode.FPS_60_1X_RealTime;

    public static SimulationMode? INTERNAL_overrideSimulationMode = null;


    [SerializeField] private AIVehicle racecar = null;

    private bool carAssigned = false;

    private bool healthCheckEnabled = true;
    private int healthDelayCount = 0;
    private const int MAX_HEALTH_DELAY_COUNT = 20;

    public void AssignRacecar(AIVehicle car)
    {
        if (carAssigned)
        {
            throw new UnityException("Cannot assign car. Already assigned.");
        }

        carAssigned = true;
        this.racecar = car;
        this.prevUpdateTicks = this.racecar.UpdateTicks;
        healthCheckEnabled = false;
        healthDelayCount = 0;
    }

    private void Awake()
    {

        if (racecar == null)
            Debug.LogError("No racecar assigned");

        // Decide audio here (before any Start()) so sound components can disable themselves on Start
        var effectiveMode = INTERNAL_overrideSimulationMode ?? simulationMode;
        if (RaceLaunchOptions.Unattended)
            effectiveMode = SimulationMode.FPS_60_1X_SimTime;
        SimulationAudio.SetMuted(SimulationAudio.ShouldMute(effectiveMode == SimulationMode.FPS_60_1X_SimTime));

    }

    // Start is called before the first frame update
    void Start()
    {
        //QualitySettings.vSyncCount = 0;
        //Application.targetFrameRate = 60;

        if (INTERNAL_overrideSimulationMode != null)
            simulationMode = (SimulationMode)INTERNAL_overrideSimulationMode;
        else if (RaceLaunchOptions.TrainerConnected || RaceLaunchOptions.Unattended)
        {
            // NN training or command-line recording: simulated time (the trainer sets time scale / frame rates), no audio
            simulationMode = SimulationMode.FPS_60_1X_SimTime;
            SimulationAudio.SetMuted(true);
        }


        Debug.Log($"SimulationMode: {simulationMode}");


        switch (simulationMode)
        {
            case SimulationMode.FPS_60_1X_RealTime:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                Time.captureFramerate = 0;   // in case simulated time was used earlier in this session
                break;
            case SimulationMode.FPS_60_1X_SimTime:
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                Time.captureFramerate = 60;
                break;
        }

        StartCoroutine(LateFixedUpdateLoop());

    }

    // Update is called once per frame
    void Update()
    {

        if (Input.GetKeyUp(KeyCode.Escape))
        {
            Application.Quit();
        }

    }

    private bool cheatDetected = false;

    private long prevUpdateTicks = 0L;
    private long prevFixedUpdateTicks = 0L;

    void LateUpdate()
    {
        if (racecar != null)
        {
        
            if (!cheatDetected && racecar.HardCodedValueUsed)
            {
                cheatDetected = true;
                throw new UnityException("Hard coded throttle/steering use detection. Not allowed in submissions for grade.");
            }

            if (!healthCheckEnabled && healthDelayCount >= MAX_HEALTH_DELAY_COUNT)
            {
                healthCheckEnabled = true;
                return;
            }
            else
            {
                ++healthDelayCount;
                return;
            }

            long u = racecar.UpdateTicks;

            if (u <= prevUpdateTicks)
            {
                prevUpdateTicks = u;
                throw new UnityException($"Racecar failed to Update(): {u} < {prevUpdateTicks} :: {healthDelayCount}");
            }

            prevUpdateTicks = u;

        }
    }


    private IEnumerator LateFixedUpdateLoop()
    {
        while (true)
        {
            // Wait until after FixedUpdate and physics resolution for this step
            yield return new WaitForFixedUpdate();

            if (racecar == null)
            {
                continue;
            }

            if (!healthCheckEnabled)
            {
                continue;
            }

            long f = racecar.FixedUpdateTicks;

            if (f <= prevFixedUpdateTicks)
            {
                prevFixedUpdateTicks = f;

                throw new UnityException($"Racecar failed to FixedUpdate()! {f} < {prevFixedUpdateTicks}");
            }

            prevFixedUpdateTicks = f;
            
        }
    }
}
