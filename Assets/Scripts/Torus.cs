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

    // Physics parameters - calibrated for smoother movement
    public float staticFrictionThreshold = 5f;
    public float baseKineticFriction = 10f;
    public float maxVelocity = 1f;                    // Reduced for slower movement

    // Response coefficients from the model
    public float gamma = 5f;                          // Mass response coefficient from equation (12)
    public float gamma_rot = 10f;                     // Rotational response coefficient from equation (13)
    public float dampingFactor = 0.98f;               // Damping to smooth movement
    public float collisionBounciness = 0.2f;          // Reduced bounciness

    // Colony interaction
    private bool insideColony = false;
    private float colonyEntryTime = 0f;
    private float colonyProcessTime = 3f;

    // Movement tracking
    private float stuckTimer = 0f;
    private Vector2 lastPosition;
    private bool isStuck = false;
    private float angularVelocity = 0f;

    // Direction to home (for debugging)
    [HideInInspector]
    public Vector2 directionToHome;
    // Parameters
    [SerializeField] private float wallPushSpeed = 10f;
    [SerializeField] private float velocityCorrectionStrength = 0.5f;
    [SerializeField] private float safeDistanceMultiplier = 1.01f;

    void Start()
    {
        currentPosition = transform.position;
        lastPosition = currentPosition;
        currentForce = Vector2.zero;
        currentVelocity = Vector2.zero;
        forcesFromAnts = new List<Vector2>();
        directionToHome = Vector2.zero;

        // Find direction to colony
        FindHomeDirection();
    }

    void Update()
    {
        // Update direction to home periodically
        if (Time.frameCount % 30 == 0)
        {
            FindHomeDirection();
        }

        // Check if we're inside the colony
        CheckColonyEntry();

        if (insideColony)
        {
            // If we're being processed by the colony
            ProcessInColony();
            return;
        }

        // Clear accumulated force for this frame
        totForce = Vector2.zero;

        // Sum all forces from ants (equation 9 from model)
        foreach (var force in forcesFromAnts)
        {
            totForce += force;
        }

        // Apply kinetic friction based on lifters (equation 11)
        ApplyFrictionModel();

        // Calculate motion based on model equations (12) and (13)
        CalculateMotionFromModel();

        // Handle collisions with environment

        // Update position and rotation
        lastPosition = currentPosition;
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;
        transform.Rotate(0, 0, angularVelocity * Mathf.Rad2Deg * Time.deltaTime);

        // Clear forces for next frame
        forcesFromAnts.Clear();

        HandleCollisions();





    }
    public void ApplyForce(Vector2 force)
    {
        // Add ant's force to the collection (will be processed in Update)
        forcesFromAnts.Add(force);
    }

    // Find the colony location to establish direction
    void FindHomeDirection()
    {
        Collider2D homeCollider = Physics2D.OverlapCircle(currentPosition, 50f, homeMask);
        if (homeCollider)
        {
            directionToHome = (homeCollider.transform.position - transform.position).normalized;
        }
    }

    // Apply friction model based on number of lifters (equation 11)
    void ApplyFrictionModel()
    {
        // Get number of lifters
        int lifters = Ant.nOfStates["lifter"];

        // Bare friction force (F^0_kin in the paper)
        float F0_kin = baseKineticFriction;

        // Friction reduction factor (β in the paper)
        float beta = 0.5f;

        // Calculate effective friction: f_kin = max{F^0_kin - β*N_lifter, 0} (Equation 11)
        float effectiveFriction = Mathf.Max(F0_kin - beta * lifters, 0);

        // Apply friction in opposite direction of movement if moving
        if (currentVelocity.magnitude > 0.01f)
        {
            totForce -= currentVelocity.normalized * effectiveFriction;
        }
        else if (totForce.magnitude < staticFrictionThreshold)
        {
            // Apply static friction threshold
            totForce = Vector2.zero;
        }
    }

    // Calculate motion according to equations (12) and (13)
    void CalculateMotionFromModel()
    {
        // V_cm = F_cm / γ (Equation 12)
        Vector2 targetVelocity = totForce / gamma;

        // Limit maximum velocity
        targetVelocity = Vector2.ClampMagnitude(targetVelocity, maxVelocity);

        // Smooth velocity changes
        currentVelocity = Vector2.Lerp(currentVelocity, targetVelocity, Time.deltaTime * 2f);

        // Apply damping
        currentVelocity *= dampingFactor;

        // Calculate net torque from all ants
        float netTorque = 0f;

        // Calculate angular velocity using equation (13)
        // ω = (1/γ_rot) * Σ(r^i * sin(φ_i) - f_kin)
        // Currently simplifying by assuming torque contributions are minimal
        angularVelocity = netTorque / gamma_rot;

        // Detect if stuck
        if (Vector2.Distance(currentPosition, lastPosition) < 0.005f && totForce.magnitude > 1f)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer > 0.5f)
            {
                isStuck = true;
                currentVelocity += (Vector2)Random.insideUnitCircle * 0.5f;
                stuckTimer = 0f;
                isStuck = false;
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    void HandleCollisions()
    {
        
        // Update currentPosition manually
        currentPosition += currentVelocity * Time.deltaTime;

        // Cast to detect wall collision
        RaycastHit2D hit = Physics2D.CircleCast(currentPosition, radius, currentVelocity.normalized, currentVelocity.magnitude * Time.deltaTime, LayerMask.GetMask("Wall"));

        if (hit)
        {
            Debug.Log("Collision detected with: " + hit.collider.name);

            Vector2 normal = hit.normal;

            // --- 1. Correct velocity (cancel into-wall motion) ---
            float intoWall = Vector2.Dot(currentVelocity, normal);
            if (intoWall < 0f)
            {
                currentVelocity -= normal * intoWall * velocityCorrectionStrength;
            }

            // --- 2. Correct position (push out gently) ---
            float distanceToWall = Vector2.Dot(currentPosition - hit.point, normal);
            float desiredDistance = radius * safeDistanceMultiplier;
            float penetrationDepth = desiredDistance - distanceToWall;

            if (penetrationDepth > 0f)
            {
                currentPosition += normal * penetrationDepth * wallPushSpeed * Time.deltaTime;
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

            // Gradually slow down
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
            gameObject.layer = 0;

            // Notify attached ants
            NotifyAntsToDetach();

            // Deactivate and destroy
            gameObject.SetActive(false);
            Destroy(gameObject, 1f);
        }
    }

    void NotifyAntsToDetach()
    {
        // Find all ants and notify them to detach
        Ant[] allAnts = FindObjectsOfType<Ant>();
        foreach (Ant ant in allAnts)
        {
            ant.SendMessage("DetachFromTorus", this, SendMessageOptions.DontRequireReceiver);
        }
    }

    // Method to get current angular velocity (needed by ants)
    public float GetAngularVelocity()
    {
        return angularVelocity;
    }
}