/* using System.Collections;
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

	// Role transition parameters - adjusted for better behavior
	public float Kc = 0.4f;              // Higher value for more frequent role switching 
	public float Find = 0.8f;            // Reduced for more sensitive response to force alignment
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

	// New properties for improved behavior
	float lastRoleAssessmentTime;
	float roleStabilityTime = 5f;  // Time to stay in role before switching
	bool hasFoundOptimalPosition = false;
	float positionOptimizeTime;
	int antID;  // To help ants distinguish themselves
	public float timeBetweenRandomSteers = 0.5f;

	public void SetColony(AntColony colony)
	{
		this.colony = colony;
	}

	void Start()
	{
		antID = Random.Range(0, 10000);  // Give each ant a unique ID
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
		lastRoleAssessmentTime = Time.time;
		positionOptimizeTime = Time.time;
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
			OptimizePositionAroundTorus();
			MoveRelativeToTorus();
			PushPullTorus();
		}
		else
		{
			HandleMovement();
		}
	}

	// New method to optimize ant positioning around torus
	void OptimizePositionAroundTorus()
	{
		if (targetTorus == null || hasFoundOptimalPosition)
			return;

		// Only optimize position periodically
		if (Time.time < positionOptimizeTime)
			return;

		positionOptimizeTime = Time.time + 2f;

		// Get other ants around the torus
		// This is simplified - in a real implementation you'd detect nearby ants
		float numAntsOnTorus = nOfStates["informed"] + nOfStates["puller"] + nOfStates["lifter"];

		if (numAntsOnTorus < 2)
		{
			// If we're the only ant, position doesn't matter much
			hasFoundOptimalPosition = true;
			return;
		}

		// Spread ants evenly around the torus
		// Use ant ID to determine a stable position on the torus
		float optimalAngle = (antID % 20) / 20f * 2f * Mathf.PI;

		// Gradually move toward that position
		float angleDiff = Mathf.DeltaAngle(relativeAngleToTorus * Mathf.Rad2Deg, optimalAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;

		if (Mathf.Abs(angleDiff) < 0.1f)
		{
			hasFoundOptimalPosition = true;
		}
		else
		{
			// Move toward optimal position
			relativeAngleToTorus += angleDiff * 0.1f;

			// Keep angle in proper range
			while (relativeAngleToTorus > 2f * Mathf.PI)
				relativeAngleToTorus -= 2f * Mathf.PI;

			while (relativeAngleToTorus < 0)
				relativeAngleToTorus += 2f * Mathf.PI;
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

		// If we're in an active state (informed, pulling, lifting)
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			// Maintain position around the torus perimeter
			Vector2 newOffset = new Vector2(
				Mathf.Cos(relativeAngleToTorus),
				Mathf.Sin(relativeAngleToTorus)
			) * outerRadius;

			// Set position relative to torus
			currentPosition = torusPos + newOffset;
			transform.position = currentPosition;

			// Orient ant facing toward or away from torus based on state
			Vector2 directionToFace;
			if (currentState == State.Pulling)
			{
				// When pulling, face away from torus (toward estimated nest direction)
				directionToFace = -newOffset.normalized;

				// Mix in some home direction information
				Vector2 homeDirection = FindHomeDirection();
				directionToFace = Vector2.Lerp(directionToFace, homeDirection, 0.3f).normalized;
			}
			else
			{
				// When informed or lifting, face toward torus center
				directionToFace = newOffset.normalized;
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

	void PushPullTorus()
	{
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Lifting))
		{
			return;
		}

		// Calculate direction vector from torus to ant
		Vector2 offset = currentPosition - targetTorus.currentPosition;
		Vector2 direction = offset.normalized;

		if (currentState == State.Pulling)
		{
			// When pulling, apply force away from the ant's position (toward nest)
			// Find direction toward home if possible
			Vector2 homeDirection = FindHomeDirection();

			// Blend home direction with ant-torus direction for more natural movement
			Vector2 pullDirection = Vector2.Lerp(homeDirection, -direction, 0.3f).normalized;

			// Scale force based on coordination with other ants
			float forceMultiplier = 0.8f;

			// If we're aligned with overall movement, pull harder
			if (targetTorus.totForce.magnitude > 0.1f)
			{
				float alignment = Vector2.Dot(pullDirection, targetTorus.totForce.normalized);
				forceMultiplier *= Mathf.Lerp(0.8f, 1.2f, (alignment + 1f) * 0.5f);
			}

			pullingForce = pullDirection * settings.collisionAvoidSteerStrength * forceMultiplier;
		}
		else if (currentState == State.Lifting)
		{
			// When lifting, reduce friction by applying a small upward force
			// This simulates the ant helping to lift the torus

			// Calculate a lifting force that's perpendicular to movement direction
			Vector2 liftDirection = Vector2.up;

			// If torus is already moving, lift perpendicular to movement
			if (targetTorus.totForce.magnitude > 0.1f)
			{
				Vector2 movementDir = targetTorus.totForce.normalized;
				liftDirection = new Vector2(-movementDir.y, movementDir.x);

				// Bias upward
				liftDirection = Vector2.Lerp(liftDirection, Vector2.up, 0.7f).normalized;
			}

			pullingForce = liftDirection * settings.collisionAvoidSteerStrength * 0.5f;
		}

		// Apply the force to the torus
		targetTorus.ApplyForce(pullingForce);
	}

	Vector2 FindHomeDirection()
	{
		// Try to locate the home
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);
		if (home)
		{
			// Return direction toward home
			return ((Vector2)home.transform.position - currentPosition).normalized;
		}

		// If home not in perception range, use the direction the ant came from
		// (Ants would typically remember the general direction they came from)
		if (currentState == State.Pulling && Time.time - torusAttachmentTime < 10f)
		{
			// Use the ant's initial orientation when it first attached to the torus
			// This simulates the ant remembering where it came from
			return -hitRelativeToTorus.normalized;
		}

		// Fallback: use a random direction with slight bias toward home position
		return ((homePos - currentPosition).normalized + Random.insideUnitCircle * 0.3f).normalized;
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
					// Large food (torus) requires collaboration
					Debug.Log("Transitioning to Informed state");
					targetFood.gameObject.layer = 0;
					currentState = State.Informed;
					nOfStates["informed"]++;

					// Store current relative position to torus
					hitRelativeToTorus = currentPosition - targetTorus.currentPosition;

					// Calculate angle for positioning around torus
					Vector2 offset = currentPosition - targetTorus.currentPosition;
					relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x);
					hasFoundOptimalPosition = false;  // Need to find optimal position

					// Store when we attached to the torus
					torusAttachmentTime = Time.time;
					lastRoleAssessmentTime = Time.time;

					// Stop normal movement
					currentVelocity = Vector2.zero;

					// Schedule transition after assessment period
					float timeAsInformed = Random.Range(8f, 12f);  // Stagger transition times
					Invoke("TransitionFromInformed", timeAsInformed);
				}
			}
		}
		else
		{
			HandlePheromoneSteering();
		}
	}

	void TransitionFromInformed()
	{
		if (targetTorus == null || currentState != State.Informed)
		{
			return;
		}

		nOfStates["informed"]--;

		// Determine whether to pull or lift based on position relative to nest
		bool shouldPull = ShouldPullRatherThanLift();

		if (shouldPull)
		{
			// Pull - ant believes it's closer to home than the torus
			currentState = State.Pulling;
			nOfStates["puller"]++;
			Debug.Log("Transitioning to Pulling state");
		}
		else
		{
			// Lift -// Lift - ant believes it's in a better position to reduce friction
			currentState = State.Lifting;
			nOfStates["lifter"]++;
			Debug.Log("Transitioning to Lifting state");
		}

		// Set next role assessment time
		lastRoleAssessmentTime = Time.time + roleStabilityTime;
	}

	// Helper method to determine if ant should pull rather than lift
	bool ShouldPullRatherThanLift()
	{
		// If there are too few pullers, we need more
		int totalAntsOnTorus = nOfStates["puller"] + nOfStates["lifter"] + nOfStates["informed"];
		float pullerRatio = totalAntsOnTorus > 0 ? (float)nOfStates["puller"] / totalAntsOnTorus : 0;

		if (pullerRatio < 0.4f)
			return true;  // Need more pullers

		if (pullerRatio > 0.8f)
			return false; // Too many pullers, become a lifter

		// Check position relative to torus movement
		if (targetTorus != null && targetTorus.totForce.magnitude > 0.1f)
		{
			// Am I positioned to be an effective puller?
			// Determine if ant is in "front" relative to movement direction
			Vector2 offsetFromTorus = (currentPosition - targetTorus.currentPosition).normalized;
			Vector2 movementDir = targetTorus.totForce.normalized;

			float dot = Vector2.Dot(offsetFromTorus, -movementDir);

			// If positioned in front of torus (opposite to movement), pull
			if (dot > Find)
			{
				return true;
			}

			// If positioned perpendicular to movement, lift
			if (Mathf.Abs(dot) < 0.3f)
			{
				return false;
			}
		}

		// Decide based on ant's position and nest direction
		Vector2 homeDirection = FindHomeDirection();
		Vector2 antOffset = (currentPosition - targetTorus.currentPosition).normalized;

		// If ant is on the side facing home, it's a good puller position
		float homeAlignment = Vector2.Dot(antOffset, -homeDirection);
		return homeAlignment > 0.2f;
	}

	// Periodically reassess role when attached to torus
	void RoleReassessment()
	{
		if (targetTorus == null || currentState == State.SearchingForFood || currentState == State.ReturningHome)
			return;

		// Only reassess after stability period
		if (Time.time < lastRoleAssessmentTime + roleStabilityTime)
			return;

		// Update last assessment time
		lastRoleAssessmentTime = Time.time + roleStabilityTime;

		// Keep track of current role for transition logging
		State previousState = currentState;

		// Calculate transition probabilities based on current forces and roles
		float pullerTransitionProb = 0.1f;
		float lifterTransitionProb = 0.1f;

		if (currentState == State.Pulling)
		{
			// Calculate how effective the ant's pull is
			float pullEffectiveness = Vector2.Dot(pullingForce.normalized, targetTorus.totForce.normalized);

			// If pull is not aligned with total force, higher chance to switch
			if (pullEffectiveness < 0.5f)
			{
				pullerTransitionProb = Kc * (1f - pullEffectiveness);
			}

			// Check if we should transition
			if (Random.value < pullerTransitionProb)
			{
				nOfStates["puller"]--;
				currentState = State.Lifting;
				nOfStates["lifter"]++;
				Debug.Log("Reassessed: Changed from Pulling to Lifting");
			}
		}
		else if (currentState == State.Lifting)
		{
			// Check if we're positioned well to be a puller
			bool betterAsPuller = ShouldPullRatherThanLift();

			// Adjust transition probability based on position and current needs
			if (betterAsPuller)
			{
				lifterTransitionProb = Kc * 1.2f;
			}

			// Check if we should transition
			if (Random.value < lifterTransitionProb)
			{
				nOfStates["lifter"]--;
				currentState = State.Pulling;
				nOfStates["puller"]++;
				Debug.Log("Reassessed: Changed from Lifting to Pulling");
			}
		}
		else if (currentState == State.Informed)
		{
			// The TransitionFromInformed method handles this case
		}
	}

	void HandleRandomSteering()
	{
		if (Time.time > nextRandomSteerTime)
		{
			randomSteerForce = Random.insideUnitCircle * settings.randomSteerStrength;
			nextRandomSteerTime = Time.time + settings.timeBetweenRandomSteers * Random.Range(0.5f, 1.5f);
		}
	}

	void HandlePheromoneSteering()
	{
		if (Time.time < nextDirUpdateTime) return;
		nextDirUpdateTime = Time.time + settings.timeBetweenDirUpdate;

		int numPheromones = 0;
		if (currentState == State.SearchingForFood)
		{
			numPheromones = colony.homeToFoodMap.GetMap().GatherNearbyNonZero(currentPosition, settings.perceptionRadius, pheromoneEntries);
		}
		else if (currentState == State.ReturningHome)
		{
			numPheromones = colony.foodToHomeMap.GetMap().GatherNearbyNonZero(currentPosition, settings.perceptionRadius, pheromoneEntries);
		}

		Vector2 desiredDir = currentForwardDir;

		if (numPheromones > 0)
		{
			// Get direction from highest concentration pheromone
			if (settings.followHighestConcentration)
			{
				int highestValueIndex = 0;
				float highestValue = 0;

				for (int i = 0; i < numPheromones; i++)
				{
					if (pheromoneEntries[i].value > highestValue)
					{
						highestValue = pheromoneEntries[i].value;
						highestValueIndex = i;
					}
				}

				Vector2 highestPheromoneDir = pheromoneEntries[highestValueIndex].pos - currentPosition;
				if (highestPheromoneDir != Vector2.zero)
				{
					desiredDir = highestPheromoneDir.normalized;
				}
			}
			// Average direction from all pheromones
			else
			{
				Vector2 sumDir = Vector2.zero;
				float weightSum = 0;

				for (int i = 0; i < numPheromones; i++)
				{
					Vector2 pheromoneDir = pheromoneEntries[i].pos - currentPosition;
					if (pheromoneDir != Vector2.zero)
					{
						float weight = pheromoneEntries[i].value;
						sumDir += pheromoneDir.normalized * weight;
						weightSum += weight;
					}
				}

				if (weightSum > 0)
				{
					desiredDir = (sumDir / weightSum).normalized;
				}
			}

			pheromoneSteerForce = desiredDir * settings.targetSteerStrength;
		}
		else
		{
			pheromoneSteerForce = Vector2.zero;
		}
	}

	void HandlePheromonePlacement()
	{
		float dstFromLastPheromone = ((Vector2)transform.position - lastPheromonePos).magnitude;
		if (dstFromLastPheromone < settings.pheromoneSpacing) return;

		lastPheromonePos = transform.position;

		float timeSinceLeftHomeOrFood = 0;

		// Don't place pheromones if very close to source
		if (Vector2.Distance(transform.position, homePos) < colony.radius)
		{
			return;
		}

		// Place home to food pheromones when searching
		if (currentState == State.SearchingForFood)
		{
			timeSinceLeftHomeOrFood = Mathf.Min(Time.time - leftHomeTime, settings.maxPheromoneStrength);
			float pheromoneStrength = timeSinceLeftHomeOrFood / settings.maxPheromoneStrength;
			colony.homeToFoodMap.AddPheromone(transform.position, pheromoneStrength);
		}
		// Place food to home pheromones when returning
		else if (currentState == State.ReturningHome)
		{
			timeSinceLeftHomeOrFood = Mathf.Min(Time.time - leftFoodTime, settings.maxPheromoneStrength);
			float pheromoneStrength = timeSinceLeftHomeOrFood / settings.maxPheromoneStrength;
			colony.foodToHomeMap.AddPheromone(transform.position, pheromoneStrength);
		}
	}

	void OnDrawGizmos()
	{
		if (settings.showDebugGizmos)
		{
			Gizmos.color = Color.blue;
			Gizmos.DrawWireSphere(transform.position, settings.perceptionRadius);

			if (targetTorus != null &&
			   (currentState == State.Pulling || currentState == State.Lifting))
			{
				Gizmos.color = (currentState == State.Pulling) ? Color.red : Color.green;
				Gizmos.DrawLine(transform.position, transform.position + (Vector3)pullingForce);

				// Show connection to torus
				Gizmos.color = Color.yellow;
				Gizmos.DrawLine(transform.position, targetTorus.transform.position);
			}

			// Debug state visualization
			if (currentState == State.SearchingForFood)
				Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
			else if (currentState == State.ReturningHome)
				Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
			else if (currentState == State.Informed)
				Gizmos.color = new Color(0.6f, 0.6f, 1f, 0.5f);
			else if (currentState == State.Pulling)
				Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
			else if (currentState == State.Lifting)
				Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.5f);

			Gizmos.DrawSphere(transform.position, 0.1f);
		}
	}

	// For external systems to check what state the ant is in
	public State GetCurrentState()
	{
		return currentState;
	}

	// Called when object is being destroyed
	void OnDestroy()
	{
		// Decrement state counters when ant is destroyed
		if (currentState == State.Informed)
			nOfStates["informed"]--;
		else if (currentState == State.Pulling)
			nOfStates["puller"]--;
		else if (currentState == State.Lifting)
			nOfStates["lifter"]--;
	}

	// Update is called once per frame, but we may need additional logic on a fixed timestep
	void FixedUpdate()
	{
		// Re-evaluate role choice at regular intervals when attached to torus
		if (targetTorus != null &&
		   (currentState == State.Informed || 
		   currentState == State.Pulling ||
		   currentState == State.Lifting))
		{
			RoleReassessment();
		}
	}

}  */


// ANT CLASS
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

	public float Kc = 0.2f;              // From paper 
	public float Find = 1f;         // From paper
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
			PushPullTorus();
		}
		else
		{
			HandleMovement();
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

		// If we're in an active state (informed, pulling, lifting)
		if (currentState == State.Informed || currentState == State.Pulling || currentState == State.Lifting)
		{
			// Maintain position around the torus perimeter
			Vector2 newOffset = new Vector2(
				Mathf.Cos(relativeAngleToTorus),
				Mathf.Sin(relativeAngleToTorus)
			) * outerRadius;

			// Set position relative to torus
			currentPosition = torusPos + newOffset;
			transform.position = currentPosition;

			// Orient ant facing toward or away from torus based on state
			Vector2 directionToFace;
			if (currentState == State.Pulling)
			{
				// When pulling, face away from torus (toward estimated nest direction)
				directionToFace = -newOffset.normalized;
			}
			else
			{
				// When informed or lifting, face toward torus center
				directionToFace = newOffset.normalized;
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

	void PushPullTorus()
	{
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Lifting))
		{
			return;
		}

		// Calculate direction vector from torus to ant
		Vector2 offset = currentPosition - targetTorus.currentPosition;
		Vector2 direction = offset.normalized;

		if (currentState == State.Pulling)
		{
			// When pulling, apply force away from the ant's position (toward nest)
			// Find direction toward home if possible
			Vector2 homeDirection = FindHomeDirection();

			// Blend home direction with ant-torus direction for more natural movement
			Vector2 pullDirection = Vector2.Lerp(homeDirection, -direction, 0.3f).normalized;
			pullingForce = pullDirection * settings.collisionAvoidSteerStrength * 0.8f;
		}
		else if (currentState == State.Lifting)
		{
			// When lifting, reduce friction by applying a small upward force
			// This simulates the ant helping to lift the torus
			pullingForce = Vector2.up * settings.collisionAvoidSteerStrength * 0.5f;
		}

		// Apply the force to the torus
		targetTorus.ApplyForce(pullingForce);
	}

	Vector2 FindHomeDirection()
	{
		// Try to locate the home
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);
		if (home)
		{
			// Return direction toward home
			return ((Vector2)home.transform.position - currentPosition).normalized;
		}

		// If home not in perception range, use the direction the ant came from
		// (Ants would typically remember the general direction they came from)
		if (currentState == State.Pulling && Time.time - torusAttachmentTime < 10f)
		{
			// Use the ant's initial orientation when it first attached to the torus
			// This simulates the ant remembering where it came from
			return -hitRelativeToTorus.normalized;
		}

		// Fallback: use a random direction with slight bias toward home position
		return ((homePos - currentPosition).normalized + Random.insideUnitCircle * 0.5f).normalized;
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
					// Large food (torus) requires collaboration
					Debug.Log("Transitioning to Informed state");
					targetFood.gameObject.layer = 0;
					currentState = State.Informed;
					nOfStates["informed"]++;

					// Store current relative position to torus
					hitRelativeToTorus = currentPosition - targetTorus.currentPosition;

					// Calculate angle for positioning around torus
					Vector2 offset = currentPosition - targetTorus.currentPosition;
					relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x);

					// Store when we attached to the torus
					torusAttachmentTime = Time.time;

					// Stop normal movement
					currentVelocity = Vector2.zero;

					// Schedule transition after assessment period
					float timeAsInformed = 10f;
					Invoke("TransitionFromInformed", timeAsInformed);
				}
			}
		}
		else
		{
			HandlePheromoneSteering();
		}
	}

	void TransitionFromInformed()
	{
		if (targetTorus == null || currentState != State.Informed)
		{
			return;
		}

		nOfStates["informed"]--;

		// Determine whether to pull or lift based on position relative to nest
		bool shouldPull = ShouldPullRatherThanLift();

		if (shouldPull)
		{
			// Pull - ant believes it's closer to home than the torus
			currentState = State.Pulling;
			nOfStates["puller"]++;
			Debug.Log("Transitioning to Pulling state");
		}
		else
		{
			// Lift - ant believes lifting would be more effective
			currentState = State.Lifting;
			nOfStates["lifter"]++;
			Debug.Log("Transitioning to Lifting state");
		}

		// Schedule role reassessment
		float reassessmentRate = 10f;
		Invoke("MaybeSwitchRoles", reassessmentRate);
	}

	bool ShouldPullRatherThanLift()
	{
		// Try to detect if ant is between nest and torus
		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius * 2, homeMask);
		if (home)
		{
			Vector2 homePos = home.transform.position;
			float distanceAntHome = ((Vector2)homePos - currentPosition).magnitude;
			float distanceTorusHome = ((Vector2)homePos - targetTorus.currentPosition).magnitude;

			// If ant is closer to home than torus is, pulling makes more sense
			return distanceAntHome < distanceTorusHome;
		}

		// If home not visible, make probabilistic decision 
		// Based on initial approach direction (ants should remember where they came from)
		Vector2 approachDirection = -hitRelativeToTorus.normalized;
		Vector2 torusForceDirection = targetTorus.totForce.normalized;

		// Dot product to determine if ant's approach aligns with current torus movement
		float alignmentWithMovement = Vector2.Dot(approachDirection, torusForceDirection);

		// If torus is already moving in direction ant came from, lifting is more useful
		// Otherwise, pulling might help more
		if (alignmentWithMovement > 0.3f)
		{
			return false; // Lift
		}
		else if (alignmentWithMovement < -0.3f)
		{
			return true; // Pull
		}
		else
		{
			// Not strongly aligned or opposed - random with bias toward pulling
			return Random.value < 0.6f;
		}
	}

	void MaybeSwitchRoles()
	{
		if (targetTorus == null || (currentState != State.Pulling && currentState != State.Lifting))
		{
			return;
		}

		// Get normalized force directions
		Vector2 pullDirection = pullingForce.normalized;
		Vector2 totalForceDirection = targetTorus.totForce.normalized;

		// Calculate alignment between ant's pull and total force (dot product)
		float alignment = Vector2.Dot(pullDirection, totalForceDirection);
		float exponent = alignment / Find;

		float switchRate;

		if (currentState == State.Pulling)
		{
			// Calculate probability of Puller → Lifter transition
			// Lower probability if pull is aligned with overall movement
			switchRate = Kc * Mathf.Exp(-exponent);
		}
		else if (currentState == State.Lifting && targetTorus.totForce.magnitude > 0.1f)
		{
			// Calculate probability of Lifter → Puller transition
			// Higher probability if pull would be aligned with overall movement
			switchRate = Kc * Mathf.Exp(exponent);
		}
		else
		{
			// Default Lifter → Puller rate when torus isn't moving much
			switchRate = Kc * Mathf.Exp(-exponent);
		}

		// Scale rate by time delta to make it per-frame probability
		float frameRate = switchRate * Time.deltaTime * 10f; // Adjusted to make switches more likely

		// Probabilistic role switch
		if (Random.value < frameRate)
		{
			if (currentState == State.Pulling)
			{
				currentState = State.Lifting;
				nOfStates["puller"]--;
				nOfStates["lifter"]++;
				Debug.Log("Switched from Pulling to Lifting");
			}
			else
			{
				currentState = State.Pulling;
				nOfStates["puller"]++;
				nOfStates["lifter"]--;
				Debug.Log("Switched from Lifting to Pulling");
			}
		}

		// Schedule next role reassessment
		float rate = 10f;
		Invoke("MaybeSwitchRoles", rate);
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
			else if (currentState == State.ReturningHome && settings.useFoodMarkers && (Time.time - leftFoodTime) < settings.pheromoneRunOutTime)
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
}



///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

/* using System.Collections;
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

	public float Kc = 0.2f;              // From paper 
	public float Find = 1f;			// From paper
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
		PushPullTorus();
		MoveRelativeToTorus();
		HandleMovement();
	}

	void MoveRelativeToTorus() {
		// if(State.SearchingForFood == currentState || State.ReturningHome == currentState)
		// {	
		// 	torusFollowForce = Vector2.zero;
		// 	return;
		// }

		// Vector2 torusPos = targetTorus.currentPosition;
		// // //Vector2 DesiredPos = torusPos + hitRelativeToTorus;
		// // Vector2 distance = currentPosition - torusPos;
		// // Debug.Log("Distance to torus: " + distance.magnitude);
		// // if(distance.magnitude > targetTorus.radius + 0.4f)
		// // {
		// // 	Debug.Log("Far from Torus");
		// // 	torusFollowForce = -distance.normalized * settings.collisionAvoidSteerStrength;
		// // } else {
		// // 	Debug.Log("Close to Torus");
		// // 	torusFollowForce = distance.normalized * settings.collisionAvoidSteerStrength;
		// // 	// torusFollowForce = -distance.normalized * settings.pheromoneWeight;
		// // 	//torusFollowForce = transform.up * settings.collisionAvoidSteerStrength;
		// // }
		// // //Vector2 offsetToTorus = (DesiredPos - currentPosition).normalized;

		// // Just a try 
		// float radius = targetTorus.radius * 1.05f; // stay slightly outside collider

		// // Keep ant on torus perimeter
		// Vector2 offsetFromTorus = new Vector2(
		// 	Mathf.Cos(relativeAngleToTorus),
		// 	Mathf.Sin(relativeAngleToTorus)
		// ) * radius;

		// currentPosition = torusPos + offsetFromTorus;
		// transform.position = currentPosition;

		void MoveRelativeToTorus()
		{
			if (currentState == State.SearchingForFood || currentState == State.ReturningHome || targetTorus == null)
			{
				torusFollowForce = Vector2.zero;
				return;
			}

			Vector2 torusPos = targetTorus.currentPosition;
			float radius = targetTorus.radius * 1.05f;

			// Stay attached at fixed angle relative to torus center
			Vector2 newOffset = new Vector2(
				Mathf.Cos(relativeAngleToTorus),
				Mathf.Sin(relativeAngleToTorus)
			) * radius;

			// Update position relative to new torus center
			currentPosition = torusPos + newOffset;
			transform.position = currentPosition;
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

	void PushPullTorus()
	{
		if (State.SearchingForFood == currentState || State.ReturningHome == currentState && targetTorus == null)
		{
			return;

		}
		Vector2 distance = currentPosition - targetTorus.currentPosition;
		if (distance.magnitude <= targetTorus.radius) {
			Debug.Log("Pushing torus");
			Vector2 offsetToTorus = distance.normalized;
			pullingForce = offsetToTorus * 0.5f; // Example force calculation
			//Vector2 force =  -distance * settings.collisionAvoidSteerStrength ; // Example force calculation
			targetTorus.ApplyForce(pullingForce);
		}
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
			// int numFoodInRadius = Physics2D.OverlapCircleNonAlloc(perceptionCentre.position, settings.perceptionRadius, foodColliders, foodMask);
			// if (numFoodInRadius > 0)
			// {
			// 	targetFood = foodColliders[Random.Range(0, numFoodInRadius)].transform;
			// 	if (targetFood.CompareTag("SmallFood"))
			// 	{
			// 		targetFood.gameObject.layer = 0;
			// 	}
			// }
			int numFoodInRadius = Physics2D.OverlapCircleNonAlloc(perceptionCentre.position, settings.perceptionRadius, foodColliders, torusMask);
			if (numFoodInRadius > 0)
			{
				Collider2D foodCollider = foodColliders[Random.Range(0, numFoodInRadius)];
				targetTorus = foodCollider.GetComponent<Torus>();
				if (targetTorus != null)
				{
					Debug.Log("Found torus" + targetTorus.currentPosition);
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
			if (dstToFood < targetTorus.radius * 1f)
			{
				Debug.Log("Collected food");
				if (targetFood.CompareTag("SmallFood"))
				{
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
					Debug.Log("Informed");
					targetFood.gameObject.layer = 0;
					currentState = State.Informed;
					nOfStates["informed"]++;
					currentVelocity = Vector2.zero;
					hitRelativeToTorus = currentPosition - targetTorus.currentPosition;
					float timeAsInformed = 10f;
					Invoke("TransitionFromInformed", timeAsInformed);
					Vector2 offset = currentPosition - targetTorus.currentPosition;
					relativeAngleToTorus = Mathf.Atan2(offset.y, offset.x); // store this angle
				}
			}
		}
		else
		{
			HandlePheromoneSteering();
		}

	}

	void TransitionFromInformed()
	{
		if (targetTorus == null || currentState != State.Informed)
		{
			return;
		}

		nOfStates["informed"]--;

		float fuctor;

		Collider2D home = Physics2D.OverlapCircle(perceptionCentre.position, settings.perceptionRadius, homeMask);
		if (home)
		{
			float distanceAntHome = ((Vector2)home.transform.position - currentPosition).magnitude;
			float distanceTorusHome = ((Vector2)home.transform.position - targetTorus.currentPosition).magnitude;
			fuctor = distanceAntHome - distanceTorusHome;
		}
		else
		{
			fuctor = Random.Range(-1f, 1f);
		}

		if (fuctor < 0f)
		{
			// pull
			currentState = State.Pulling;
			nOfStates["puller"]++;
		}
		else
		{
			// lift
			currentState = State.Lifting;
			nOfStates["lifter"]++;
		}

		float rate = 10f;
		Invoke("MaybeSwitchRoles", rate);
	}

void MaybeSwitchRoles()
{
	// unit vector
	Vector2 pullDirection = pullingForce.normalized;

	float alignment = Vector2.Dot(pullDirection, targetTorus.totForce.normalized); // dot product
	float exponent = alignment / Find;

	float switchRate;

	if (currentState == State.Pulling)
	{
		switchRate = Kc * Mathf.Exp(-exponent); // Lifter → Puller
	}
	else if (currentState == State.Lifting && targetTorus.totForce.magnitude > 0.1f)
	{
		switchRate = Kc * Mathf.Exp(exponent); // Puller → Lifter
	}
	else if (currentState == State.Lifting)
	{
		switchRate = Kc * Mathf.Exp(-exponent); // Lifter → Puller
	}
	else return;

	// Switch probabilistically
	if (Random.value < switchRate * Time.deltaTime) // Normalize rate to per-frame
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
			nOfStates["puller"]++;
			nOfStates["lifter"]--;
		}
	}

	float rate = 10f;
	Invoke("MaybeSwitchRoles", rate);
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
			else if (currentState == State.ReturningHome && settings.useFoodMarkers && (Time.time - leftFoodTime) < settings.pheromoneRunOutTime)
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
		if(currentState == State.Informed)
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
			if (State.Informed == currentState)
			{
				// in this state, the ant is trying to bring the food back to the nest - handled in 
				// Different code.
				// no need for pheromone steering
				return;
			}
			else
			{
				// centre
				sensors[centreIndex] = currentPosition + currentForwardDir * settings.sensorDst;
				// left
				sensors[leftIndex] = currentPosition + leftSensorDir * settings.sensorDst;
				// right
				sensors[rightIndex] = currentPosition + rightSensorDir * settings.sensorDst;
			}


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
					//strength = strength * strength;
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
		if(currentState == State.Informed)
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
} */