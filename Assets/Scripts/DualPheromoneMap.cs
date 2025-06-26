using System.Collections.Generic;
using UnityEngine;

public enum PheromoneType { Positive, Negative }

public class DualPheromoneMap : MonoBehaviour
{
    [SerializeField] private float decayTime = 10f;

    private class Pheromone
    {
        public Vector2 position;
        public float strength;
        public float timestamp;
    }

    private List<Pheromone> positiveMarkers = new();
    private List<Pheromone> negativeMarkers = new();

    public void Deposit(Vector2 pos, float amount, PheromoneType type)
    {
        var list = type == PheromoneType.Positive ? positiveMarkers : negativeMarkers;
        list.Add(new Pheromone
        {
            position = pos,
            strength = amount,
            timestamp = Time.time
        });
    }

    public Vector2 GetGradient(Vector2 pos, float radius, PheromoneType type)
    {
        var list = type == PheromoneType.Positive ? positiveMarkers : negativeMarkers;
        Vector2 gradient = Vector2.zero;
        float totalWeight = 0f;

        foreach (var p in list)
        {
            float age = Time.time - p.timestamp;
            if (age > decayTime) continue;

            float dist = Vector2.Distance(pos, p.position);
            if (dist > radius || dist == 0) continue;

            float weight = Mathf.Exp(-dist * 2f) * Mathf.Exp(-age / decayTime);
            Vector2 dir = (p.position - pos).normalized;

            gradient += dir * weight * p.strength;
            totalWeight += weight;
        }

        return totalWeight > 0 ? gradient.normalized : Vector2.zero;
    }
}
