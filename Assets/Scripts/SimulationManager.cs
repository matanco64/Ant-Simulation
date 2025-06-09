using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SimRunData
{
    public string runId;
    public float duration;
    public bool didSucceed;
    public float timeScale;
    public int antCount;
    public List<Vector2> torusPositions;

    public Vector2 ant_colony_position;
}


public class SimulationManager : MonoBehaviour
{
    public static SimulationManager instance;
    SimRunData runData;


    void Awake()
    {
        // Singleton pattern
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        runData = new SimRunData {
            runId = System.Guid.NewGuid().ToString(),
            duration = 0f,
            didSucceed = false,
            timeScale = Time.timeScale,
            antCount = 0,
            torusPositions = new List<Vector2>()
            
        };
    }


    public void UpdateAntCount(int count)
    {
        runData.antCount = count;
    }

    public void AddTorusDataPoint(Vector2 position)
    {
        runData.torusPositions.Add(position);
    }
    public void AddColonyPosition(Vector2 position)
    {
        runData.ant_colony_position = position;
    }

    public void endSimulation(bool didSucceed)
    {

        runData.didSucceed = didSucceed;
        runData.duration = Time.timeSinceLevelLoad;
        SaveRunData(runData);
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    public void SaveRunData(SimRunData runData)
    {
        string json = JsonUtility.ToJson(runData, true);
        System.IO.File.WriteAllText("sim_output.json", json);
    }
}