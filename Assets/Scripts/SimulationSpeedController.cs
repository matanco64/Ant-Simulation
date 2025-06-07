using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimulationSpeedController : MonoBehaviour
{
    // Start is called before the first frame update
    public float initialTimeScale = 40f;

    void Start()
    {
        Time.timeScale = initialTimeScale;
        // Time.fixedDeltaTime = 0.001f / initialTimeScale;
        Time.fixedDeltaTime = 0.02f;

    }
}
