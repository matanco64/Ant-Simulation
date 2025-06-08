using System.Collections;
using UnityEngine;
using System;

public class SimulationSpeedController : MonoBehaviour
{
    // Start is called before the first frame update
    public float initialTimeScale = 40f;

    public float stopAfterSeconds = 0f; // 0 means don't stop automatically

    void Start()
    {

        string[] args = Environment.GetCommandLineArgs();

        foreach (string arg in args) {
            if (arg.StartsWith("-simulationSpeed")) {
                string[] parts = arg.Split(' ');
                if (parts.Length > 1) float.TryParse(parts[1], out initialTimeScale);
            } else if (arg.StartsWith("-stopAfterSeconds")) {
                string[] parts = arg.Split(' ');
                if (parts.Length > 1) float.TryParse(parts[1], out stopAfterSeconds);
            }
        }

        Time.timeScale = initialTimeScale;
        // Time.fixedDeltaTime = 0.001f / initialTimeScale;
        Time.fixedDeltaTime = 0.02f;

        if (stopAfterSeconds > 0f)
        {
            StartCoroutine(StopAfterTime());
        }
    }

    private IEnumerator StopAfterTime()
    {
        yield return new WaitForSeconds(stopAfterSeconds);
        SimulationManager.instance.endSimulation(false);

    }
}
