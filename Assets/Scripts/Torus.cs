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

    // Further reduced friction values for faster initial movement
    public float staticFrictionThreshold = 3f;          // Lower threshold to break free more easily
    public float baseKineticFriction = 5f;              // Reduced kinetic friction
    public float maxVelocity = 2f;                      // Increased max velocity

    // New properties for smoother movement
    public float massSimulation = 25f;                  // Adjusted mass for more responsive movement
    public float dampingFactor = 0.95f;                 // Less damping for smoother acceleration
    public float collisionBounciness = 0.3f;            // Reduced bounciness for smoother wall navigation

    // New properties to track colony entry
    private bool insideColony = false;
    private float colonyEntryTime = 0f;
    private float colonyProcessTime = 3f;               // Time it takes for colony to process the torus

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
        // Check if we're inside the colony
        CheckColonyEntry();

        if (insideColony)
        {
            // If we're being processed by the colony
            ProcessInColony();
            return;
        }

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
        bool isMoving = velocityMagnitude > 0.01f; // Lower threshold to consider it moving

        // Check if we're stuck against an obstacle
        if (Vector2.Distance(currentPosition, lastPosition) < 0.005f && forceMagnitude > 0.5f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > 0.5f) // Reduced time to detect stuck state
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
            // Apply significantly reduced static friction for easier initial movement
            float effectiveStaticThreshold = staticFrictionThreshold;

            // Further reduce static friction if we have lifters
            if (lifter > 0)
            {
                effectiveStaticThreshold *= (1.0f - Mathf.Min(lifter * 0.2f, 0.8f));
            }

            if (forceMagnitude < effectiveStaticThreshold)
            {
                // Not enough force to overcome static friction
                // But allow some very small movement to avoid total sticking
                currentForce *= 0.1f;
            }
            else
            {
                // Reduce force by static friction but allow movement
                float reduction = Mathf.Min(effectiveStaticThreshold * 0.8f, forceMagnitude * 0.4f);
                currentForce -= currentForce.normalized * reduction;
            }
        }
        else
        {
            // Already moving - apply kinetic friction
            // Reduce friction based on number of lifters (lifting behavior)
            float liftingEffect = Mathf.Min(lifter * 0.25f, 0.85f); // Better lifting effect
            float effectiveKineticFriction = baseKineticFriction * (1.0f - liftingEffect);

            // Apply kinetic friction in opposite direction of movement
            currentForce -= currentVelocity.normalized * effectiveKineticFriction;
        }

        // Boost force if we have coordination (pullers in alignment)
        if (puller > 0 && forceMagnitude > 0.01f)
        {
            // Calculate coordination factor based on dot product of forces
            float coordinationFactor = CalculateCoordinationFactor();

            // Increased coordination bonus - more pullers working together are more effective
            float coordinationBonus = 1.0f + (puller * 0.2f * coordinationFactor);
            currentForce *= coordinationBonus;
        }

        // Apply force with mass simulation for more realistic movement
        currentVelocity += currentForce * Time.deltaTime / massSimulation;

        // Apply damping for smoother movement
        currentVelocity *= dampingFactor;

        // If stuck, apply a slightly larger random force to break free
        if (isStuck)
        {
            currentVelocity += (Vector2)Random.insideUnitCircle * 0.8f;
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

        // Use multiple raycasts for better collision detection
        for (int i = 0; i < 4; i++)
        {
            // Cast in different directions around the circle
            float angle = i * Mathf.PI / 2;
            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 0.8f;
            Vector2 origin = currentPosition + offset;

            RaycastHit2D hit = Physics2D.Raycast(
                origin,                    // Origin point with offset
                currentVelocity.normalized, // Direction of movement
                currentVelocity.magnitude * Time.deltaTime + 0.05f, // Distance to check
                collisionMask              // Layer mask for collisions
            );

            if (hit)
            {
                // Reflect velocity off the collision surface with reduced energy
                currentVelocity = Vector2.Reflect(currentVelocity, hit.normal) * collisionBounciness;

                // Add a slight perpendicular component to help navigate around obstacles
                Vector2 perpendicular = new Vector2(-hit.normal.y, hit.normal.x);
                currentVelocity += perpendicular * currentVelocity.magnitude * 0.3f;

                // Adjust position to prevent overlapping
                currentPosition = hit.point - hit.normal * (radius * 1.05f);

                // Apply small random variation to prevent getting stuck
                currentVelocity += (Vector2)Random.insideUnitCircle * 0.1f;

                // Break out of the loop after handling first collision
                break;
            }
        }
    }

    void CheckColonyEntry()
    {
        // Check if we're inside a colony
        Collider2D homeCollider = Physics2D.OverlapCircle(currentPosition, radius * 0.5f, homeMask);

        if (homeCollider && !insideColony)
        {
            insideColony = true;
            colonyEntryTime = Time.time;

            // Gradually slow down when entering colony
            currentVelocity *= 0.5f;
        }
    }

    void ProcessInColony()
    {
        // Gradually slow down and stop
        currentVelocity *= 0.9f;
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;

        // Process the torus after some time in the colony
        if (Time.time > colonyEntryTime + colonyProcessTime)
        {
            // Change layer to stop being a food item
            gameObject.layer = 0; // Default layer

            // Notify any attached ants that the torus is processed
            NotifyAntsToDetach();

            // Consider destroying the torus or recycling it
            gameObject.SetActive(false);
            Destroy(gameObject, 1f);
        }
    }

    void NotifyAntsToDetach()
    {
        // Find all ants that might be working on this torus
        Ant[] allAnts = FindObjectsOfType<Ant>();
        foreach (Ant ant in allAnts)
        {
            // Send message to detach from this torus
            ant.SendMessage("DetachFromTorus", this, SendMessageOptions.DontRequireReceiver);
        }
    }
}


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
 */