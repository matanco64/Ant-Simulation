using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;



[System.Serializable]
public class LoadedParameters
{
	public int antCount = 100;
	public float kc = 0.2f;              // Switching rate coefficient (basal decision-making rate)
	public float simulationSpeed = 1f; // Speed of the simulation
	public float informedTime = 0.5f; // Time spent in informed state
	public float stopAfterSeconds = 0f; // Time after which the simulation stops, 0 means no stop

	public LoadedParameters()
	{
		string[] args = Environment.GetCommandLineArgs();
		int filenameIndex = Array.IndexOf(args, "-filename");
		if (filenameIndex >= 0 && filenameIndex < args.Length - 1)
		{
			string filename = args[filenameIndex + 1];
			// Load parameters from file
			Debug.Log("Loading parameters from file: " + filename);
			string json = System.IO.File.ReadAllText(filename);
			JsonUtility.FromJsonOverwrite(json, this);
		}
		else
		{
			Debug.LogWarning("No parameters file specified, using default values.");
		}
	}

}

[CreateAssetMenu()]
public class AntSettings : ScriptableObject
{
	[Header("Movement")]
	public float maxSpeed = 2;
	public float acceleration = 3;
	public float collisionAvoidSteerStrength = 5;
	public float targetSteerStrength = 3;
	public float randomSteerStrength = 0.6f;
	public float randomSteerMaxDuration = 1;
	public float timeBetweenDirUpdate = 0.15f;
	public float collisionRadius = 0.15f;
	public float timeBetweenRandomSteers = 0.5f;

	[Header("Pheromones")]
	public float dstBetweenMarkers = 0.75f;
	public float pheromoneEvaporateTime = 45;
	public float pheromoneRunOutTime = 30;
	public float pheromoneWeight = 1;
	public float perceptionRadius = 2.5f;
	public bool useHomeMarkers = true;
	public bool useFoodMarkers = true;

	[Header("Sensing")]
	public float sensorSize = 0.75f;
	public float sensorDst = 1.25f;
	public float sensorSpacing = 1;
	public float antennaDst = 0.25f;

	[Header("Lifetime")]
	public float lifetime = 150;
	public bool useDeath = false;

	[Header("Forces")]
	public float antForce = 2;
		
	public LoadedParameters loadedParameters;

	public float pheromoneSenseRadius = 2f;

	// add start function to init settings from command line arguments
	private void OnEnable()
	{

		loadedParameters = new LoadedParameters();
	}
	
}