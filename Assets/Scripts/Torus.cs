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
}