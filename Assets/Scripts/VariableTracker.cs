using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VariableTracker : MonoBehaviour
{
    [Serializable]
    public class TrackedValue
    {
        public string name;
        public Func<float> getValue; // Delegate to get the current value
        public List<(float time, float value)> records = new List<(float, float)>();
    }

    public float interval = 1f; // Time between recordings
    public List<TrackedValue> trackedValues = new List<TrackedValue>();

    private bool tracking = false;

    public void StartTracking()
    {
        if (!tracking)
        {
            StartCoroutine(TrackRoutine());
            tracking = true;
        }
    }

    public void StopAndPrint()
    {
        tracking = false;
        StopAllCoroutines();
        PrintResults();
    }

    private IEnumerator TrackRoutine()
    {
        while (true)
        {
            float time = Time.time;
            foreach (var tracked in trackedValues)
            {
                tracked.records.Add((time, tracked.getValue()));
            }
            yield return new WaitForSeconds(interval);
        }
    }

    private void PrintResults()
    {
        foreach (var tracked in trackedValues)
        {
            Debug.Log($"Variable: {tracked.name}");
            foreach (var (time, value) in tracked.records)
            {
                Debug.Log($"  Time: {time:F2}s, Value: {value}");
            }
        }
    }
}

/* How to use this script:

public class SomeSimulation : MonoBehaviour
{
    public VariableTracker tracker;
    private float speed;
    private float temperature;

    void Start()
    {
        tracker.trackedValues.Add(new VariableTracker.TrackedValue {
            name = "Speed",
            getValue = () => speed
        });

        tracker.trackedValues.Add(new VariableTracker.TrackedValue {
            name = "Temperature",
            getValue = () => temperature
        });

        tracker.interval = 2f; // record every 2 seconds
        tracker.StartTracking();
    }

    void Update()
    {
        // simulate changing variables
        speed = Mathf.Sin(Time.time) * 10f;
        temperature = Mathf.Cos(Time.time) * 5f;

        if (Time.time > 20f)
        {
            tracker.StopAndPrint();
            enabled = false;
        }
    }
}

 */
