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
}