using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Torus : MonoBehaviour
{
    public AntSettings settings;
    public Transform center;
    public float radius = 1f;
    public LayerMask homeMask;
    public LayerMask collisionMask;

    public Vector2 totForce;
    Vector2 currentForce;
    Vector2 currentVelocity;
    public Vector2 currentPosition;

    List<Vector2> forcesFromAnts;

    // Reduced static friction to allow easier initial movement
    public float staticFrictionThreshold = 5f;          // Force needed to "break free"
    public float baseKineticFriction = 8f;              // Reduced kinetic friction
    public float maxVelocity = 1.5f;                    // Lowered max velocity for smoother movement

    // New properties for smoother movement
    public float massSimulation = 30f;                  // Higher mass makes movement more stable
    public float dampingFactor = 0.92f;                 // Stronger damping for smoother deceleration
    public float collisionBounciness = 0.4f;            // Reduced bounciness on collision

    // Timer to track stuck state
    private float stuckTimer = 0f;
    private Vector2 lastPosition;
    private bool isStuck = false;

    // Start is called before the first frame update
    void Start()
    {
        currentPosition = transform.position;
        lastPosition = currentPosition;
        currentForce = Vector2.zero;
        currentVelocity = Vector2.zero;
        forcesFromAnts = new List<Vector2>();
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate total force from all ants
        Vector2 currentForce = Vector2.zero;
        foreach (var force in forcesFromAnts)
            currentForce += force;

        totForce = currentForce; // Store total force for reference by ants
        forcesFromAnts.Clear();

        // Apply cooperative behaviors based on ant roles
        int informed = Ant.nOfStates["informed"];
        int puller = Ant.nOfStates["puller"];
        int lifter = Ant.nOfStates["lifter"];

        // Calculate force magnitude and check if we're already moving
        float forceMagnitude = currentForce.magnitude;
        float velocityMagnitude = currentVelocity.magnitude;
        bool isMoving = velocityMagnitude > 0.001f;

        // Check if we're stuck against an obstacle
        if (Vector2.Distance(currentPosition, lastPosition) < 0.01f && velocityMagnitude > 0.1f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > 1.5f)
            {
                isStuck = true;
            }
        }
        else
        {
            stuckTimer = 0f;
            isStuck = false;
        }

        // Apply friction model based on motion state
        if (!isMoving)
        {
            // Apply reduced static friction for easier initial movement
            float effectiveStaticThreshold = staticFrictionThreshold;

            // Reduce static friction if we have lifters
            if (lifter > 0)
            {
                effectiveStaticThreshold *= (1.0f - Mathf.Min(lifter * 0.15f, 0.7f));
            }

            if (forceMagnitude < effectiveStaticThreshold)
            {
                // Not enough force to overcome static friction
                currentForce = Vector2.zero;
                currentVelocity = Vector2.zero;
            }
            else
            {
                // Reduce force by static friction but allow movement
                float reduction = Mathf.Min(effectiveStaticThreshold, forceMagnitude * 0.5f);
                currentForce -= currentForce.normalized * reduction;
            }
        }
        else
        {
            // Already moving - apply kinetic friction
            // Reduce friction based on number of lifters (lifting behavior)
            float liftingEffect = Mathf.Min(lifter * 0.2f, 0.8f); // Cap at 80% reduction
            float effectiveKineticFriction = baseKineticFriction * (1.0f - liftingEffect);

            // Apply kinetic friction in opposite direction of movement
            currentForce -= currentVelocity.normalized * effectiveKineticFriction;
        }

        // Boost force if we have coordination (pullers in alignment)
        if (puller > 1 && forceMagnitude > 0.01f)
        {
            // Calculate coordination factor based on dot product of forces
            float coordinationFactor = CalculateCoordinationFactor();

            // Coordination bonus - more pullers working together are more effective
            float coordinationBonus = 1.0f + (puller * 0.15f * coordinationFactor);
            currentForce *= coordinationBonus;
        }

        // Apply force with mass simulation for more realistic movement
        currentVelocity += currentForce * Time.deltaTime / massSimulation;

        // Apply stronger damping for smoother movement
        currentVelocity *= dampingFactor;

        // If stuck, apply a small random force to try to break free
        if (isStuck)
        {
            currentVelocity += (Vector2)Random.insideUnitCircle * 0.5f;
            stuckTimer = 0;
            isStuck = false;
        }

        // Clamp maximum velocity 
        currentVelocity = Vector2.ClampMagnitude(currentVelocity, maxVelocity);

        // Handle collisions with environment with improved collision response
        HandleCollisions();

        // Update position based on velocity
        lastPosition = currentPosition;
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;
    }

    public void ApplyForce(Vector2 force)
    {
        // Add ant's force to the collection (will be processed in Update)
        forcesFromAnts.Add(force);
    }

    // Calculate how well the ants are coordinating their forces
    private float CalculateCoordinationFactor()
    {
        if (forcesFromAnts.Count <= 1)
            return 1.0f;

        // Calculate average direction
        Vector2 avgDirection = Vector2.zero;
        foreach (var force in forcesFromAnts)
        {
            if (force.magnitude > 0.01f)
                avgDirection += force.normalized;
        }

        if (avgDirection.magnitude < 0.01f)
            return 0.5f;

        avgDirection.Normalize();

        // Calculate how aligned the forces are with the average direction
        float alignmentSum = 0;
        int alignmentCount = 0;

        foreach (var force in forcesFromAnts)
        {
            if (force.magnitude > 0.01f)
            {
                alignmentSum += Vector2.Dot(force.normalized, avgDirection);
                alignmentCount++;
            }
        }

        // Return average alignment (higher is better coordination)
        return alignmentCount > 0 ? Mathf.Clamp01((alignmentSum / alignmentCount + 1) * 0.5f) : 0.5f;
    }

    void HandleCollisions()
    {
        if (currentVelocity.magnitude < 0.01f)
            return; // Skip collision detection for negligible movement

        // Cast in the direction of movement to detect collisions
        RaycastHit2D hit = Physics2D.CircleCast(
            currentPosition,            // Origin point
            radius * 0.9f,              // Slightly smaller radius for better collision detection
            currentVelocity.normalized, // Direction of movement
            currentVelocity.magnitude * Time.deltaTime + 0.05f, // Distance to check + small buffer
            collisionMask               // Layer mask for collisions
        );

        if (hit)
        {
            // Reflect velocity off the collision surface with reduced energy
            currentVelocity = Vector2.Reflect(currentVelocity, hit.normal) * collisionBounciness;

            // Add a slight perpendicular component to help navigate around obstacles
            Vector2 perpendicular = new Vector2(-hit.normal.y, hit.normal.x);
            currentVelocity += perpendicular * currentVelocity.magnitude * 0.2f;

            // Adjust position to prevent overlapping with a bit more clearance
            currentPosition = hit.point - currentVelocity.normalized * (radius * 1.1f);

            // Apply small random variation to prevent getting stuck
            currentVelocity += (Vector2)Random.insideUnitCircle * 0.05f;
        }
    }
}

/////////////////////////////////
/* // TORUS CLASS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Torus : MonoBehaviour
{
    public AntSettings settings;
    public Transform center;
    public float radius = 1f;
    public LayerMask homeMask;
    public LayerMask collisionMask;

    public Vector2 totForce;
    Vector2 currentForce;
    Vector2 currentVelocity;
    public Vector2 currentPosition;

    List<Vector2> forcesFromAnts;

    public float staticFrictionThreshold = 20f;          // Force needed to "break free"
    public float baseKineticFriction = 15f;
    public float maxVelocity = 2.0f;                     // Maximum velocity to prevent extreme acceleration

    // Start is called before the first frame update
    void Start()
    {
        currentPosition = transform.position;
        currentForce = Vector2.zero;
        currentVelocity = Vector2.zero;
        forcesFromAnts = new List<Vector2>();
    }

    // Update is called once per frame
    void Update()
    {
        // Calculate total force from all ants
        Vector2 currentForce = Vector2.zero;
        foreach (var force in forcesFromAnts)
            currentForce += force;

        totForce = currentForce; // Store total force for reference by ants
        forcesFromAnts.Clear();

        // Apply cooperative behaviors based on ant roles
        int informed = Ant.nOfStates["informed"];
        int puller = Ant.nOfStates["puller"];
        int lifter = Ant.nOfStates["lifter"];

        // Calculate force magnitude and check if we're already moving
        float forceMagnitude = currentForce.magnitude;
        float velocityMagnitude = currentVelocity.magnitude;
        bool isMoving = velocityMagnitude > 0.001f;

        // Apply friction model based on motion state
        if (!isMoving)
        {
            // Apply static friction model (need to overcome threshold)
            if (forceMagnitude < staticFrictionThreshold)
            {
                // Not enough force to overcome static friction
                currentForce = Vector2.zero;
                currentVelocity = Vector2.zero;
            }
            else
            {
                // Reduce force by static friction but allow movement
                float reduction = Mathf.Min(staticFrictionThreshold, forceMagnitude * 0.8f);
                currentForce -= currentForce.normalized * reduction;
            }
        }
        else
        {
            // Already moving - apply kinetic friction
            // Reduce friction based on number of lifters (lifting behavior)
            float liftingEffect = Mathf.Min(lifter * 0.2f, 0.8f); // Cap at 80% reduction
            float effectiveKineticFriction = baseKineticFriction * (1.0f - liftingEffect);

            // Apply kinetic friction in opposite direction of movement
            currentForce -= currentVelocity.normalized * effectiveKineticFriction;
        }

        // Boost force if we have coordination (pullers in alignment)
            // Boost force if we have coordination (pullers in alignment)
            if (puller > 1 && forceMagnitude > 0.01f)
            {
                // Coordination bonus - more pullers working together are more effective
                float coordinationBonus = 1.0f + (puller * 0.1f); // Each puller adds 10% more effectiveness
                currentForce *= coordinationBonus;
            }

        // Apply force and update velocity
        currentVelocity += currentForce * Time.deltaTime;

        // Apply damping to prevent excessive oscillation
        currentVelocity *= 0.98f;

        // Clamp maximum velocity 
        currentVelocity = Vector2.ClampMagnitude(currentVelocity, maxVelocity);

        // Handle collisions with environment
        HandleCollisions();

        // Update position based on velocity
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;
    }

    public void ApplyForce(Vector2 force)
    {
        // Add ant's force to the collection (will be processed in Update)
        forcesFromAnts.Add(force);
    }

    void HandleCollisions()
    {
        if (currentVelocity.magnitude < 0.01f)
            return; // Skip collision detection for negligible movement

        // Cast in the direction of movement to detect collisions
        RaycastHit2D hit = Physics2D.CircleCast(
            currentPosition,            // Origin point
            radius * 0.9f,              // Slightly smaller radius for better collision detection
            currentVelocity.normalized, // Direction of movement
            currentVelocity.magnitude * Time.deltaTime + 0.01f, // Distance to check + small buffer
            collisionMask               // Layer mask for collisions
        );

        if (hit)
        {
            Debug.Log("Torus hit object: " + hit.collider.name);

            // Reflect velocity off the collision surface
            currentVelocity = Vector2.Reflect(currentVelocity, hit.normal) * 0.8f; // 20% energy loss on collision

            // Adjust position to prevent overlapping
            currentPosition = hit.point - currentVelocity.normalized * radius;

            // Apply small random variation to prevent getting stuck
            currentVelocity += (Vector2)Random.insideUnitSphere * 0.1f;
        }
    }
} */




///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
/* using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Torus : MonoBehaviour
{
    public AntSettings settings;
    public Transform center;
    public float radius = 1f;
    public LayerMask homeMask;
    public LayerMask collisionMask;

    public Vector2 totForce;
    Vector2 currentForce;
    Vector2 currentVelocity;
    public Vector2 currentPosition;

    List<Vector2> forcesFromAnts;

    public float staticFrictionThreshold = 20f;          // Force needed to "break free"
    public float baseKineticFriction = 15f;


    // Start is called before the first frame update
    void Start()
    {
        currentPosition = transform.position;
        currentForce = Vector2.zero;
        currentVelocity = Vector2.zero;
        forcesFromAnts = new List<Vector2>();
    }


    // Update is called once per frame
    void Update()
    {
        Vector2 currentForce = Vector2.zero;
        foreach (var force in forcesFromAnts)
            currentForce += force;
        forcesFromAnts.Clear();

        int informed = Ant.nOfStates["informed"];
        int puller = Ant.nOfStates["puller"];
        int lifter = Ant.nOfStates["lifter"];

        // Puller help logic
        if (informed > 0 && puller > 0)
        {
            float assistFactor = (float)puller / (informed + puller);
            currentForce *= (1 + assistFactor);
        }

        float forceMagnitude = currentForce.magnitude;
        float velocityMagnitude = currentVelocity.magnitude;

        bool isMoving = velocityMagnitude > 0.001f;

        if (!isMoving)
        {
            if (forceMagnitude < staticFrictionThreshold)
            {
                currentForce = Vector2.zero;
                currentVelocity = Vector2.zero;
            }
            else
            {
                currentForce -= currentForce.normalized * staticFrictionThreshold;
            }
        }
        else
        {
            float effectiveKineticFriction = Mathf.Max(baseKineticFriction - lifter * 0.05f, 0f);
            currentForce -= currentVelocity.normalized * effectiveKineticFriction;
        }

        // Apply force and update
        currentVelocity += currentForce * Time.deltaTime;
        currentVelocity *= 0.98f; // damping
        HandleCollisions();
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;

        totForce = currentForce.normalized;
    }


    public void ApplyForce(Vector2 force)
    {
        Debug.Log("Applying force: " + force);
        forcesFromAnts.Add(force);
    }

    void HandleCollisions()
{
    // if(currentVelocity == Vector2.zero)
    //     return; // No movement, no collision check
    // Perform a circle cast instead of a raycast to account for the Torus's radius
    // RaycastHit2D hit =  Physics2D.Raycast(
    //     currentPosition,                     // Circle's origin
    //     radius,           // Radius of the Torus
    //     currentVelocity.normalized,         // Direction of movement
    //     currentVelocity.magnitude * Time.deltaTime, // Distance to check
    //     collisionMask                       // Layer mask for collisions
    // );
    float moveDst = currentVelocity.magnitude * Time.deltaTime;

	RaycastHit2D hit = Physics2D.Raycast(currentPosition, currentForce.normalized, Mathf.Max(radius, moveDst), collisionMask);

    if (hit)
    {   
        Debug.Log("hit something");
        // Reflect velocity on collision
        currentVelocity = Vector2.Reflect(currentVelocity, hit.normal);

        // Adjust position to prevent overlap
        currentPosition = hit.point - currentVelocity.normalized * radius;
    }
}
} */