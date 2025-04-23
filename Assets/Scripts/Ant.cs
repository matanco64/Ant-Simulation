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
}


