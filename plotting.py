from sim_tools import run_simulation, SimulationParameters, run_simulations_batch, load_results_from_directory, SimulationData
import matplotlib.pyplot as plt
import numpy as np
import random
from tqdm import tqdm


num_sim = 50
steering_strategies = 6

Steering_names = [
    "home",
    "circle",
    "gradient",
    "gaussian",
    "Inverse Square",
    "Exponential",
]


def plot_success_rates(batch_results: list, num_strategies=6, strategy_names=None, summary_text=None):
    # Count successes for each strategy
    success_counts = [0] * num_strategies
    total_counts = [0] * num_strategies

    for params, res in batch_results:
        strategy = params.usePheromoneSteering
        if 0 <= strategy < num_strategies:
            total_counts[strategy] += 1
            if res and res.didSucceed:
                success_counts[strategy] += 1

    # Calculate success rates
    success_rates = [
        (success_counts[i] / total_counts[i]) if total_counts[i] else 0
        for i in range(num_strategies)
    ]

    # Prepare labels
    if strategy_names is None:
        labels = [f"Steering={i}" for i in range(num_strategies)]
    else:
        labels = [strategy_names[i] if i < len(
            strategy_names) else f"Steering={i}" for i in range(num_strategies)]

    # Bar colors for visual interest
    colors = ["#85bcdd", "#e59b67", "#90ca92", "#a4a2c4", "#e294cb", "#e1e182"]

    plt.figure(figsize=(12, 7))
    bars = plt.bar(range(num_strategies), success_rates,
                   tick_label=labels, color=colors)
    plt.ylabel("Success Rate")
    plt.title("Success Rate by Steering Strategy")
    plt.ylim(0, 1.05)  # Just a little space above bars

    plt.grid(axis='y', linestyle='--', alpha=0.6)

    # Annotate bars with percentage and counts
    for i, bar in enumerate(bars):
        height = bar.get_height()
        plt.text(bar.get_x() + bar.get_width()/2, height + 0.025,
                 f"{success_rates[i]*100:.0f}%\n({success_counts[i]}/{total_counts[i]})",
                 ha='center', va='bottom', fontsize=10, fontweight='bold')

    # Add summary text box BELOW the plot area
    if summary_text:
        plt.gcf().subplots_adjust()  # Make space at the bottom
        plt.gcf().text(
            0.5, 0.02, summary_text,
            fontsize=11, va='center', ha='center',
            bbox=dict(facecolor='white', alpha=0.85, edgecolor='gray')
        )

    plt.tight_layout()
    plt.show()

    # Print summary
    for i in range(num_strategies):
        print(
            f"{labels[i]}: Success Rate={success_rates[i]:.2%} ({success_counts[i]}/{total_counts[i]})")



def plot_torus_positions_by_strategy(batch_results, strategy_names=None):
    """
    Plots the torus positions for each strategy in separate graphs.
    Each plot shows the paths for all successful runs of that strategy.
    """
    if strategy_names is None:
        strategy_names = [f"Steering={i}" for i in range(steering_strategies)]

    for strategy in range(steering_strategies):
        # Collect all successful torus positions for this strategy
        paths = [
            res.torusPositions for params, res in batch_results
            if params.usePheromoneSteering == strategy and res and res.didSucceed
        ]
        if not paths:
            continue  # Skip if no successful runs

        plt.figure(figsize=(6, 6))
        for path in paths:
            xs = [p.x for p in path]
            ys = [p.y for p in path]
            plt.plot(xs, ys, alpha=0.5)
        plt.title(f"Torus Paths - {strategy_names[strategy]}")
        plt.xlabel("X")
        plt.ylabel("Y")
        plt.grid(True)
        plt.axis("equal")
        plt.show()


def plot_duration_comparison(batch_results, strategy_names=None, summary_text=None):
    """
    Plots a boxplot comparing the durations (time to win) for each strategy.
    Only successful runs are included.
    """
    # Collect durations for each strategy
    durations_by_strategy = [[] for _ in range(steering_strategies)]
    for params, res in batch_results:
        if res and res.didSucceed:
            durations_by_strategy[params.usePheromoneSteering].append(
                res.duration)

    # Prepare labels and colors
    if strategy_names is None:
        labels = [f"Steering={i}" for i in range(steering_strategies)]
    else:
        labels = [strategy_names[i] if i < len(
            strategy_names) else f"Steering={i}" for i in range(steering_strategies)]
    colors = ["#85bcdd", "#e59b67", "#90ca92", "#a4a2c4", "#e294cb", "#e1e182"]

    # Plot boxplot with colored boxes
    plt.figure(figsize=(12, 7))
    box = plt.boxplot(durations_by_strategy,
                      patch_artist=True, tick_labels=labels)
    for patch, color in zip(box['boxes'], colors):
        patch.set_facecolor(color)
        patch.set_alpha(0.7)

    plt.ylabel("Duration (seconds)")
    plt.title("Time to Win by Strategy (Successful Runs Only)")
    plt.grid(True, axis='y', linestyle='--', alpha=0.6)
    plt.ylim(bottom=0)

    # Annotate with average duration in black, above each box at the same relative position
    for i, data in enumerate(durations_by_strategy):
        if data:
            avg = np.mean(data)
            # Get the box's top y-value (whisker or box)
            y_max = max(box['whiskers'][2*i].get_ydata().max(),
                        box['whiskers'][2*i+1].get_ydata().max())
            plt.text(i+1, y_max + 0.05*(plt.ylim()[1] - plt.ylim()[0]), f"{avg:.0f}s",
                     ha='center', va='bottom', fontsize=11, fontweight='bold', color='black')

    # Add summary text box BELOW the plot area
    if summary_text:
        plt.gcf().subplots_adjust(bottom=0.25)
        plt.gcf().text(
            0.5, 0.02, summary_text,
            fontsize=11, va='bottom', ha='center',
            bbox=dict(facecolor='white', alpha=0.85, edgecolor='gray')
        )

    plt.tight_layout()
    plt.show()

    # Print average duration for each strategy
    for i in range(steering_strategies):
        avg = np.mean(
            durations_by_strategy[i]) if durations_by_strategy[i] else float('nan')
        print(f"{labels[i]}: Average Duration = {avg:.2f} seconds")


def compute_path_length(points):
    """Compute total path length from a list of Point objects with x, y attributes."""
    if not points or len(points) < 2:
        return 0
    return sum(
        np.linalg.norm([points[i+1].x - points[i].x,
                        points[i+1].y - points[i].y])
        for i in range(len(points)-1)
    )


def plot_path_length_comparison(batch_results, strategy_names=None, summary_text=None):
    """
    Plots a boxplot comparing the total path length for each strategy.
    Only successful runs are included.
    """
    # Use global steering_strategies if available, else infer
    try:
        num_strategies = steering_strategies
    except NameError:
        num_strategies = max(
            params.usePheromoneSteering for params, _ in batch_results) + 1

    # Collect path lengths for each strategy
    path_lengths_by_strategy = [[] for _ in range(num_strategies)]
    for params, res in batch_results:
        if res and getattr(res, "didSucceed", False) and hasattr(res, "torusPositions"):
            path_length = compute_path_length(res.torusPositions)
            path_lengths_by_strategy[params.usePheromoneSteering].append(
                path_length)

    # Prepare labels and colors
    if strategy_names is None:
        labels = [f"Steering={i}" for i in range(num_strategies)]
    else:
        labels = [strategy_names[i] if i < len(
            strategy_names) else f"Steering={i}" for i in range(num_strategies)]
    colors = ["#85bcdd", "#e59b67", "#90ca92", "#a4a2c4", "#e294cb", "#e1e182"]

    # Plot boxplot with colored boxes
    plt.figure(figsize=(12, 7))
    box = plt.boxplot(path_lengths_by_strategy,
                      patch_artist=True, tick_labels=labels)
    for patch, color in zip(box['boxes'], colors):
        patch.set_facecolor(color)
        patch.set_alpha(0.7)

    plt.ylabel("Total Path Length")
    plt.title("Torus Path Length by Strategy (Successful Runs Only)")
    plt.grid(True, axis='y', linestyle='--', alpha=0.6)
    plt.ylim(bottom=0)

    # Annotate with average path length in black, above each box at the same relative position
    for i, data in enumerate(path_lengths_by_strategy):
        if data:
            avg = np.mean(data)
            # Get the box's top y-value (whisker or box)
            y_max = max(box['whiskers'][2*i].get_ydata().max(),
                        box['whiskers'][2*i+1].get_ydata().max())
            plt.text(i+1, y_max + 0.05*(plt.ylim()[1] - plt.ylim()[0]), f"{avg:.0f}",
                     ha='center', va='bottom', fontsize=11, fontweight='bold', color='black')

    # Add summary text box BELOW the plot area
    if summary_text:
        plt.gcf().subplots_adjust(bottom=0.25)
        plt.gcf().text(
            0.5, 0.02, summary_text,
            fontsize=11, va='bottom', ha='center',
            bbox=dict(facecolor='white', alpha=0.85, edgecolor='gray')
        )

    plt.tight_layout()
    plt.show()

    # Print average path length for each strategy
    for i in range(num_strategies):
        avg = np.mean(
            path_lengths_by_strategy[i]) if path_lengths_by_strategy[i] else float('nan')
        print(f"{labels[i]}: Average Path Length = {avg:.2f}")


def compute_turning_angles(points):
    """Compute average signed turning angle (in degrees) between consecutive segments of a path."""
    if len(points) < 3:
        return 0.0

    # Extract coordinates as arrays
    coords = np.array([[p.x, p.y] for p in points])

    # Compute vectors between consecutive points
    v1 = coords[1:-1] - coords[:-2]  # Segment before
    v2 = coords[2:] - coords[1:-1]   # Segment after

    # Normalize to avoid division by zero
    norms1 = np.linalg.norm(v1, axis=1)
    norms2 = np.linalg.norm(v2, axis=1)
    valid = (norms1 > 1e-8) & (norms2 > 1e-8)

    v1 = v1[valid]
    v2 = v2[valid]

    # Compute signed angles using arctangent of cross and dot product
    cross = v1[:, 0]*v2[:, 1] - v1[:, 1]*v2[:, 0]  # 2D cross product
    dot = np.einsum('ij,ij->i', v1, v2)

    angles = np.arctan2(cross, dot)  # signed turning angle in radians
    return angles * (180.0 / np.pi)  # convert to degrees

# np.degrees(np.mean(np.abs(angles))) if len(angles) > 0 else 0.0


def compute_average_turning_angle(torus_positions):
    """
    Computes the average turning angle (smoothness) of the path defined by torus positions.
    Returns the average angle in degrees.
    """
    if not torus_positions or len(torus_positions) < 3:
        return 0.0  # Not enough points to compute angles

    angles = compute_turning_angles(torus_positions)
    # Average of absolute angles
    return np.mean(np.abs(angles)) if len(angles) > 0 else 0.0


def plot_smoothness_comparison(batch_results, strategy_names=None):
    """
    Plots a bar chart comparing the average turning angle (smoothness) for each strategy.
    Only successful runs are included.
    """
    smoothness_by_strategy = [[] for _ in range(steering_strategies)]
    for params, res in batch_results:
        if res and getattr(res, "didSucceed", False) and hasattr(res, "torusPositions"):
            avg_angle = compute_average_turning_angle(res.torusPositions)
            smoothness_by_strategy[params.usePheromoneSteering].append(
                avg_angle)

    # Compute mean smoothness for each strategy
    mean_smoothness = [
        np.mean(smoothness_by_strategy[i]) if smoothness_by_strategy[i] else float(
            'nan')
        for i in range(steering_strategies)
    ]

    # Prepare labels
    if strategy_names is None:
        labels = [f"Steering={i}" for i in range(steering_strategies)]
    else:
        labels = [strategy_names[i] if i < len(
            strategy_names) else f"Steering={i}" for i in range(steering_strategies)]


def plot_smoothness_comparison(batch_results, strategy_names=None, summary_text=None):
    """
    Plots a bar chart comparing the average turning angle (smoothness) for each strategy.
    Only successful runs are included.
    """
    smoothness_by_strategy = [[] for _ in range(steering_strategies)]
    for params, res in batch_results:
        if res and getattr(res, "didSucceed", False) and hasattr(res, "torusPositions"):
            avg_angle = compute_average_turning_angle(res.torusPositions)
            smoothness_by_strategy[params.usePheromoneSteering].append(
                avg_angle)

    # Compute mean smoothness for each strategy
    mean_smoothness = [
        np.mean(smoothness_by_strategy[i]) if smoothness_by_strategy[i] else float(
            'nan')
        for i in range(steering_strategies)
    ]

    # Prepare labels and colors
    if strategy_names is None:
        labels = [f"Steering={i}" for i in range(steering_strategies)]
    else:
        labels = [strategy_names[i] if i < len(
            strategy_names) else f"Steering={i}" for i in range(steering_strategies)]
    colors = ["#85bcdd", "#e59b67", "#90ca92", "#a4a2c4", "#e294cb", "#e1e182"]

    # Plot
    plt.figure(figsize=(12, 7))
    bars = plt.bar(range(steering_strategies), mean_smoothness,
                   tick_label=labels, color=colors, alpha=0.85)
    plt.ylabel("Average Turning Angle (degrees)")
    plt.title("Route Smoothness by Strategy (Lower is Smoother)")
    plt.ylim(bottom=0)
    plt.grid(axis='y', linestyle='--', alpha=0.6)

    # Annotate with average turning angle in black, above each bar at the same relative position
    for i, bar in enumerate(bars):
        height = bar.get_height()
        plt.text(bar.get_x() + bar.get_width()/2, height + 0.01*(plt.ylim()[1] - plt.ylim()[0]),
                 f"{height:.1f}°", ha='center', va='bottom', fontsize=11, fontweight='bold', color='black')

    # Add summary text box BELOW the plot area
    if summary_text:
        plt.gcf().subplots_adjust(bottom=0.25)
        plt.gcf().text(
            0.5, 0.02, summary_text,
            fontsize=11, va='bottom', ha='center',
            bbox=dict(facecolor='white', alpha=0.85, edgecolor='gray')
        )

    plt.tight_layout()
    plt.show()

    # Print summary
    for i in range(steering_strategies):
        print(
            f"{labels[i]}: Average Turning Angle = {mean_smoothness[i]:.2f}°")


