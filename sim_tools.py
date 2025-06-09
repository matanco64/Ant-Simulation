import subprocess
import sys
from dataclasses import dataclass, asdict
import pydantic
import json
import numpy as np
from multiprocessing import cpu_count
from multiprocessing.dummy import Pool  # For multithreading

# --- Data Classes ---
@dataclass
class SimulationParameters:
    antCount: int = 100
    simulationSpeed: float = 40.0
    infromedTime: float = 5.0
    stopAfterSeconds: int = 250
    kc: float = 0.3

class Point(pydantic.BaseModel):
    x: float
    y: float

class SimulationResultsPydantic(pydantic.BaseModel):
    runId: str
    duration: float
    didSucceed: bool
    timeScale: float
    antCount: int
    torusPositions: list[Point]
    ant_colony_position: Point

# --- File Paths ---
SIMULATION_RESULTS_FILE = "sim_output.json"
PARAMETERS_FILE = "sim_parameters.json"
BUILD_EXE = r"Build\\Ant Simulation.exe"

# --- Core Functions ---
def run_unity_build(build_path, params: SimulationParameters, parameters_path = PARAMETERS_FILE, headless=True):
    """Run the Unity simulation build with given parameters."""
    with open(parameters_path, 'w') as f:
        json.dump(asdict(params), f, indent=4)
    args = ["-filename", parameters_path]
    if headless:
        args += ["-batchmode", "-nographics", "-quit"]
    cmd = [build_path] + args
    process = subprocess.Popen(cmd, shell=False)
    process.wait()
    return process.returncode

def run_simulation(params: SimulationParameters |  None = None, build_path: str = BUILD_EXE, headless: bool = True) -> SimulationResultsPydantic | None:
    """Run a single simulation and return results as a Pydantic object."""
    if params is None:
        params = SimulationParameters()
    success = run_unity_build(build_path, params, headless=headless)
    if success != 0:
        print(f"Simulation failed with exit code {success}")
        return None
    try:
        with open(SIMULATION_RESULTS_FILE, 'r') as f:
            results_data = f.read()
            return SimulationResultsPydantic.model_validate_json(results_data)
    except FileNotFoundError:
        print(f"Results file {SIMULATION_RESULTS_FILE} not found.")
    except json.JSONDecodeError as e:
        print(f"Error decoding JSON from results file: {e}")
    return None

def _run_simulation_worker(params_dict):
    """Worker for parallel simulation runs."""
    params = SimulationParameters(**params_dict)
    return run_simulation(params)

def run_simulations_batch(param_list, processes=None):
    """
    Run multiple simulations in parallel.
    param_list: list of dicts or SimulationParameters
    processes: number of parallel processes (default: cpu_count())
    Returns: list of SimulationResultsPydantic or None
    """
    if not param_list:
        return []
    # Convert to dicts if needed
    param_dicts = [asdict(p) if isinstance(p, SimulationParameters) else p for p in param_list]
    if processes is None:
        processes = min(int(cpu_count() / 2), len(param_dicts))
    with Pool(processes=processes) as pool:
        results = pool.map(_run_simulation_worker, param_dicts)
    return results


if __name__ == '__main__':

    param_list = [SimulationParameters(antCount=ac) for ac in range(50, 60, 3)]
    batch_results = run_simulations_batch(param_list)

    for i, res in enumerate(batch_results):
        if res:
            print(f"Run {i+1} (antCount={param_list[i].antCount}): Success, duration={res.duration}")
        else:
            print(f"Run {i+1} (antCount={param_list[i].antCount}): Failed")
    