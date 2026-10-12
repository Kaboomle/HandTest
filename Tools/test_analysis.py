import unittest
import csv
import json
import tempfile
from pathlib import Path
from analyze_session import measure_trial, analyze, POSES


class AnalysisTests(unittest.TestCase):
    def test_new_targets_and_both_schemas_are_analyzed(self):
        for schema in (1, 2):
            for pose in POSES:
                with self.subTest(schema=schema, pose=pose), tempfile.TemporaryDirectory() as directory:
                    root = Path(directory)
                    (root / "session.json").write_text(json.dumps(dict(schema_version=schema, session_id="test", source="SYNTHETIC")), encoding="utf-8")
                    with (root / "poses.csv").open("w", encoding="utf-8", newline="") as stream:
                        names = ("frame_id", "timestamp_s", "hand_id", "input_valid", "raw_pose", "stable_pose", "stable_ready", "processing_both_hands_ms")
                        writer = csv.DictWriter(stream, fieldnames=names); writer.writeheader()
                        for i in range(41):
                            writer.writerow(dict(frame_id=i, timestamp_s=i / 20, hand_id="Left", input_valid="true", raw_pose=pose,
                                                 stable_pose=pose, stable_ready="true", processing_both_hands_ms=.1))
                    with (root / "trials.csv").open("w", encoding="utf-8", newline="") as stream:
                        writer = csv.DictWriter(stream, fieldnames=("trial_id", "hand_id", "target_pose", "hold_start_s", "hold_end_s", "status"))
                        writer.writeheader(); writer.writerow(dict(trial_id=1, hand_id="Left", target_pose=pose, hold_start_s=0, hold_end_s=2, status="Completed"))
                    result = analyze(root)
                    self.assertEqual(result["classes"][0]["pose"], pose)
                    self.assertEqual(result["classes"][0]["successful_trials"], 1)

    def test_retired_target_is_accepted_only_for_old_recordings(self):
        for version in (4, 5, 6):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                (root / "session.json").write_text(json.dumps(dict(schema_version=2, session_id="test", settings=dict(rulesVersion=version))), encoding="utf-8")
                (root / "poses.csv").write_text("frame_id,timestamp_s,hand_id,input_valid,raw_pose,stable_pose,stable_ready,processing_both_hands_ms\n0,0,Left,true,Fuck,Fuck,true,0.1\n", encoding="utf-8")
                (root / "trials.csv").write_text("trial_id,hand_id,target_pose,hold_start_s,hold_end_s,status\n1,Left,Fuck,0,2,Completed\n", encoding="utf-8")
                if version == 4:
                    self.assertEqual(analyze(root)["completed_trials"], 1)
                else:
                    with self.assertRaises(ValueError):
                        analyze(root)

    def test_unknown_schema_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "session.json").write_text('{"schema_version":99}', encoding="utf-8")
            with self.assertRaises(ValueError):
                analyze(root)

    def trial(self):
        return dict(trial_id="1", hand_id="Left", target_pose="Open", hold_start_s="0", hold_end_s="2")

    def rows(self, valid=True, pose="Open"):
        return [dict(time=i/20, input_valid=str(valid).lower(), raw_pose=pose,
                     stable_pose=pose, stable_ready=str(valid).lower()) for i in range(41)]

    def test_perfect_hold_success(self):
        result = measure_trial(self.rows(), self.trial(), .25)
        self.assertTrue(result["success"])
        self.assertAlmostEqual(result["raw_accuracy_on_valid"], 1)
        self.assertAlmostEqual(result["tracking_or_sample_missing_fraction"], 0)

    def test_missing_is_in_success_denominator(self):
        rows = self.rows()
        for row in rows[10:30]:
            row["input_valid"] = row["stable_ready"] = "false"
        result = measure_trial(rows, self.trial(), .25)
        self.assertAlmostEqual(result["raw_accuracy_on_valid"], 1)
        self.assertAlmostEqual(result["tracking_or_sample_missing_fraction"], .5)
        self.assertFalse(result["success"])

    def test_no_observations_has_no_accuracy(self):
        result = measure_trial([], self.trial(), .25)
        self.assertIsNone(result["raw_accuracy_on_valid"])
        self.assertEqual(result["tracking_or_sample_missing_fraction"], 1)
        self.assertFalse(result["success"])

    def test_gaps_are_not_held_indefinitely(self):
        rows = self.rows()[:1]
        result = measure_trial(rows, self.trial(), .25)
        self.assertAlmostEqual(result["unobserved_fraction"], .875)
        self.assertFalse(result["success"])

    def test_variable_intervals_use_duration(self):
        rows = self.rows()
        for row in rows[20:]:
            row["raw_pose"] = row["stable_pose"] = "Fist"
        result = measure_trial(rows, self.trial(), .25)
        self.assertAlmostEqual(result["raw_accuracy_on_valid"], .5)
        self.assertEqual(result["switches_in_hold"], 1)
        self.assertEqual(result["unwanted_switches_in_hold"], 1)
        self.assertFalse(result["success"])

    def test_first_entry_into_target_is_not_unwanted_switch(self):
        rows = self.rows(pose="Fist")
        for row in rows[20:]:
            row["raw_pose"] = row["stable_pose"] = "Open"
        result = measure_trial(rows, self.trial(), .25)
        self.assertEqual(result["switches_in_hold"], 1)
        self.assertEqual(result["unwanted_switches_in_hold"], 0)
        self.assertAlmostEqual(result["stabilization_latency_s"], 1)


if __name__ == "__main__":
    unittest.main()
