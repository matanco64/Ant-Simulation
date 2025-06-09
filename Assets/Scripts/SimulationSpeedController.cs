using System.Collections;
using UnityEngine;
using System;

public class SimulationSpeedController : MonoBehaviour
{
    // Start is called before the first frame update
    LoadedParameters loadedParameters;
    void Start()
    {

        loadedParameters = new LoadedParameters();
        Time.timeScale = loadedParameters.simulationSpeed;
        // Time.fixedDeltaTime = 0.001f / initialTimeScale;
        Time.fixedDeltaTime = 0.02f;

        if (loadedParameters.stopAfterSeconds > 0f)
        {
            StartCoroutine(StopAfterTime());
        }
    }

    private IEnumerator StopAfterTime()
    {
        yield return new WaitForSeconds(loadedParameters.stopAfterSeconds);
        SimulationManager.instance.endSimulation(false);

    }
}
