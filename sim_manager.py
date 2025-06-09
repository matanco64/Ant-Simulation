import subprocess
import sys
from dataclasses import dataclass, asdict
import pydantic
import json
import matplotlib.pyplot as plt
import numpy as np

@dataclass
class SimulationParameters:
    antCount: int = 100
    simulationSpeed: float = 40.0
    infromedTime: float = 5.0
    stopAfterSeconds: int = 120 
    kc: int = 0.3

   


class Point(pydantic.BaseModel):
    x: float
    y: float

# use pydantic instead
class SimulationResultsPydantic(pydantic.BaseModel):
    runId: str
    duration: float
    didSucceed: bool
    timeScale: float
    antCount: int
    torusPositions: list[Point]
    ant_colony_position: Point
    


SIMULATION_RESULTS_FILE = "sim_output.json"
PARAMETERS_FILE = "sim_parameters.json"

BUILD_EXE = r"Build\Ant Simulation.exe"

def run_unity_build(build_path, params: SimulationParameters, headless=True):
    # Save parameters to a JSON file
    with open(PARAMETERS_FILE, 'w') as f:
        json.dump(asdict(params), f, indent=4)
    args = ["-filename", PARAMETERS_FILE]
    if headless:
        args += ["-batchmode", "-nographics", "-quit"]
    cmd = [build_path] + args

    print(f"Running: {' '.join(cmd)}")
    process = subprocess.Popen(cmd, shell=False)
    process.wait()
    return process.returncode

def run_simulation() -> SimulationResultsPydantic | None:
    parameters = SimulationParameters()
    successs =run_unity_build(BUILD_EXE, parameters, headless=False)
    if successs != 0:
        print(f"Simulation failed with exit code {successs}")
        sys.exit(successs)

    #load results from the output file
    try:
        with open(SIMULATION_RESULTS_FILE, 'r') as f:
            results_data = f.read()
            return SimulationResultsPydantic.model_validate_json(results_data)
            
    except FileNotFoundError:
        print(f"Results file {SIMULATION_RESULTS_FILE} not found.")
    except json.JSONDecodeError as e:
        print(f"Error decoding JSON from results file: {e}")
    
    
    
if __name__ == "__main__":
    for i in range(10):
        print(f"Running simulation iteration {i+1}")
        results = run_simulation()
        if not results:
            print("Simulation did not return results. Exiting.")
            sys.exit(1)
            
        torus_positions = np.array([[p.x, p.y] for p in results.torusPositions])
        plt.scatter(torus_positions[:, 0], torus_positions[:, 1], s=1, label=f"Run {i+1}")
    plt.title(f"Ant Simulation Results over multiple Runs\n")
    plt.xlabel("X Position")
    plt.ylabel("Y Position")
    plt.grid(True)
    plt.show()
    
# import time

# if __name__ == "__main__":
#     ant_counts = [20, 40, 60, 80, 100, 120, 140, 160]
#     spreads = []
#     displacements = []
#     run_times = []

#     plt.figure(figsize=(10, 8))

#     for i, ant_count in enumerate(ant_counts):
#         print(f"\nRunning simulation with {ant_count} ants...")

#         params = SimulationParameters(
#             ant_count=ant_count,
#             simulation_speed=40.0,
#             stop_after_seconds=120
#         )

#         start_time = time.time()
#         success = run_unity_build(BUILD_EXE, params, headless=True)
#         run_duration = time.time() - start_time
#         run_times.append(run_duration)

#         if success != 0:
#             print(f"Simulation failed with exit code {success}")
#             spreads.append(None)
#             displacements.append(None)
#             continue

#         try:
#             with open(SIMULATION_RESULTS_FILE, 'r') as f:
#                 results_data = f.read()
#                 results = SimulationResultsPydantic.model_validate_json(
#                     results_data)
#         except Exception as e:
#             print(f"Error loading results: {e}")
#             spreads.append(None)
#             displacements.append(None)
#             continue

#         torus_positions = np.array([[p.x, p.y]
#                                    for p in results.torusPositions])
#         center = np.mean(torus_positions, axis=0)
#         spread = np.std(torus_positions, axis=0).mean()
#         displacement = np.linalg.norm(center)

#         spreads.append(spread)
#         displacements.append(displacement)

#         plt.subplot(3, 1, 1)
#         plt.scatter(
#             torus_positions[:, 0], torus_positions[:, 1], s=1, label=f"{ant_count} ants")

#     # Torus Position Scatter Plot
#     plt.subplot(3, 1, 1)
#     plt.title("Torus Final Positions (per ant count)")
#     plt.xlabel("X Position")
#     plt.ylabel("Y Position")
#     plt.legend()
#     plt.grid(True)

#     # Spread Plot
#     plt.subplot(3, 1, 2)
#     plt.plot(ant_counts, spreads, marker='o', color='tab:blue')
#     plt.title("Spread (Standard Deviation) vs Ant Count")
#     plt.xlabel("Ant Count")
#     plt.ylabel("Average Position Spread")
#     plt.grid(True)

#     # Displacement Plot
#     plt.subplot(3, 1, 3)
#     plt.plot(ant_counts, displacements, marker='s', color='tab:orange')
#     plt.title("Center Displacement from Origin vs Ant Count")
#     plt.xlabel("Ant Count")
#     plt.ylabel("Displacement from Origin")
#     plt.grid(True)

#     plt.tight_layout()
#     plt.show()
