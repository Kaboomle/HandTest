using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandPose
{
    public enum HandId { Left, Right }
    // Original serialized IDs are preserved, including Other=5 and TrackingUnavailable=6.
    public enum PoseId { Open=0, Fist=1, Victory=2, Point=3, Pinch=4, Other=5, TrackingUnavailable=6, Gun=7, Rock=8, Fuck=9, ThumbsUp=10 }
    public enum RuleStatus { NoMatch, Match, Unavailable }
    public enum FingerState { Invalid, Intermediate, Extended, Bent }
    // OpenXR/PICO 26-joint convention. These are not MediaPipe indices.
    public enum JointId
    {
        Palm, Wrist, ThumbMetacarpal, ThumbProximal, ThumbDistal, ThumbTip,
        IndexMetacarpal, IndexProximal, IndexIntermediate, IndexDistal, IndexTip,
        MiddleMetacarpal, MiddleProximal, MiddleIntermediate, MiddleDistal, MiddleTip,
        RingMetacarpal, RingProximal, RingIntermediate, RingDistal, RingTip,
        LittleMetacarpal, LittleProximal, LittleIntermediate, LittleDistal, LittleTip
    }

    public static class Numeric
    {
        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool ValidQuaternion(Quaternion q) => Finite(q.x) && Finite(q.y) &&
            Finite(q.z) && Finite(q.w) && (double)q.x*q.x + (double)q.y*q.y + (double)q.z*q.z + (double)q.w*q.w > 1e-12;
    }

    public struct JointSample
    {
        public Vector3 Position;
        public Quaternion Orientation;
        public bool PositionValid, OrientationValid;
        public ulong SdkFlags;
        public bool HasPosition => PositionValid && Numeric.Finite(Position);
        public bool HasOrientation => OrientationValid && Numeric.ValidQuaternion(Orientation);
    }

    public sealed class HandFrame
    {
        public const int JointCount = 26;
        public long FrameId;
        public double Timestamp;
        public double? SdkTimestamp;
        public HandId Hand;
        public bool HandValid;
        public uint SdkActive;
        public string InvalidReason = "";
        public readonly JointSample[] Joints = new JointSample[JointCount];
        public bool TryPosition(JointId id, out Vector3 p)
        {
            JointSample j = Joints[(int)id]; p = j.Position;
            return HandValid && j.HasPosition;
        }
    }

    public struct ResetCommand
    {
        public long AfterFrameId;
        public double Timestamp;
        public HandId Hand;
        public string Reason;
    }

    public sealed class HandInputBatch
    {
        public HandFrame Left, Right;
        public readonly List<ResetCommand> ResetsAfter = new List<ResetCommand>();
    }

    public interface IHandJointProvider
    {
        string SourceDescription { get; }
        bool TryGetBatch(double appTime, out HandInputBatch batch);
    }

    [Serializable]
    public sealed class FingerThreshold
    {
        public float extendedDegrees = 160;
        public float bentDegrees = 130;
    }

    [Serializable]
    public sealed class PoseSettings
    {
        public string version = "6.0-thumb-base-angle";
        public int rulesVersion = PoseCatalog.CurrentRulesVersion;
        public FingerThreshold[] fingers = { new FingerThreshold(), new FingerThreshold(), new FingerThreshold(), new FingerThreshold() };
        [HideInInspector] public FingerThreshold thumb = DefaultThumbThreshold(); // Historical MCP/IP thresholds.
        [Tooltip("Angle between thumb CMC-to-MCP and wrist-to-middle-MCP (rule version 6).")]
        public FingerThreshold thumbBase = DefaultThumbBaseThreshold();
        public float pinchEnter = 0.25f, pinchExit = 0.35f;
        public float minHandScaleMeters = 0.01f, maxHandScaleMeters = 0.20f;
        public float minSegmentMeters = 0.000001f;
        public double stabilizationSeconds = 0.1;
        public PoseId[] priority = PoseCatalog.DefaultPriority();
        public bool RequiresThumb => rulesVersion >= 2;
        public static FingerThreshold DefaultThumbThreshold() => new FingerThreshold { extendedDegrees=150, bentDegrees=125 };
        public static FingerThreshold DefaultThumbBaseThreshold() => new FingerThreshold { extendedDegrees=45, bentDegrees=20 };
        static void ValidateThreshold(FingerThreshold f)
        {
            if(f == null || !Numeric.Finite(f.bentDegrees) || !Numeric.Finite(f.extendedDegrees) ||
               f.bentDegrees < 0 || f.extendedDegrees > 180 || f.bentDegrees >= f.extendedDegrees)
                throw new ArgumentException("Finger thresholds require 0 <= bent < extended <= 180.");
        }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Settings version is required.");
            if (fingers == null || fingers.Length != 4) throw new ArgumentException("Four finger thresholds are required.");
            foreach (var f in fingers) ValidateThreshold(f);
            ValidateThreshold(thumb);
            if(rulesVersion>=6) ValidateThreshold(thumbBase);
            if(rulesVersion < 1 || rulesVersion > PoseCatalog.CurrentRulesVersion) throw new ArgumentException("Unsupported rules version.");
            if (!Numeric.Finite(pinchEnter) || !Numeric.Finite(pinchExit) || pinchEnter < 0 || pinchExit <= pinchEnter)
                throw new ArgumentException("Pinch requires 0 <= enter < exit.");
            if (!Numeric.Finite(minHandScaleMeters) || !Numeric.Finite(maxHandScaleMeters) ||
                minHandScaleMeters <= 0 || maxHandScaleMeters <= minHandScaleMeters ||
                !Numeric.Finite(minSegmentMeters) || minSegmentMeters <= 0 ||
                !Numeric.Finite(stabilizationSeconds) || stabilizationSeconds < 0)
                throw new ArgumentException("Invalid scale, segment length or stabilization time.");
            int required = rulesVersion == 1 ? 5 : PoseCatalog.RulesForVersion(rulesVersion).Length;
            if (priority == null || priority.Length != required) throw new ArgumentException("Priority must contain each active rule exactly once.");
            var seen = new HashSet<PoseId>();
            foreach (var p in priority)
                if (!PoseCatalog.IsRule(p,rulesVersion) || (rulesVersion == 1 && (int)p > 4) || !seen.Add(p)) throw new ArgumentException("Invalid or duplicate rule priority.");
        }
        public PoseSettings Copy()
        {
            Validate();
            var c = (PoseSettings)MemberwiseClone();
            c.priority = (PoseId[])priority.Clone();
            c.fingers = new FingerThreshold[4];
            for (int i = 0; i < 4; i++) c.fingers[i] = new FingerThreshold { bentDegrees = fingers[i].bentDegrees, extendedDegrees = fingers[i].extendedDegrees };
            c.thumb = new FingerThreshold { bentDegrees=thumb.bentDegrees, extendedDegrees=thumb.extendedDegrees };
            var baseThreshold=thumbBase ?? DefaultThumbBaseThreshold();
            c.thumbBase=new FingerThreshold { bentDegrees=baseThreshold.bentDegrees, extendedDegrees=baseThreshold.extendedDegrees };
            return c;
        }
        // Only live configurations adopt new rules. Replay keeps the saved rule version.
        public PoseSettings CopyForLive()
        {
            var c = (PoseSettings)MemberwiseClone();
            c.thumb = thumb ?? DefaultThumbThreshold();
            // Old internal-angle calibration is not meaningful for the new vector angle.
            c.thumbBase = rulesVersion<6 ? DefaultThumbBaseThreshold() : (thumbBase ?? DefaultThumbBaseThreshold());
            if(priority != null && priority.Length == 5)
            {
                c.rulesVersion=1; c.Validate();
                var order=new List<PoseId>();
                foreach(var pose in priority)
                {
                    if(pose == PoseId.Point) order.Add(PoseId.Gun);
                    if(pose == PoseId.Fist) order.Add(PoseId.ThumbsUp);
                    order.Add(pose);
                    if(pose == PoseId.Pinch) order.Add(PoseId.Rock);
                }
                c.priority=order.ToArray();
            }
            else
            {
                c.Validate();
                var order=new List<PoseId>();
                foreach(var pose in c.priority) if(PoseCatalog.IsRule(pose)) order.Add(pose);
                c.priority=order.ToArray();
            }
            if(c.rulesVersion < PoseCatalog.CurrentRulesVersion) c.version=version+"-base-angle-v6";
            c.rulesVersion=PoseCatalog.CurrentRulesVersion;
            return c.Copy();
        }
    }
}
