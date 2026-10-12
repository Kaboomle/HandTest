"""Analyze instructed static-hold trials. Standard library only; no learned classifier."""
import argparse
import bisect
import csv
import json
import math
import statistics
from pathlib import Path

POSES = ("Open", "Fist", "Victory", "Point", "Pinch", "Gun", "Rock", "ThumbsUp")


LEGACY_POSES = POSES[:-1] + ("Fuck", "ThumbsUp")


def poses_for_metadata(metadata):
    version = metadata.get("settings", {}).get("rulesVersion", 1)
    if not isinstance(version, int) or not 1 <= version <= 6:
        raise ValueError("Unsupported rules version")
    return POSES if version >= 5 else LEGACY_POSES


def finite(value):
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"Non-finite number: {value!r}")
    return number


def percentile(values, fraction):
    if not values:
        return None
    ordered = sorted(values)
    index = (len(ordered) - 1) * fraction
    low, high = math.floor(index), math.ceil(index)
    return ordered[low] + (ordered[high] - ordered[low]) * (index - low)


def measure_trial(rows, trial, max_gap, required_fraction=0.9, min_hold=1.0):
    """Integrate each recorded state until the next timestamp, capped at max_gap.

    Missing samples and tracking-unavailable intervals remain in the denominator
    of trial success. Raw accuracy uses only input-valid time; stable accuracy
    uses only input-valid and stable-ready time. These denominators are reported.
    """
    start, end = finite(trial["hold_start_s"]), finite(trial["hold_end_s"])
    if end <= start:
        raise ValueError("Trial hold end must be later than start")
    target = trial["target_pose"]
    total = end - start
    valid = ready = raw_correct = stable_correct = observed = longest = run = 0.0
    first_correct = None
    switches = 0
    unwanted_switches = 0
    last_stable = None
    last_interval_end = start
    timestamps = [row["time"] for row in rows]
    first = max(0, bisect.bisect_right(timestamps, start) - 1)
    for i in range(first, len(rows)):
        row = rows[i]
        if row["time"] >= end:
            break
        next_time = rows[i + 1]["time"] if i + 1 < len(rows) else end
        a = max(start, row["time"])
        b = min(end, next_time, row["time"] + max_gap)
        if b <= a:
            continue
        if a > last_interval_end + 1e-9:
            run, last_stable = 0.0, None
        duration = b - a
        observed += duration
        if row["input_valid"] == "true":
            valid += duration
            if row["raw_pose"] == target:
                raw_correct += duration
            if row["stable_ready"] == "true":
                ready += duration
                current = row["stable_pose"]
                if last_stable is not None and current != last_stable:
                    switches += 1
                    # The first transition into the instructed target is recognition,
                    # while other label changes during a static hold are unwanted.
                    if not (current == target and first_correct is None):
                        unwanted_switches += 1
                last_stable = current
                if current == target:
                    stable_correct += duration
                    run += duration
                    longest = max(longest, run)
                    if first_correct is None:
                        first_correct = max(0.0, a - start)
                else:
                    run = 0.0
            else:
                run, last_stable = 0.0, None
        else:
            run, last_stable = 0.0, None
        last_interval_end = b
    return {
        "trial_id": trial["trial_id"], "hand": trial["hand_id"], "pose": target,
        "hold_s": total, "observed_s": observed, "input_valid_s": valid,
        "stable_ready_s": ready, "raw_correct_s": raw_correct,
        "stable_correct_s": stable_correct,
        "tracking_or_sample_missing_fraction": max(0.0, 1 - valid / total),
        "unobserved_fraction": max(0.0, 1 - observed / total),
        "raw_accuracy_on_valid": raw_correct / valid if valid else None,
        "stable_accuracy_on_ready": stable_correct / ready if ready else None,
        "stable_correct_fraction_including_missing": stable_correct / total,
        "longest_stable_correct_s": longest, "stabilization_latency_s": first_correct,
        "switches_in_hold": switches,
        "unwanted_switches_in_hold": unwanted_switches,
        "success": valid / total >= required_fraction - 1e-9 and
                   stable_correct / total >= required_fraction - 1e-9 and
                   longest >= min_hold - 1e-9,
    }


def analyze(directory, max_gap=0.25):
    directory = Path(directory)
    metadata = json.loads((directory / "session.json").read_text(encoding="utf-8-sig"))
    if metadata.get("schema_version") not in (1, 2):
        raise ValueError("Unsupported schema")
    active_poses = poses_for_metadata(metadata)
    by_hand = {"Left": [], "Right": []}
    processing = {}
    with (directory / "poses.csv").open(encoding="utf-8-sig", newline="") as stream:
        for row in csv.DictReader(stream):
            row["time"] = finite(row["timestamp_s"])
            if row["hand_id"] not in by_hand:
                raise ValueError("Unknown hand ID")
            hand_rows = by_hand[row["hand_id"]]
            if hand_rows and row["time"] < hand_rows[-1]["time"]:
                raise ValueError("Non-monotonic pose timestamps")
            hand_rows.append(row)
            processing.setdefault(row["frame_id"], finite(row["processing_both_hands_ms"]))
    trials, aborted = [], 0
    with (directory / "trials.csv").open(encoding="utf-8-sig", newline="") as stream:
        for trial in csv.DictReader(stream):
            if trial["status"] != "Completed":
                aborted += 1
                continue
            if trial["target_pose"] not in active_poses or trial["hand_id"] not in by_hand:
                raise ValueError("Invalid instructed target")
            trials.append(measure_trial(by_hand[trial["hand_id"]], trial, max_gap))
    groups = []
    for hand in by_hand:
        for pose in active_poses:
            subset = [trial for trial in trials if trial["hand"] == hand and trial["pose"] == pose]
            if not subset:
                continue
            total = sum(t["hold_s"] for t in subset)
            valid = sum(t["input_valid_s"] for t in subset)
            ready = sum(t["stable_ready_s"] for t in subset)
            raw = sum(t["raw_correct_s"] for t in subset)
            stable = sum(t["stable_correct_s"] for t in subset)
            latencies = [t["stabilization_latency_s"] for t in subset if t["stabilization_latency_s"] is not None]
            groups.append({
                "hand": hand, "pose": pose, "trials": len(subset),
                "successful_trials": sum(t["success"] for t in subset),
                "trial_success_rate": sum(t["success"] for t in subset) / len(subset),
                "tracking_or_sample_missing_fraction": max(0.0, 1 - valid / total),
                "raw_accuracy_on_valid": raw / valid if valid else None,
                "stable_accuracy_on_ready": stable / ready if ready else None,
                "stable_correct_fraction_including_missing": stable / total,
                "input_valid_s": valid, "stable_ready_s": ready, "hold_s": total,
                "latency_median_s": statistics.median(latencies) if latencies else None,
                "latency_p95_s": percentile(latencies, 0.95),
                "trials_without_correct_stable": len(subset) - len(latencies),
                "switches_in_hold": sum(t["switches_in_hold"] for t in subset),
                "unwanted_switches_in_hold": sum(t["unwanted_switches_in_hold"] for t in subset),
            })
    return {
        "session_id": metadata["session_id"], "source": metadata.get("source"),
        "settings_version": metadata.get("settings_version"),
        "recording_complete": metadata.get("recording_complete", False),
        "dropped_batches": metadata.get("dropped_batches", 0),
        "dropped_events": metadata.get("dropped_events", 0),
        "aborted_trials": aborted, "completed_trials": len(trials),
        "metric_definition": {
            "time_weighted": True, "max_observation_interval_s": max_gap,
            "success": "input-valid >=90% AND stable-correct >=90% of total hold AND continuous stable-correct >=1s",
            "latency": "first correct ready label minus instructed hold start; 0 if already correct; no detection reported separately",
            "ground_truth": "instructed trials.csv target; compliance must be observed separately",
            "unwanted_switches": "ready-label changes during hold, excluding the first transition into the instructed target; tracking loss is reported as missing time separately",
        },
        "processing_both_hands_ms": {
            "samples": len(processing),
            "median": statistics.median(processing.values()) if processing else None,
            "p95": percentile(list(processing.values()), 0.95),
        },
        "classes": groups, "trials": trials,
    }


def write_report(result, output):
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    (output / "evaluation.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    groups = result["classes"]
    if groups:
        with (output / "classes.csv").open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(groups[0]))
            writer.writeheader()
            writer.writerows(groups)
    percent = lambda v: "--" if v is None else f"{v * 100:.1f}%"
    lines = [f"Session: {result['session_id']}", f"Source: {result['source']}",
             f"Complete recording: {result['recording_complete']}; dropped batches/events: {result['dropped_batches']}/{result['dropped_events']}",
             f"Completed/aborted trials: {result['completed_trials']}/{result['aborted_trials']}", "",
             "| Hand | Pose | Missing | Raw on valid | Stable on ready | Stable incl. missing | Trial success | Median latency s | Switches / unwanted |",
             "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
    for group in groups:
        latency = group["latency_median_s"]
        lines.append(f"| {group['hand']} | {group['pose']} | {percent(group['tracking_or_sample_missing_fraction'])} | "
                     f"{percent(group['raw_accuracy_on_valid'])} | {percent(group['stable_accuracy_on_ready'])} | "
                     f"{percent(group['stable_correct_fraction_including_missing'])} | {group['successful_trials']}/{group['trials']} | "
                     f"{('--' if latency is None else format(latency, '.3f'))} | {group['switches_in_hold']} / {group['unwanted_switches_in_hold']} |")
    lines += ["", "Accuracy is time-weighted. Missing and unobserved time are included in trial success.",
              "Synthetic source results demonstrate software behavior only; they do not measure hand-tracking accuracy.",
              "No instructed trials means no accuracy estimate. See evaluation.json for denominators and metric definitions.",
              f"Both-hands processing ms: {result['processing_both_hands_ms']}"]
    (output / "evaluation.md").write_text("\n".join(lines) + "\n", encoding="utf-8")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("session", type=Path)
    parser.add_argument("--output", type=Path, default=Path("Evaluation"))
    parser.add_argument("--max-gap", type=float, default=0.25)
    args = parser.parse_args()
    if not math.isfinite(args.max_gap) or args.max_gap <= 0:
        parser.error("--max-gap must be a finite positive duration")
    report = analyze(args.session, args.max_gap)
    write_report(report, args.output)
    print(f"Analyzed {report['completed_trials']} instructed trials -> {args.output.resolve()}")
