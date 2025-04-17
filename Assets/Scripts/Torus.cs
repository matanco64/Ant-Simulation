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

    Vector2 currentForce;
    Vector2 currentVelocity;
    public Vector2 currentPosition;

    List<Vector2> forcesFromAnts;


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
        // Calculate total force from ants
        //Debug.Log("Torus: " + currentPosition + "Transform: " + transform.position);
        Vector2 currentForce = Vector2.zero;
        foreach (var force in forcesFromAnts)
        {
            currentForce += force;
        }
        forcesFromAnts.Clear();

        // Apply force to velocity
        currentVelocity += currentForce * Time.deltaTime;

        // Handle collisions
        HandleCollisions();

        // Update position
        currentPosition += currentVelocity.normalized  * Time.deltaTime;
        transform.position = currentPosition;
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