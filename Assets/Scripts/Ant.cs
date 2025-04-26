using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Ant : MonoBehaviour
{
	public enum State { SearchingForFood, ReturningHome, Informed, Pulling, Lifting }

	public AntSettings settings;
	public Transform head;
	public LayerMask foodMask;
	public LayerMask homeMask;
	public LayerMask torusMask;
	public LayerMask collisionMask;

	public Transform antennaLeft;
	public Transform antennaRight;
	public Transform perceptionCentre;
	public static Dictionary<string, int> nOfStates = new Dictionary<string, int>
	{
		{ "informed", 0 },
		{ "puller", 0 },
		{ "lifter", 0 }
	};

	public Vector2 pullingForce; // Direction of the pull force

	State currentState;
	Vector2 currentVelocity;
	Vector2 collisionAvoidForce;

	float nextRandomSteerTime;

	Vector2[] sensors = new Vector2[3];
	float[] sensorData = new float[3];

	Transform collectedFood;

	Vector2 lastPheromonePos;
	Collider2D[] foodColliders;
	PerceptionMap.Entry[] pheromoneEntries;
	AntColony colony;
	float nextDirUpdateTime;

	Vector2 randomSteerForce;
	Vector2 pheromoneSteerForce;
	Vector2 torusFollowForce;

	// State
	Vector2 currentForwardDir;
	Vector2 currentPosition;
	float colDst;
	Vector2 obstacleAvoidForce;
	float obstacleForceResetTime;
	bool antennaCollisionLastFrame;
	Vector2 homePos;

	enum Antenna { None, Left, Right }
	Antenna lastAntennaCollision;
	bool foodInSight;
	Transform targetFood;
	float deathTime;
	bool turningAround;
	Vector2 turnAroundForce;
	Vector2 hitRelativeToTorus;
	float turnAroundEndTime;
	Torus targetTorus;

	float leftHomeTime;
	float leftFoodTime;
	float relativeAngleToTorus;
	float torusAttachmentTime;

	// Model parameters from the paper
	[Header("Theoretical Model Parameters")]
	public float Kc = 0.2f;              // Switching rate coefficient (basal decision-making rate)
	public float Find = 1f;              // Alignment sensitivity (individual preference strength)
	public float beta = 0.5f;            // Friction reduction factor due to lifters
	public float gamma = 1.0f;           // Mass response coefficient
	public float gamma_rot = 1.0f;       // Rotational response coefficient
	public float f0 = 1.0f;              // Force magnitude applied by a single puller
	public float phi_max = 60f;          // Maximum tilt angle in degrees (constrains orientation)
										 // Variables for theoretical model
	float tiltAngle;                   // φ in the paper - angle between radial direction and body axis
	float attachmentRate;              // K_att in the paper - rate at which ants attach to cargo
	float detachmentRate;              // K_det in the paper - rate at which ants detach from cargo
	float barefrictionForce;           // F^0_kin in the paper - base friction before lifter reduction

	// Improved model variables
	float effectiveFind;               // Effective alignment sensitivity (adjusted for group size)
	bool isLeader;                     // Whether this ant is a leader/informed ant


	// Critical point parameters
	[Header("Critical Point Parameters")]
	public float Find_c = 0.5f;          // Critical value for the alignment sensitivity


	public void SetColony(AntColony colony)
	{
		this.colony = colony;
	}

	void Start()
	{
		lastPheromonePos = transform.position;
		currentState = State.SearchingForFood;
		transform.eulerAngles = Vector3.forward * Random.value * 360;
		currentForwardDir = transform.right;
		currentPosition = transform.position;
		currentVelocity = currentForwardDir * settings.maxSpeed;

		foodColliders = new Collider2D[1];
		homePos = transform.position;

		const int maxPerceivedPheromones = 1024;
		pheromoneEntries = new PerceptionMap.Entry[maxPerceivedPheromones];
		nextDirUpdateTime = Random.value * settings.timeBetweenDirUpdate;
		colDst = settings.collisionRadius / 2f;
		deathTime = Time.time + settings.lifetime + Random.Range(0, settings.lifetime / 2f);
		leftHomeTime = Time.time;

		// Initialize model parameters
		tiltAngle = 0f;
		barefrictionForce = 0.5f;  // Base friction force
		attachmentRate = 0.4f;     // Base attachment rate
		detachmentRate = 0.6f;     // Base detachment rate
		isLeader = Random.value < 0.05f;  // 5% chance of being a leader ant

		// Small random variation in individual parameters to simulate ant diversity
		Kc += Random.Range(-0.05f, 0.05f);
		Find += Random.Range(-0.1f, 0.1f);
	}

	void Update()
	{
		if (Time.time > deathTime && settings.useDeath)
		{
			Destroy(gameObject);
		}

		HandlePheromonePlacement();
		HandleRandomSteering();

		if (currentState == State.SearchingForFood)
		{
			HandleSearchForFood();
		}
		else if (currentState == State.ReturningHome)
		{
			HandleReturnHome();
		}

		HandleCollisionSteering();

		// Handle different behaviors based on state
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			MoveRelativeToTorus();
			if (targetTorus != null)
			{
				ApplyTheoreticalModelForces();
				ProcessStochasticDynamics();
			}
		}
		else
		{
			HandleMovement();
		}

		    // UpdateVisualizations(); // change ants colorsd based on state
	}

	void ApplyTheoreticalModelForces()
	{
		// Only apply forces if attached to torus and in pulling or lifting state
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Lifting && currentState != State.Informed))
		{
			return;
		}


		Vector2 antToCenterDir = (targetTorus.currentPosition - currentPosition).normalized;

		// Calculate the local radial direction (n^i in the paper) - this is the unit vector from torus center to ant
		Vector2 localRadialDirection = -antToCenterDir;

		// Calculate the body axis vector of the ant
		Vector2 bodyAxisVector = currentForwardDir;

		// Calculate tilt angle φ (angle between radial direction and body axis)
		tiltAngle = Vector2.SignedAngle(localRadialDirection, bodyAxisVector) * Mathf.Deg2Rad;

		// Clamp tilt angle within [-φ_max, +φ_max] as per paper
		float maxTiltRadians = phi_max * Mathf.Deg2Rad;
		tiltAngle = Mathf.Clamp(tiltAngle, -maxTiltRadians, maxTiltRadians);

		// Calculate the effective Find based on number of ants carrying the torus
		// This implements the concept that the transition depends on N*f0/Find
		int totalCarriers = nOfStates["informed"] + nOfStates["puller"] + nOfStates["lifter"];
		effectiveFind = Find / totalCarriers;

		if (currentState == State.Informed)
		{
			// CRITICAL FIX: Actually calculate and apply force when informed

			// Find direction to home
			Vector2 directionToNest = Vector2.zero;
			Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);

			if (home)
			{
				// Direction toward home
				directionToNest = ((Vector2)home.transform.position - targetTorus.currentPosition).normalized;
			}
			else
			{
				// If home not visible, use direction away from torus as fallback
				directionToNest = (homePos - targetTorus.currentPosition).normalized;
			}

			// Implementation of the external field (informed ant) in the theoretical model
			float leadershipStrength = isLeader ? 2.0f : 1.0f;
			pullingForce = directionToNest * settings.collisionAvoidSteerStrength * leadershipStrength;

			// Apply force to torus - this implements equation (9) from the paper
			targetTorus.ApplyForce(pullingForce);
		}

		else if (currentState == State.Pulling)
		{
			// Implementation of equation (9) from the paper
			// Pulling force depends on alignment with the radial direction
			float pullMagnitude = f0 * Mathf.Cos(tiltAngle);

			// The effective force that contributes to translation
			pullingForce = bodyAxisVector * pullMagnitude;

			// Apply this force to the torus
			targetTorus.ApplyForce(pullingForce);
		}

		else if (currentState == State.Lifting)
		{
			// Implementation of equation (11) from the paper
			// Lifters reduce friction rather than directly applying force

			// Calculate the effective lifting force based on the number of lifters
			float liftingContribution = beta; // Each lifter reduces friction by beta

			// Apply a small upward force to simulate friction reduction
			pullingForce = Vector2.up * settings.collisionAvoidSteerStrength * 0.3f;

			// Apply this force to the torus
			targetTorus.ApplyForce(pullingForce);

			// The actual friction reduction is handled globally via nOfStates["lifter"]
		}
		if (currentState == State.Pulling)
		{
			// For pullers, apply force according to equation (9)
			// f_m = Σ n^i F_i - f_kin
			// Where n^i is the radial direction and F_i is the force applied by a single puller

			// In our case, each ant applies force in its forward direction
			Vector2 pullForce = bodyAxisVector * settings.collisionAvoidSteerStrength;

			// Calculate the effective pulling force as per the paper's model
			// The effective force depends on how aligned the ant is with the radial direction
			float pullMagnitude = pullForce.magnitude * Mathf.Cos(tiltAngle);

			// Apply the force in the direction of the body axis
			pullingForce = bodyAxisVector * pullMagnitude;

			// Apply this force to the torus
			targetTorus.ApplyForce(pullingForce);
		}
		else if (currentState == State.Lifting)
		{
			// For lifters, according to equation (11)
			// f_kin = max{F^0_kin - β*N_lifter, 0}
			// They don't apply direct force but reduce friction

			// We simulate this by applying a small upward force to reduce the effect of gravity/friction
			pullingForce = Vector2.up * settings.collisionAvoidSteerStrength * 0.3f;

			// Apply this force to the torus
			targetTorus.ApplyForce(pullingForce);

			// We would also want to reduce the friction term in the Torus class
			// This is handled by the global counter of lifters (nOfStates["lifter"])
			// and should be implemented in the Torus class's movement calculations
		}
	}

	void MoveRelativeToTorus()
	{
		if (targetTorus == null)
		{
			torusFollowForce = Vector2.zero;
			return;
		}

		Vector2 torusPos = targetTorus.currentPosition;
		float outerRadius = targetTorus.radius * 1.05f; // Stay slightly outside collider

		// If we're in an active state
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			// Maintain position around the torus perimeter according to attachment site
			Vector2 newOffset = new Vector2(
				Mathf.Cos(relativeAngleToTorus),
				Mathf.Sin(relativeAngleToTorus)
			) * outerRadius;

			// Set position relative to torus
			currentPosition = torusPos + newOffset;
			transform.position = currentPosition;

			// Calculate the total force direction from all ants on the torus
			Vector2 totalForceDir = targetTorus.totForce.normalized;

			// Orient ant based on its role according to the model
			Vector2 directionToFace;
			if (currentState == State.Pulling)
			{
				// Implementation of state-dependent orientation from the paper
				// When pulling, face partially away from torus with limited tilt angle

				// Calculate desired tilt for alignment with total force
				float desiredTilt = Vector2.SignedAngle(-newOffset.normalized, totalForceDir) * Mathf.Deg2Rad;
				desiredTilt = Mathf.Clamp(desiredTilt, -phi_max * Mathf.Deg2Rad, phi_max * Mathf.Deg2Rad);

				// Calculate the direction to face based on the tilt angle
				float facingAngle = Mathf.Atan2(newOffset.y, newOffset.x) + Mathf.PI + desiredTilt;
				directionToFace = new Vector2(Mathf.Cos(facingAngle), Mathf.Sin(facingAngle));
			}
			else if (currentState == State.Lifting)
			{
				// When lifting, face radially (toward torus center)
				directionToFace = -newOffset.normalized;
			}
			else // Informed state
			{
				Vector2 nestDirection = Vector2.zero;
				Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);
				if (home)
				{
					// Direction toward home
					nestDirection = ((Vector2)home.transform.position - torusPos).normalized;
					// Face in a direction that helps move toward nest
					Vector2 tangentVector = new Vector2(-newOffset.y, newOffset.x).normalized;
					// Orient to maximize effect in nest direction
					directionToFace = Vector2.Dot(tangentVector, nestDirection) > 0 ? tangentVector : -tangentVector;
				}
				else
				{
					// Default to tangential orientation if nest not visible
					directionToFace = new Vector2(-newOffset.y, newOffset.x).normalized;
				}
			}

			// Set rotation to face the appropriate direction
			transform.rotation = Quaternion.FromToRotation(Vector3.right, directionToFace);
			currentForwardDir = directionToFace;
		}
		// Otherwise, if we're approaching the torus
		else if (targetTorus != null)
		{
			Vector2 distance = currentPosition - torusPos;
			float dist = distance.magnitude;

			// If we're close to the torus but not yet attached
			if (dist > outerRadius && dist < outerRadius * 1.5f)
			{
				// Generate steering force toward the torus perimeter
				torusFollowForce = -distance.normalized * settings.targetSteerStrength;
			}
			else
			{
				torusFollowForce = Vector2.zero;
			}
		}
	}

	void HandleMovement()
	{
		Vector2 steerForce = randomSteerForce + pheromoneSteerForce + obstacleAvoidForce + torusFollowForce;

		if (turningAround)
		{
			steerForce += turnAroundForce * settings.targetSteerStrength;
			if (Time.time > turnAroundEndTime)
			{
				turningAround = false;
			}
		}

		Vector2 desiredVelocity = steerForce.normalized * settings.maxSpeed;
		SteerTowards(desiredVelocity);

		currentForwardDir = currentVelocity.normalized;
		float moveDst = currentVelocity.magnitude * Time.deltaTime;
		Vector2 desiredPos = currentPosition + currentVelocity * Time.deltaTime;

		RaycastHit2D hit = Physics2D.Raycast(currentPosition, currentForwardDir, Mathf.Max(settings.collisionRadius, moveDst), collisionMask);
		if (hit)
		{
			if (!turningAround)
			{
				StartTurnAround(Vector2.Reflect(currentForwardDir, hit.normal), 2);
			}
			desiredPos = hit.point - currentForwardDir * settings.collisionRadius;
		}

		currentPosition = desiredPos;
		transform.SetPositionAndRotation(new Vector3(currentPosition.x, currentPosition.y, -0.1f), Quaternion.FromToRotation(Vector3.right, currentForwardDir));
	}

	void HandleCollisionSteering()
	{
		RaycastHit2D hitLeft = Physics2D.Raycast(antennaLeft.position, antennaLeft.right, settings.antennaDst, collisionMask);
		RaycastHit2D hitRight = Physics2D.Raycast(antennaRight.position, antennaRight.right, settings.antennaDst, collisionMask);

		if (Time.time > obstacleForceResetTime)
		{
			obstacleAvoidForce = Vector2.zero;
			lastAntennaCollision = Antenna.None;
		}

		if (hitLeft || hitRight)
		{
			if (hitLeft && lastAntennaCollision != Antenna.Right && (!hitRight || hitLeft.distance < hitRight.distance))
			{
				obstacleAvoidForce = -transform.up * settings.collisionAvoidSteerStrength;
				lastAntennaCollision = Antenna.Left;
			}
			if (hitRight && lastAntennaCollision != Antenna.Left && (!hitLeft || hitRight.distance < hitLeft.distance))
			{
				obstacleAvoidForce = transform.up * settings.collisionAvoidSteerStrength;
				lastAntennaCollision = Antenna.Right;
			}

			obstacleForceResetTime = Time.time + 0.5f;
			randomSteerForce = obstacleAvoidForce.normalized * settings.randomSteerStrength;
		}
	}

	void SteerTowards(Vector2 desiredVelocity)
	{
		Vector2 steeringForce = desiredVelocity - currentVelocity;

		Vector2 acceleration = Vector2.ClampMagnitude(steeringForce * settings.acceleration, settings.acceleration);
		currentVelocity += acceleration * Time.deltaTime;
		currentVelocity = Vector2.ClampMagnitude(currentVelocity, settings.maxSpeed);
	}

	void HandleReturnHome()
	{
		Vector2 currentPos = transform.position;
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius, homeMask);
		if (home)
		{
			pheromoneSteerForce = ((Vector2)home.transform.position - currentPos).normalized * settings.targetSteerStrength;

			if (Vector2.SqrMagnitude(currentPos - homePos) < colony.radius * colony.radius)
			{
				deathTime = Time.time + settings.lifetime;
				Destroy(collectedFood.gameObject);
				currentState = State.SearchingForFood;
				nextDirUpdateTime = 0;
				StartTurnAround();
				colony.FoodCollected();
				leftHomeTime = Time.time;
			}
		}
		else
		{
			HandlePheromoneSteering();
		}
	}

	void HandleSearchForFood()
	{
		if (colony)
		{
			if (Vector2.SqrMagnitude(currentPosition - homePos) < colony.radius * colony.radius)
			{
				deathTime = Time.time + settings.lifetime;
				leftHomeTime = Time.time;
			}
		}

		if (targetFood == null)
		{
			int numFoodInRadius = Physics2D.OverlapCircleNonAlloc(perceptionCentre.position, settings.perceptionRadius, foodColliders, torusMask);
			if (numFoodInRadius > 0)
			{
				Collider2D foodCollider = foodColliders[Random.Range(0, numFoodInRadius)];
				targetTorus = foodCollider.GetComponent<Torus>();
				if (targetTorus != null)
				{
					Debug.Log("Found torus at " + targetTorus.currentPosition);
				}
				targetFood = foodCollider.transform;
				if (targetFood.CompareTag("SmallFood"))
				{
					targetFood.gameObject.layer = 0;
				}
			}
		}

		if (targetFood != null)
		{
			Vector2 offsetToFood = targetFood.transform.position - transform.position;
			float dstToFood = offsetToFood.magnitude;
			Vector2 dirToFood = offsetToFood / dstToFood;
			pheromoneSteerForce = dirToFood * settings.targetSteerStrength;

			// When close to the torus
			if (dstToFood < targetTorus.radius * 1.2f)
			{
				if (targetFood.CompareTag("SmallFood"))
				{
					// Small food can be carried back
					collectedFood = targetFood.transform;
					targetFood.position = head.position;
					targetFood.SetParent(transform, true);
					targetFood.gameObject.layer = 0;
					currentState = State.ReturningHome;
					nextDirUpdateTime = 0;
					targetFood = null;
					StartTurnAround();
					leftFoodTime = Time.time;
				}
				else
				{
					// Large food (torus) requires collaboration according to model
					targetFood.gameObject.layer = 0;
					currentState = State.Informed;
					nOfStates["informed"]++;

					// Store current relative position to torus
					hitRelativeToTorus = currentPosition - targetTorus.currentPosition;

					// Calculate angle for positioning around torus (attachment site)
					// This follows the "N_site equally spaced sites labelled by the angle θ_i" from paper
					Vector2 offset = currentPosition - targetTorus.currentPosition;
					relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x);

					// Update attachment rate based on torus movement
					UpdateAttachmentRate();

					// Store when we attached to the torus
					torusAttachmentTime = Time.time;

					// Stop normal movement
					currentVelocity = Vector2.zero;

					// Schedule transition after assessment period
					float timeAsInformed = 5f;
					Invoke("TransitionFromInformed", timeAsInformed);
				}
			}
		}
		else
		{
			HandlePheromoneSteering();
		}
	}

	float CalculateInformedAssessmentTime()
	{
		// Calculate assessment time based on proximity to critical point
		// Groups near critical size should have longer assessment periods
		int totalCarriers = nOfStates["informed"] + nOfStates["puller"] + nOfStates["lifter"];

		// Calculate distance from critical point (N*f0/Find ~ 0.5)
		float criticalRatio = totalCarriers * f0 / Find;
		float distanceFromCritical = Mathf.Abs(criticalRatio - Find_c);

		// Groups near critical size (0.5) get longer assessment time
		// Peaked at critical point, falls off as we move away
		float baseTime = 5.0f;
		float criticalBonus = Mathf.Exp(-distanceFromCritical * 2.0f) * 3.0f;

		return baseTime + criticalBonus;
	}

	void UpdateAttachmentRate()
	{
		// According to the paper, attachment rate depends on cargo movement
		// "when the cargo is stationary the detachment rate is higher than the rate when the cargo is moving"
		if (targetTorus != null)
		{
			float torusSpeed = targetTorus.totForce.magnitude;

			// Higher attachment rate when torus is moving (as described in paper)
			if (torusSpeed > 0.1f)
			{
				attachmentRate = 0.8f;  // Higher rate when moving
				detachmentRate = 0.2f;  // Lower detachment when moving
			}
			else
			{
				attachmentRate = 0.4f;  // Lower rate when stationary
				detachmentRate = 0.6f;  // Higher detachment when stationary
			}
		}
	}

	void TransitionFromInformed()
	{
		if (targetTorus == null || currentState != State.Informed)
		{
			return;
		}

		nOfStates["informed"]--;

		// Calculate appropriate role based on theoretical model
		bool shouldPull = CalculateRoleProbability();

		if (shouldPull)
		{
			// become a puller
			currentState = State.Pulling;
			nOfStates["puller"]++;
			Debug.Log("Transitioning to Pulling state");
		}
		else
		{
			// become a lifter
			currentState = State.Lifting;
			nOfStates["lifter"]++;
			Debug.Log("Transitioning to Lifting state");
		}

		// Schedule role reassessment according to stochastic model
		// Rate depends on proximity to critical point for maximal responsiveness
		float criticalFactor = CalculateCriticalityFactor();
		float reassessmentRate = 3.0f * criticalFactor;
		Invoke("EvaluateRoleSwitching", reassessmentRate);
	}

	float CalculateCriticalityFactor()
	{
		// Calculate a factor that peaks at the critical point
		int totalCarriers = nOfStates["informed"] + nOfStates["puller"] + nOfStates["lifter"];
		float criticalRatio = totalCarriers * f0 / Find;
		float distanceFromCritical = Mathf.Abs(criticalRatio - Find_c);

		// Factor peaks at 1.0 at critical point, decays away from it
		return Mathf.Exp(-distanceFromCritical * 1.5f) + 0.5f;
	}

	bool CalculateRoleProbability()
	{
		// Implementation of the Ising-like model for role assignment

		// Check if we can see home to implement directional bias
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);
		float dotProduct = 0f;

		if (home)
		{
			Vector2 homeDirection = ((Vector2)home.transform.position - currentPosition).normalized;
			Vector2 torusToAnt = (currentPosition - targetTorus.currentPosition).normalized;
			dotProduct = Vector2.Dot(homeDirection, torusToAnt);
		}

		// If no clear positional advantage, use a probabilistic approach
		// Start with 50/50 distribution
		float pullProbability = 0.5f;

		// Leaders (informed ants) prefer pulling when in good position
		if (isLeader && dotProduct > 0)
		{
			pullProbability = 0.8f;
		}


		// Adjust based on current distribution of roles
		int totalAttached = nOfStates["puller"] + nOfStates["lifter"];
		if (totalAttached > 0)
		{
			// Current ratio of pullers
			float pullerRatio = (float)nOfStates["puller"] / totalAttached;

			// Calculate distance from critical point
			int allAnts = totalAttached + nOfStates["informed"];
			float criticalRatio = allAnts * f0 / Find;
			float distanceFromCritical = Mathf.Abs(criticalRatio - Find_c);

			// Near critical point: balance roles more closely to 50/50
			// Far from critical point: stronger tendency to compensate for imbalance
			float balancingStrength = 1.0f + distanceFromCritical;

			// Adjust probability to balance the system
			pullProbability = 1.0f - Mathf.Pow(pullerRatio, balancingStrength);
		}

		// Make final decision
		return Random.value < pullProbability;
	}

	void EvaluateRoleSwitching()
	{
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Lifting))
		{
			return;
		}

		UpdateAttachmentRate();

		// Implementation of equation (1) from the paper for role switching

		// Get normalized force directions
		Vector2 antForce = Vector2.zero;
		if (currentState == State.Pulling)
		{
			antForce = pullingForce.normalized;
		}
		else
		{
			// For lifters, use their orientation as their "preferred" force direction
			antForce = currentForwardDir;
		}

		Vector2 totalForceDirection = targetTorus.totForce.normalized;

		// Calculate alignment (dot product) between ant's force and total force
		float alignment = Vector2.Dot(antForce, totalForceDirection);

		// Calculate exponent term from the paper
		float exponent = alignment / Find;

		float switchProbability;

		// Implement the switching rates described in the paper
		if (currentState == State.Pulling)
		{
			// Probability of Puller → Lifter transition
			// Higher when alignment is negative (pulling against total force)
			switchProbability = Kc * Mathf.Exp(-exponent);
		}
		else // Lifting state
		{
			// Probability of Lifter → Puller transition.
			// Higher when alignment is positive  (could help by pulling)
			switchProbability = Kc * Mathf.Exp(exponent);
		}

		// Scale by time delta to get per-frame probability
		float frameProb = switchProbability * Time.deltaTime * 10f;
		frameProb = Mathf.Clamp01(frameProb);

		// Apply critical point sensitivity - increased switching near critical point

		int totalAttached = nOfStates["puller"] + nOfStates["lifter"] + nOfStates["informed"];
		float criticalRatio = totalAttached * f0 / Find;
		float distanceFromCritical = Mathf.Abs(criticalRatio - Find_c);

		// Enhance switching rate near critical point for maximum responsiveness
		if (distanceFromCritical < 0.2f)
		{
			frameProb *= 1.5f;
			frameProb = Mathf.Clamp01(frameProb);
		}

		// Apply stochastic role switching
		if (Random.value < frameProb)
		{
			if (currentState == State.Pulling)
			{
				currentState = State.Lifting;
				nOfStates["puller"]--;
				nOfStates["lifter"]++;
			}
			else
			{
				currentState = State.Pulling;
				nOfStates["lifter"]--;
				nOfStates["puller"]++;
			}
		}

		// Continue evaluating role switching
		float reassessmentRate = 3f * CalculateCriticalityFactor(); // Reassess every 3 seconds
		Invoke("EvaluateRoleSwitching", reassessmentRate);
	}

	void StartTurnAround(Vector2 returnDir, float randomStrength = 0.2f)
	{
		turningAround = true;
		turnAroundEndTime = Time.time + 1.5f;
		Vector2 perpAxis = new Vector2(-returnDir.y, returnDir.x);
		turnAroundForce = returnDir + perpAxis * (Random.value - 0.5f) * 2 * randomStrength;
	}

	void StartTurnAround(float randomStrength = 0.2f)
	{
		StartTurnAround(-currentForwardDir, randomStrength);
	}

	void HandlePheromonePlacement()
	{
		if (Vector2.Distance(transform.position, lastPheromonePos) > settings.dstBetweenMarkers)
		{
			if (currentState == State.SearchingForFood && settings.useHomeMarkers && (Time.time - leftHomeTime) < settings.pheromoneRunOutTime)
			{
				float t = 1 - (Time.time - leftHomeTime) / settings.pheromoneRunOutTime;
				t = Mathf.Lerp(0.5f, 1, t);
				colony.homeMarkers.Add(transform.position, t);
				lastPheromonePos = transform.position + (Vector3)Random.insideUnitCircle * settings.dstBetweenMarkers * 0.2f;
			}
			else if ((currentState == State.ReturningHome || currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
			 && settings.useFoodMarkers && (Time.time - leftFoodTime) < settings.pheromoneRunOutTime)
			{
				float t = 1 - (Time.time - leftFoodTime) / settings.pheromoneRunOutTime;
				t = Mathf.Lerp(0.5f, 1, t);
				colony.foodMarkers.Add(transform.position, t);
				lastPheromonePos = transform.position + (Vector3)Random.insideUnitCircle * settings.dstBetweenMarkers * 0.2f;
			}
		}
	}

	void HandlePheromoneSteering()
	{
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			pheromoneSteerForce = Vector2.zero;
			return;
		}

		if (Time.time > nextDirUpdateTime)
		{
			Vector2 leftSensorDir = (currentForwardDir + (Vector2)transform.up * settings.sensorDst).normalized;
			Vector2 rightSensorDir = (currentForwardDir - (Vector2)transform.up * settings.sensorDst).normalized;

			pheromoneSteerForce = Vector2.zero;
			float currentTime = Time.time;
			const int centreIndex = 0;
			const int leftIndex = 1;
			const int rightIndex = 2;
			nextDirUpdateTime = Time.time + settings.timeBetweenDirUpdate;

			// center
			sensors[centreIndex] = currentPosition + currentForwardDir * settings.sensorDst;
			// left
			sensors[leftIndex] = currentPosition + leftSensorDir * settings.sensorDst;
			// right
			sensors[rightIndex] = currentPosition + rightSensorDir * settings.sensorDst;

			for (int i = 0; i < 3; i++)
			{
				sensorData[i] = 0;
				int numPheromones = 0;
				if (currentState == State.SearchingForFood && settings.useFoodMarkers)
				{
					numPheromones = colony.foodMarkers.GetAllInCircle(pheromoneEntries, sensors[i]);
				}
				if (currentState == State.ReturningHome && settings.useHomeMarkers)
				{
					numPheromones = colony.homeMarkers.GetAllInCircle(pheromoneEntries, sensors[i]);
				}
				for (int j = 0; j < numPheromones; j++)
				{
					float evaporateT = ((currentTime - pheromoneEntries[j].creationTime) / settings.pheromoneEvaporateTime);
					float strength = Mathf.Clamp01(1 - evaporateT);
					sensorData[i] += strength;
				}
			}

			float centre = sensorData[centreIndex];
			float left = sensorData[leftIndex];
			float right = sensorData[rightIndex];

			if (centre > left && centre > right)
			{
				pheromoneSteerForce = currentForwardDir * settings.pheromoneWeight;
			}
			else if (left > right)
			{
				pheromoneSteerForce = leftSensorDir * settings.pheromoneWeight;
			}
			else if (right > left)
			{
				pheromoneSteerForce = rightSensorDir * settings.pheromoneWeight;
			}
		}
	}


	void HandleRandomSteering()
	{
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			randomSteerForce = Vector2.zero;
			return;
		}

		if (targetFood != null)
		{
			randomSteerForce = Vector2.zero;  //maybe we should had rundom steer here too
			return;
		}

		if (Time.time > nextRandomSteerTime)
		{
			nextRandomSteerTime = Time.time + Random.Range(settings.randomSteerMaxDuration / 3, settings.randomSteerMaxDuration);
			randomSteerForce = GetRandomDir(currentForwardDir, 5) * settings.randomSteerStrength;
		}
	}

	Vector2 GetRandomDir(Vector2 referenceDir, int similarity = 4)
	{
		Vector2 smallestRandomDir = Vector2.zero;
		float change = -1;
		const int iterations = 4;
		for (int i = 0; i < iterations; i++)
		{
			Vector2 randomDir = Random.insideUnitCircle.normalized;
			float dot = Vector2.Dot(referenceDir, randomDir);
			if (dot > change)
			{
				change = dot;
				smallestRandomDir = randomDir;
			}
		}
		return smallestRandomDir;
	}

	// Add new methods to implement the theoretical model more precisely

	// Method to calculate the kinetic friction force based on equation (11)
	float CalculateKineticFriction()
	{
		if (targetTorus == null)
			return 0f;

		// f_kin = max{F^0_kin - β*N_lifter, 0} (Equation 11)
		int numLifters = nOfStates["lifter"];
		float frictionReduction = beta * numLifters;
		float remainingFriction = Mathf.Max(barefrictionForce - frictionReduction, 0f);

		return remainingFriction;
	}

	// Method to calculate torque as per equation (10)
	Vector2 CalculateTorque()
	{
		if (targetTorus == null)
			return Vector2.zero;

		// r_i is the outer radius of the object (distance from center to ant)
		float r_i = targetTorus.radius;

		// τ_rot = (r_i/γ_rot) * r_i × ω (Equation 10)
		// We're in 2D so the cross product becomes a scalar
		float angularVelocity = targetTorus.GetAngularVelocity();

		// Calculate perpendicular component 
		Vector2 radiusVector = currentPosition - targetTorus.currentPosition;
		Vector2 tangentVector = new Vector2(-radiusVector.y, radiusVector.x).normalized;

		// Calculate torque magnitude
		float torqueMagnitude = (r_i / gamma_rot) * r_i * angularVelocity;

		// Return as a force in the tangential direction
		return tangentVector * torqueMagnitude;
	}

	// Helper method to get the tilt angle for position around torus
	float GetTiltAngle(Vector2 forceDirection)
	{
		// Calculate the tilt angle φ between the pull direction and the radial direction
		Vector2 radiusVector = (targetTorus.currentPosition - currentPosition).normalized;

		// Get the signed angle between vectors in degrees
		float angle = Vector2.SignedAngle(radiusVector, forceDirection);

		// Convert to radians for theoretical model calculations
		return angle * Mathf.Deg2Rad;
	}

	// Method to apply force as per equations (8) and (9)
	void ApplyForceToTorus()
	{
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Informed))
			return;

		// Vector from torus center to ant (radial direction n^i in paper)
		Vector2 radiusVector = currentPosition - targetTorus.currentPosition;
		Vector2 radialDirection = radiusVector.normalized;

		// Calculate force applied by this ant (F_i in paper)
		float forceMagnitude = f0;
		Vector2 pullForce = currentForwardDir * forceMagnitude;

		// Calculate the projected component of force onto radial direction (n^i·F_i)
		float radialComponent = Vector2.Dot(pullForce, radialDirection);

		// Calculate friction force
		float frictionForce = CalculateKineticFriction();

		// Calculate effective force according to equation (9)
		Vector2 effectiveForce = Vector2.zero;
		if (currentState == State.Pulling)
		{
			// For pullers: Apply force along body axis, modified by tilt angle
			effectiveForce = pullForce * Mathf.Cos(tiltAngle);
		}
		else if (currentState == State.Informed)
		{
			// For informed ants: Direct force toward nest
			Vector2 directionToNest = (homePos - targetTorus.currentPosition).normalized;
			effectiveForce = directionToNest * forceMagnitude;
		}

		// Apply force to torus
		targetTorus.ApplyForce(effectiveForce);
	}

	// Method to calculate the center of mass velocity as per equation (12)
	Vector2 CalculateCenterOfMassVelocity()
	{
		if (targetTorus == null)
			return Vector2.zero;

		// V_cm = F_cm / γ (Equation 12)
		Vector2 totalForce = targetTorus.totForce;
		Vector2 centerOfMassVelocity = totalForce / gamma;
		return centerOfMassVelocity;
	}

	// Method to calculate angular velocity as per equation (13)
	float CalculateAngularVelocity()
	{
		if (targetTorus == null)
			return 0f;

		// ω = (1/γ_rot) * Σ(r^i * sin(φ) - f_kin) (Equation 13)

		// We would need to sum contributions from all ants
		// For simplicity, we'll just calculate this ant's contribution

		float r_i = targetTorus.radius; // Distance to ant

		// Calculate sine of tilt angle
		float sinPhi = Mathf.Sin(tiltAngle);

		// Calculate kinetic friction
		float frictionTorque = CalculateKineticFriction();

		// Calculate numerator term for this ant
		float torqueContribution = r_i * sinPhi - frictionTorque;

		// Calculate angular velocity (partial - would need sum from all ants)
		float angularVelocity = torqueContribution / gamma_rot;

		return angularVelocity;
	}

	// Improved method to implement the stochastic attachment-detachment dynamics
	void ProcessAttachmentDetachment()
	{
		if (targetTorus == null)
			return;

		// Update attachment/detachment rates based on torus movement
		UpdateAttachmentRate();

		// For attached ants, calculate chance to detach
		if (currentState == State.Pulling || currentState == State.Lifting)
		{
			// Apply detachment probability using the Boltzmann factor from the paper
			float detachProb = detachmentRate * Time.deltaTime;

			// Modify probability based on alignment with total force (as in equation 1)
			Vector2 totalForceDir = targetTorus.totForce.normalized;
			float alignment = Vector2.Dot(currentForwardDir, totalForceDir);

			if (currentState == State.Pulling)
			{
				// Pullers more likely to detach when misaligned with total force
				detachProb *= Mathf.Exp(-alignment / Find);
			}
			else
			{
				// Lifters more likely to detach when aligned with total force
				detachProb *= Mathf.Exp(alignment / Find);
			}

			if (Random.value < detachProb)
			{
				// Detach from torus
				if (currentState == State.Pulling)
					nOfStates["puller"]--;
				else
					nOfStates["lifter"]--;

				// Return to searching
				currentState = State.SearchingForFood;
				targetTorus = null;
				targetFood = null;

				// Move away from torus
				StartTurnAround();
			}
		}
		// For nearby but unattached ants, calculate chance to attach
		else if (currentState == State.SearchingForFood &&
				targetTorus != null &&
				Vector2.Distance(currentPosition, targetTorus.currentPosition) < targetTorus.radius * 1.5f)
		{
			// Apply attachment probability
			if (Random.value < attachmentRate * Time.deltaTime)
			{
				// Become informed
				currentState = State.Informed;
				nOfStates["informed"]++;

				// Calculate position on torus
				Vector2 offset = currentPosition - targetTorus.currentPosition;
				relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x);

				// Schedule role decision 
				float assessmentTime = CalculateInformedAssessmentTime();
				Invoke("TransitionFromInformed", assessmentTime);
			}
		}
	}


	// Process the complete stochastic dynamics model
	void ProcessStochasticDynamics()
	{
		// Handle attachment/detachment dynamics
		ProcessAttachmentDetachment();

		// Calculate total force on torus for phase transition analysis
		if (targetTorus != null)
		{
			// Analyze proximity to critical point
			int totalCarriers = nOfStates["informed"] + nOfStates["puller"] + nOfStates["lifter"];
			float criticalRatio = totalCarriers * f0 / Find;

			// Identify whether we're near critical point for optimal responsiveness
			bool nearCritical = Mathf.Abs(criticalRatio - Find_c) < 0.2f;

			// Adjust behavior if we're near critical point
			if (nearCritical && currentState == State.Informed)
			{
				// Enhanced influence of informed ants near critical point
				// This implements the "susceptibility" concept from the paper
				isLeader = true;
			}
		}
	}
		// Add this method to the Ant class
	public void DetachFromTorus(Torus torus)
	{
		Debug.Log("Ant detaching from torus: ");
		// Only act if this is the torus we're attached to
		if (torus == targetTorus)
		{
			// Reset the ant's state
			if (currentState == State.Informed)
				nOfStates["informed"]--;
			else if (currentState == State.Pulling)
				nOfStates["puller"]--;
			else if (currentState == State.Lifting)
				nOfStates["lifter"]--;

			// Set the ant back to searching for food
			currentState = State.SearchingForFood;
			targetTorus = null;
			targetFood = null;

			// Reset velocity and set the ant on a random path
			currentVelocity = GetRandomDir(currentForwardDir) * settings.maxSpeed;

			// Mark that we're leaving from the colony
			leftHomeTime = Time.time;
			deathTime = Time.time + settings.lifetime;

			// Cancel any pending role transitions
			CancelInvoke("TransitionFromInformed");
			CancelInvoke("EvaluateRoleSwitching");
		}
	}

	/* void UpdateVisualizations()
	{
		if (currentState == State.Pulling || currentState == State.Lifting){
			GetComponent<Renderer>().material.color = Color.green;
		}
	} */
}

