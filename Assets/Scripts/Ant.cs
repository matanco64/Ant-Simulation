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

	// Model parameters from the paper
	[Header("Theoretical Model Parameters")]
	public float Kc = 0.2f;              // Switching rate coefficient
	public float Find = 1f;              // Alignment sensitivity
	public float beta = 0.5f;            // Friction reduction factor due to lifters
	public float gamma = 1.0f;           // Mass response coefficient
	public float gamma_rot = 1.0f;       // Rotational response coefficient

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

	// Variables for theoretical model
	float tiltAngle;           // φ in the paper
	float attachmentRate;      // K_att in the paper
	float detachmentRate;      // K_det in the paper
	float barefrictionForce;   // F^0_kin in the paper

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
		barefrictionForce = 0.5f;  // This would be calibrated based on your simulation
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
			}
		}
		else
		{
			HandleMovement();
		}
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

		if (currentState == State.Informed)
		{
			// CRITICAL FIX: Actually calculate and apply force when informed

			// Find direction to home
			Vector2 directionToHome = Vector2.zero;
			Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);

			if (home)
			{
				// Direction toward home
				directionToHome = ((Vector2)home.transform.position - targetTorus.currentPosition).normalized;
			}
			else
			{
				// If home not visible, use direction away from torus as fallback
				directionToHome = -antToCenterDir;
			}

			// Apply strong push force - needs to be strong enough to overcome inertia
			float pushStrength = settings.collisionAvoidSteerStrength * 1.5f;
			pullingForce = directionToHome * pushStrength;

			// CRITICAL: Actually apply the force to torus
			targetTorus.ApplyForce(pullingForce);

			Debug.Log("Informed ant applying force: " + pullingForce.magnitude);
		}
		if (currentState == State.Pulling)
		{


			// Calculate the effective pulling force as per the paper's model
			// The effective force depends on how aligned the ant is with the radial direction
			float pullMagnitude = settings.collisionAvoidSteerStrength * Mathf.Cos(tiltAngle);

			// Apply the force in the direction of the body axis
			pullingForce = bodyAxisVector * pullMagnitude;
		}

		if (currentState == State.Lifting)
		{
			// For lifters, we don't apply direct force but reduce friction
			pullingForce = Vector2.zero;
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

			// Orient ant based on its role according to the model
			Vector2 directionToFace;
			if (currentState == State.Pulling)
			{
				// When pulling, face partially away from torus
				// The tilt angle φ is limited to a window of orientation [-φ_max, +φ_max]
				float maxTiltAngle = 60f * Mathf.Deg2Rad; // φ_max in radians

				// Calculate desired tilt - try to align with total force direction
				Vector2 totalForceDir = targetTorus.totForce.normalized;
				float desiredTilt = Vector2.SignedAngle(-newOffset.normalized, totalForceDir) * Mathf.Deg2Rad;
				desiredTilt = Mathf.Clamp(desiredTilt, -maxTiltAngle, maxTiltAngle);

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
				// When informed, face tangentially to assess the situation
				directionToFace = new Vector2(-newOffset.y, newOffset.x).normalized;
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
		// Debug.DrawRay (antennaLeft.position, antennaLeft.right * ((hitLeft) ? hitLeft.distance : settings.antennaDst), (hitLeft) ? Color.red : Color.green);
		// Debug.DrawRay (antennaRight.position, antennaRight.right * ((hitRight) ? hitRight.distance : settings.antennaDst), (hitRight) ? Color.red : Color.green);

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
					Debug.Log("Transitioning to Informed state");
					targetFood.gameObject.layer = 0;
					currentState = State.Informed;
					nOfStates["informed"]++;

					// Store current relative position to torus
					hitRelativeToTorus = currentPosition - targetTorus.currentPosition;

					// Calculate angle for positioning around torus (attachment site)
					// This follows the "N_site equally spaced sites labelled by the angle θ_i" from paper
					Vector2 offset = currentPosition - targetTorus.currentPosition;
					relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x);

					// Calculate attachment rate based on torus movement
					// K_att depends on the motion of the cargo according to paper
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

		// According to the model, role assignment depends on ant position and force alignment
		// Calculate appropriate role based on theoretical model
		bool shouldPull = CalculateRoleProbability();

		if (shouldPull)
		{
			// Pull - ant decides to become a puller
			currentState = State.Pulling;
			nOfStates["puller"]++;
			Debug.Log("Transitioning to Pulling state");
		}
		else
		{
			// Lift - ant decides to become a lifter
			currentState = State.Lifting;
			nOfStates["lifter"]++;
			Debug.Log("Transitioning to Lifting state");
		}

		// Schedule role reassessment according to stochastic model
		float reassessmentRate = 5f;
		Invoke("EvaluateRoleSwitching", reassessmentRate);
	}

	bool CalculateRoleProbability()
	{
		// Initially assign role based on position relative to cargo movement direction
		// and cargo proximity to home (as a heuristic for what would be useful)

		// Check if we can see home
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);

		if (home)
		{
			Vector2 homeDirection = ((Vector2)home.transform.position - currentPosition).normalized;
			Vector2 torusToAnt = (currentPosition - targetTorus.currentPosition).normalized;

			// Calculate dot product to determine if ant is between home and torus
			float dotProduct = Vector2.Dot(homeDirection, torusToAnt);

			// If ant is between home and torus, pulling makes more sense
			if (dotProduct > 0.3f)
			{
				return true; // Become a puller
			}

			// If home is behind torus relative to ant, lifting makes more sense
			if (dotProduct < -0.3f)
			{
				return false; // Become a lifter
			}
		}

		// If no clear positional advantage, use a probabilistic approach
		// Start with 50/50 distribution
		float pullProbability = 0.5f;

		// Adjust based on current distribution of roles
		int totalAttached = nOfStates["puller"] + nOfStates["lifter"];
		if (totalAttached > 0)
		{
			// Try to maintain a balance - more likely to become the less common role
			float pullerRatio = (float)nOfStates["puller"] / totalAttached;

			// Adjust probability inverse to current ratio
			pullProbability = 1.0f - pullerRatio;
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
		// Calculate probability based on alignment with overall force

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
			// exp(-exponent) gives higher probability when alignment is negative
			switchProbability = Kc * Mathf.Exp(-exponent);
		}
		else // Lifting state
		{
			// Probability of Lifter → Puller transition
			// exp(exponent) gives higher probability when alignment is positive
			switchProbability = Kc * Mathf.Exp(exponent);
		}

		// Scale by time delta to get per-frame probability
		float frameProb = switchProbability * Time.deltaTime * 10f;

		// Clamp to sensible range
		frameProb = Mathf.Clamp01(frameProb);

		// Apply stochastic role switching
		if (Random.value < frameProb)
		{
			if (currentState == State.Pulling)
			{
				currentState = State.Lifting;
				nOfStates["puller"]--;
				nOfStates["lifter"]++;
				Debug.Log($"Switched to Lifting. Alignment: {alignment:F2}, Probability: {frameProb:F3}");
			}
			else
			{
				currentState = State.Pulling;
				nOfStates["lifter"]--;
				nOfStates["puller"]++;
				Debug.Log($"Switched to Pulling. Alignment: {alignment:F2}, Probability: {frameProb:F3}");
			}
		}

		// Continue evaluating role switching
		float reassessmentRate = 3f; // Reassess every 3 seconds
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
			randomSteerForce = Vector2.zero;
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
		// In 2D, r × F = r.magnitude * F.magnitude * sin(angle)

		// Get angular velocity (we'd need to get this from the torus)
		float angularVelocity = targetTorus.GetAngularVelocity();

		// Calculate perpendicular component 
		// In 2D, this would be the perpendicular direction to radius vector
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

		// Vector from torus center to ant
		Vector2 radiusVector = currentPosition - targetTorus.currentPosition;
		Vector2 radialDirection = radiusVector.normalized;

		// Calculate the unit radial vector n^i
		Vector2 n_i = radialDirection;

		// Calculate the force applied by this ant
		Vector2 pullForce = currentForwardDir * settings.collisionAvoidSteerStrength;

		// Project pull force onto radial direction (dot product) - this is n^i·F_i in equation (9)
		float radialComponent = Vector2.Dot(pullForce, n_i);

		// Calculate the friction force based on number of lifters
		float frictionForce = CalculateKineticFriction();

		// Calculate net force in radial direction
		float netRadialForce = radialComponent - frictionForce;

		// Apply this force to the torus if positive
		if (netRadialForce > 0)
		{
			Vector2 effectiveForce = n_i * netRadialForce;
			targetTorus.ApplyForce(effectiveForce);
		}
	}

	// Method to calculate the center of mass velocity as per equation (12)
	Vector2 CalculateCenterOfMassVelocity()
	{
		if (targetTorus == null)
			return Vector2.zero;

		// V_cm = F_cm / γ (Equation 12)
		// We'd get F_cm from the torus as the sum of all applied forces
		Vector2 totalForce = targetTorus.totForce;

		// Calculate V_cm
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

	// Method to implement the stochastic attachment-detachment dynamics
	void ProcessAttachmentDetachment()
	{
		if (targetTorus == null)
			return;

		// Update attachment/detachment rates based on torus movement
		UpdateAttachmentRate();

		// For attached ants, chance to detach
		if (currentState == State.Pulling || currentState == State.Lifting)
		{
			// Apply detachment probability
			if (Random.value < detachmentRate * Time.deltaTime)
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

		// For nearby but unattached ants, chance to attach
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
				Invoke("TransitionFromInformed", Random.Range(3f, 5f));
			}
		}
	}

	void ProcessStochasticDynamics()
	{
		// Handle stochastic attachment/detachment
		ProcessAttachmentDetachment();
	}
}
