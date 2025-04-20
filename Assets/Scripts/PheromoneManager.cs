/* using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Class to manage pheromone data and diffusion
public class PheromoneManager
{
    private PerceptionMap map;
    private float decayRate;
    private float diffusionRate;
    private float updateInterval;
    private float timeSinceLastUpdate;

    // Constructor
    public PheromoneManager(Vector2 center, float size, float decayRate, float diffusionRate = 0.1f, float updateInterval = 0.5f)
    {
        // Create a perception map to store pheromone values
        this.map = new PerceptionMap(center, size, 0.5f);  // 0.5f is the cell size
        this.decayRate = decayRate;
        this.diffusionRate = diffusionRate;
        this.updateInterval = updateInterval;
        this.timeSinceLastUpdate = 0f;
    }

    // Get the underlying perception map
    public PerceptionMap GetMap()
    {
        return map;
    }

    // Update the pheromone map - handle diffusion and decay
    public void Update(float deltaTime)
    {
        timeSinceLastUpdate += deltaTime;

        // Only update at specific intervals to save performance
        if (timeSinceLastUpdate >= updateInterval)
        {
            // Decay pheromones over time
            map.Decay(decayRate * timeSinceLastUpdate);
            
            // Diffuse pheromones to neighboring cells
            if (diffusionRate > 0)
            {
                map.Diffuse(diffusionRate);
            }
            
            timeSinceLastUpdate = 0;
        }
    }

    // Add a pheromone at a specific position
    public void AddPheromone(Vector2 position, float value)
    {
        map.Add(position, value);
    }

    // Get the pheromone value at a specific position
    public float GetPheromoneValue(Vector2 position)
    {
        return map.Sample(position);
    }

    // Reset the pheromone map
    public void Reset()
    {
        map.Reset();
        timeSinceLastUpdate = 0f;
    }
}

// Class to handle spatial data storage and queries
public class PerceptionMap
{
    // Structure to hold each entry for proximity queries
    public struct Entry
    {
        public Vector2 pos;
        public float value;
    }

    private Vector2 center;
    private float size;
    private float cellSize;
    private int resolution;
    private float[,] values;

    // Constructor
    public PerceptionMap(Vector2 center, float size, float cellSize)
    {
        this.center = center;
        this.size = size;
        this.cellSize = cellSize;
        
        resolution = Mathf.CeilToInt(size / cellSize);
        values = new float[resolution, resolution];
    }

    // Convert world position to grid position
    private Vector2Int WorldToGridPos(Vector2 worldPos)
    {
        Vector2 offset = worldPos - (center - new Vector2(size/2, size/2));
        int x = Mathf.Clamp(Mathf.FloorToInt(offset.x / cellSize), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(offset.y / cellSize), 0, resolution - 1);
        return new Vector2Int(x, y);
    }

    // Convert grid position to world position
    private Vector2 GridToWorldPos(Vector2Int gridPos)
    {
        Vector2 worldOffset = new Vector2(gridPos.x * cellSize, gridPos.y * cellSize) + new Vector2(cellSize/2, cellSize/2);
        return center - new Vector2(size/2, size/2) + worldOffset;
    }

    // Add value to a specific position
    public void Add(Vector2 worldPos, float value)
    {
        Vector2Int gridPos = WorldToGridPos(worldPos);
        values[gridPos.x, gridPos.y] += value;
        
        // Clamp value to prevent overflow
        values[gridPos.x, gridPos.y] = Mathf.Min(values[gridPos.x, gridPos.y], 5.0f);
    }

    // Sample value at a specific position
    public float Sample(Vector2 worldPos)
    {
        Vector2Int gridPos = WorldToGridPos(worldPos);
        return values[gridPos.x, gridPos.y];
    }

    // Decay all values by a factor
    public void Decay(float decayAmount)
    {
        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                values[x, y] = Mathf.Max(0, values[x, y] - decayAmount);
            }
        }
    }

    // Diffuse values to neighboring cells
    public void Diffuse(float diffusionRate)
    {
        float[,] newValues = new float[resolution, resolution];
        
        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                float centerValue = values[x, y];
                newValues[x, y] += centerValue * (1 - diffusionRate);
                
                // Diffuse to neighbors
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    for (int offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        // Skip self
                        if (offsetX == 0 && offsetY == 0) continue;
                        
                        int nx = x + offsetX;
                        int ny = y + offsetY;
                        
                        // Skip out of bounds
                        if (nx < 0 || nx >= resolution || ny < 0 || ny >= resolution) continue;
                        
                        // Add diffused value to neighbor
                        float diffusionAmount = centerValue * diffusionRate / 8f; // 8 neighbors
                        newValues[nx, ny] += diffusionAmount;
                    }
                }
            }
        }
        
        // Update values
        values = newValues;
    }

    // Reset all values to zero
    public void Reset()
    {
        values = new float[resolution, resolution];
    }

    // Find non-zero values within a radius
    public int GatherNearbyNonZero(Vector2 worldPos, float radius, Entry[] results)
    {
        int count = 0;
        float radiusSqr = radius * radius;
        
        // Calculate the grid cells that need to be checked
        int cellRadius = Mathf.CeilToInt(radius / cellSize);
        Vector2Int centerGridPos = WorldToGridPos(worldPos);
        
        for (int offsetX = -cellRadius; offsetX <= cellRadius; offsetX++)
        {
            for (int offsetY = -cellRadius; offsetY <= cellRadius; offsetY++)
            {
                int gridX = centerGridPos.x + offsetX;
                int gridY = centerGridPos.y + offsetY;
                
                // Skip out of bounds
                if (gridX < 0 || gridX >= resolution || gridY < 0 || gridY >= resolution) continue;
                
                if (values[gridX, gridY] > 0)
                {
                    Vector2 cellWorldPos = GridToWorldPos(new Vector2Int(gridX, gridY));
                    float distSqr = (cellWorldPos - worldPos).sqrMagnitude;
                    
                    if (distSqr <= radiusSqr)
                    {
                        if (count < results.Length)
                        {
                            results[count] = new Entry
                            {
                                pos = cellWorldPos,
                                value = values[gridX, gridY]
                            };
                            count++;
                        }
                    }
                }
            }
        }
        
        return count;
    }
} */