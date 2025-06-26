import functools
import subprocess
import sys
import os
from dataclasses import dataclass, asdict
import pydantic
import json
import numpy as np
from multiprocessing import cpu_count
from concurrent.futures import ThreadPoolExecutor, as_completed
import uuid
from tqdm import tqdm


SIMULATION_RESULTS_FILE = "sim_output.json"
PARAMETERS_FILE = "sim_parameters.json"
BUILD_EXE = r"Build\\Ant Simulation.exe"

SIMULATION_FILES_DIRECTORY = "SimulationFiles"

# --- Data Classes ---
@dataclass
class SimulationParameters:
    antCount: int = 200
    simulationSpeed: float = 40.0
    infromedTime: float = 15.0
    stopAfterSeconds: int = 1000
    kc: float = 0.3
    usePheromoneSteering: int = 0
    HomeSenseRadius: int = 0
    outputFile: str = SIMULATION_RESULTS_FILE


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


SimulationData = tuple[SimulationParameters, SimulationResultsPydantic]


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


def run_simulation(params: SimulationParameters | None = None, 
                   parameters_path = PARAMETERS_FILE,
                   build_path: str = BUILD_EXE, headless: bool = True) -> SimulationResultsPydantic | None:
    """Run a single simulation and return results as a Pydantic object."""
    if params is None:
        params = SimulationParameters()
    success = run_unity_build(build_path, params, parameters_path=parameters_path, headless=headless)
    if success != 0:
        print(f"Simulation failed with exit code {success}")
        return None
    return load_simulation_results(file_path=params.outputFile)


def _run_simulation_worker(params_dict, working_directory: str = SIMULATION_FILES_DIRECTORY):
    """Worker for parallel simulation runs."""
    params = SimulationParameters(**params_dict)
    return run_simulation_with_generated_input_output_files(params, working_directory=working_directory)

def run_simulation_with_generated_input_output_files(param: SimulationParameters | None = None, working_directory: str = SIMULATION_FILES_DIRECTORY):
    """
    Run a simulation with generated input and output files.
    param: SimulationParameters or None (default uses default parameters)
    Returns: SimulationResultsPydantic or None
    """
    if param is None:
        param = SimulationParameters()
    # ensure output directory exists
    os.makedirs(working_directory, exist_ok=True)
    # generate input and output file paths
    sim_id = uuid.uuid4().hex
    input_file = os.path.join(working_directory,  sim_id + "_input.json")
    output_file = os.path.join(working_directory, sim_id + "_output.json")
    param.outputFile = output_file
    results = run_simulation(param, parameters_path=input_file)
    
    
    # remove the input file after simulation
    # if os.path.exists(input_file):
    #     os.remove(input_file)
    # if results is not None and os.path.exists(output_file):
    #     os.remove(output_file)
    
    
    return results


def run_simulations_batch(param_list, working_directory: str = SIMULATION_FILES_DIRECTORY, processes: int | None = None):
    if not param_list:
        return []
    param_dicts = [asdict(p) if isinstance(p, SimulationParameters) else p for p in param_list]
    if processes is None:
        processes = min(int(cpu_count() / 2), len(param_dicts))

    results = []
    with ThreadPoolExecutor(max_workers=processes) as executor:
        futures = [executor.submit(functools.partial(_run_simulation_worker, working_directory=working_directory), param) for param in param_dicts]
        for future in tqdm(as_completed(futures), total=len(futures), desc="Running simulations"):
            results.append(future.result())
    return results

def load_simulation_results(file_path: str = SIMULATION_RESULTS_FILE) -> SimulationResultsPydantic | None:
    """Load simulation results from a JSON file."""
    try:
        with open(file_path, 'r') as f:
            results_data = f.read()
            return SimulationResultsPydantic.model_validate_json(results_data)
    except FileNotFoundError:
        print(f"Results file {file_path} not found.")
    except json.JSONDecodeError as e:
        print(f"Error decoding JSON from results file: {e}")
    return None


def load_results_from_directory(directory: str = SIMULATION_FILES_DIRECTORY) -> list[SimulationData]:
    """Load all simulation results from a directory."""
    results = []
    for filename in os.listdir(directory):
        if filename.endswith("_output.json"):
            file_path = os.path.join(directory, filename)
            input_file_path = os.path.join(directory, filename.replace("_output.json", "_input.json"))
            if os.path.exists(input_file_path):
                with open(input_file_path, 'r') as f:
                    input_p = SimulationParameters(**json.load(f))
                    result = load_simulation_results(file_path)
                    results.append((input_p, result))
    return results


if __name__ == '__main__':

    param_list = [SimulationParameters(antCount=ac) for ac in range(400, 600, 3)]
    batch_results = run_simulations_batch(param_list)

    for i, res in enumerate(tqdm(batch_results)):
        if res:
            print(
                f"Run {i+1} (antCount={param_list[i].antCount}): Success, duration={res.duration}")
        else:
            print(f"Run {i+1} (antCount={param_list[i].antCount}): Failed")
