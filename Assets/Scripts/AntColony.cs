using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AntColony : MonoBehaviour {

	public AntSettings settings;
	public Ant antPrefab;
	public int numToSpawn = 10;
	public Transform antHolder;
	public bool replenishDead;

	public PerceptionMap homeMarkers;
	public PerceptionMap foodMarkers;
	float nextPossibleRespawnTime;
	public float radius;
	public Transform graphic;

	[Header ("Debug")]
	public int numFoodCollected;
	public float timePassed;
	bool hasPrinted10MinMark;
	public TextMesh numFoodUI;
	public float spawnRate = 0.1f;
	public float timeBetweenWaves = 10f;
	public int waveAntSize = 30;

	void Start()
	{
		Random.InitState(System.Environment.TickCount);
		StartCoroutine(SpawnAntsInWaves());
		
		string[] args = System.Environment.GetCommandLineArgs();

        foreach (string arg in args) {
            if (arg.StartsWith("-antCount")) {
                string[] parts = arg.Split(' ');
                if (parts.Length > 1) int.TryParse(parts[1], out numToSpawn);
            }
        }
	
	}

	IEnumerator SpawnAntsInWaves() {
    for (int i = 0; i < numToSpawn / waveAntSize + 1; i++) {
		// Spawn wave of ants
		for (int j = 0; j < waveAntSize; j++) {
			SpawnAnt();
			yield return new WaitForSeconds(spawnRate);
		}
        yield return new WaitForSeconds(timeBetweenWaves); // Wait before spawning next ant
    }
	//  yield return new WaitForSeconds(timeBetweenWaves);
}

	void Update()
	{
		timePassed = Time.timeSinceLevelLoad;
		if (!hasPrinted10MinMark && timePassed > 60 * 10)
		{
			hasPrinted10MinMark = true;
			Debug.Log("Num food collected: " + numFoodCollected);
		}

		int numDead = numToSpawn - antHolder.childCount;
		// if (Time.time > nextPossibleRespawnTime) {
		// 	nextPossibleRespawnTime = Time.time;
		// 	if (numDead > 0 && replenishDead) {
		// 		SpawnAnt ();
		// 	}
		// }
		SimulationManager.instance.UpdateAntCount(antHolder.childCount);
	}

	void SpawnAnt () {
		Ant ant = Instantiate (antPrefab, transform.position, Quaternion.identity, antHolder);
		ant.SetColony (this);
	}

	public void FoodCollected () {
		numFoodCollected++;
		numFoodUI.text = numFoodCollected + "";
	}

	void OnValidate () {
		graphic.transform.localScale = Vector3.one * radius * 2;
	}

}