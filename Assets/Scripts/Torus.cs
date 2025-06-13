using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

public class Torus : MonoBehaviour
{
    public AntSettings settings;
    public Transform center;
    public float radius = 1.5f;
    public float distanceToWallGap = 0.4f;
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
    [SerializeField] private float safeDistanceMultiplier = 2f;

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
        // Clear forces for next frame
        forcesFromAnts.Clear();

        HandleCollisions();
        lastPosition = currentPosition;
        currentPosition += currentVelocity * Time.deltaTime;
        transform.position = currentPosition;
        transform.Rotate(0, 0, angularVelocity * Mathf.Rad2Deg * Time.deltaTime);
        SimulationManager.instance.AddTorusDataPoint(currentPosition);

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

        
    }



    // Handle collisions with walls and other obstacles
    void HandleCollisions()
    {
        // Predict next position
        Vector2 nextPosition = currentPosition + currentVelocity * Time.deltaTime;
        Vector2 moveDir = (nextPosition - currentPosition).normalized;
        float moveDist = Vector2.Distance(currentPosition, nextPosition);

        // Handle multiple collisions by iterating up to a max number of attempts
        int maxIterations = 3;
        int iteration = 0;
        bool collided = false;

        while (iteration < maxIterations) //moveDist > 0.0001f)
        {
            RaycastHit2D hit = Physics2D.CircleCast(currentPosition, radius * safeDistanceMultiplier, moveDir, moveDist, LayerMask.GetMask("Wall"));
            if (hit)
            {
                collided = true;

                // Move to the point of contact, minus a small offset to prevent sticking
                currentPosition = hit.point + hit.normal * (radius * safeDistanceMultiplier);
                

                // Project velocity along the wall (slide)
                currentVelocity = Vector2.Reflect(currentVelocity, hit.normal) * collisionBounciness;
                // Optionally, dampen velocity further to prevent jitter
                // currentVelocity *= 0.7f;

                // Prepare for next iteration in case of multiple collisions
                nextPosition = currentPosition + currentVelocity * Time.deltaTime;
                moveDir = (nextPosition - currentPosition).normalized;
                moveDist = Vector2.Distance(currentPosition, nextPosition);
                iteration++;
            }
            else
            {
                // No collision, move to next position
                break;
            }
            currentPosition = nextPosition;
        }

        // If no collision, just move as normal
        if (!collided)
        {
            currentPosition = nextPosition;
        }
    }

    private static void LogWithScreenshot()
    {
        string filename, fullPath;
        filename = $"debug_frame_{Time.frameCount}.png";
        // create a directory if it doesn't exist
        if (!Directory.Exists(Application.dataPath + "\\..\\DebugFrames"))
        {
            Directory.CreateDirectory(Application.dataPath + "\\..\\DebugFrames");
        }
        fullPath = System.IO.Path.Combine(Application.dataPath, "..", "DebugFrames", filename);
        ScreenCapture.CaptureScreenshot(fullPath);
        Debug.Log($"[Screenshot] {filename} saved.\nPath: {fullPath}");
    }


    void CheckColonyEntry()
    {

        // Check if we're inside a colony
        Collider2D homeCollider = Physics2D.OverlapCircle(currentPosition, radius * 1.05f, homeMask);

        if (homeCollider && !insideColony)
        {
            Debug.Log("Entering colony: " + homeCollider.name);
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

            // Notify attached ants to detach and go search for more food
            NotifyAntsToDetach();

            // Start fading out
            StartCoroutine(FadeOutAndDestroy());
        }
    }

    void NotifyAntsToDetach()
    {
        // Reset the global counters for ants working on this torus
        Ant.nOfStates["informed"] = 0;
        Ant.nOfStates["puller"] = 0;
        Ant.nOfStates["lifter"] = 0;

        // Find all ants and notify them to detach if they're working on this torus
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


    IEnumerator FadeOutAndDestroy()
    {
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            Color originalColor = spriteRenderer.color;
            float fadeTime = 1.5f;
            float elapsedTime = 0f;

            while (elapsedTime < fadeTime)
            {
                elapsedTime += Time.deltaTime;
                float alpha = Mathf.Lerp(originalColor.a, 0f, elapsedTime / fadeTime);
                spriteRenderer.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
                yield return null;
            }
        }

        // Deactivate and destroy after fading out
        gameObject.SetActive(false);
        Destroy(gameObject, 0.1f);
        SimulationManager.instance.AddTorusDataPoint(currentPosition);
        SimulationManager.instance.endSimulation(true);
    }
}