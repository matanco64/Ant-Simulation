import subprocess
import sys
from dataclasses import dataclass, asdict
import pydantic
import json
import matplotlib.pyplot as plt
import numpy as np

@dataclass
class SimulationParameters:
    ant_count: int = 100
    simulation_speed: float = 40.0
    stop_after_seconds: int = 120 
   
   
def to_camel_case(snake_str):
    parts = snake_str.split('_')
    return parts[0] + ''.join(word.capitalize() for word in parts[1:])

def asdict_camel_case(obj):
    original = asdict(obj)
    return {to_camel_case(k): v for k, v in original.items()}


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
    


SIMULATION_RESULTS_FILE = "sim_output.json"

BUILD_EXE = r"Build\Ant Simulation.exe"

def run_unity_build(build_path, params: SimulationParameters, headless=True):
    args = [f"-{k} {v}" for k, v in asdict_camel_case(params).items()]
    if headless:
        args += ["-batchmode", "-nographics", "-quit"]
    cmd = [build_path] + args

    print(f"Running: {' '.join(cmd)}")
    process = subprocess.Popen(cmd, shell=False)
    process.wait()
    return process.returncode

def run_simulation() -> SimulationResultsPydantic | None:
    parameters = SimulationParameters(
        ant_count=100,
        simulation_speed=40.0,
        stop_after_seconds=120
    )
    successs =run_unity_build(BUILD_EXE, parameters, headless=True)
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
    for i in range(4):
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