using System;
using UnityEngine;

namespace HandPose
{
    public struct FingerFeature
    {
        public double Pip, Dip;
        public bool PipValid, DipValid;
        public FingerState State;
    }

    public struct ThumbFeature
    {
        public double Mcp, Ip, BaseAngle, SpreadRatio;
        public bool McpValid, IpValid, BaseAngleValid, SpreadValid;
        public FingerState State;
    }

    public sealed class HandFeatures
    {
        public readonly FingerFeature[] Fingers = new FingerFeature[4];
        public ThumbFeature Thumb;
        public bool PinchValid, AllRequiredValid, ScaleMeasured, DistanceMeasured;
        public double ScaleMeters, PinchMeters, PinchRatio;
        public string InvalidReason = "";
    }

    public static class HandFeatureExtractor
    {
        public static readonly JointId[] ThumbJoints = { JointId.ThumbMetacarpal, JointId.ThumbProximal, JointId.ThumbDistal, JointId.ThumbTip };
        // Anatomical CMC=ThumbMetacarpal, MCP=ThumbProximal; middle MCP=MiddleProximal.
        public static readonly JointId[] ThumbBaseJoints = { JointId.ThumbMetacarpal, JointId.ThumbProximal, JointId.Wrist, JointId.MiddleProximal };
        public static readonly JointId[][] FingerJoints = {
            new[] { JointId.IndexProximal, JointId.IndexIntermediate, JointId.IndexDistal, JointId.IndexTip },
            new[] { JointId.MiddleProximal, JointId.MiddleIntermediate, JointId.MiddleDistal, JointId.MiddleTip },
            new[] { JointId.RingProximal, JointId.RingIntermediate, JointId.RingDistal, JointId.RingTip },
            new[] { JointId.LittleProximal, JointId.LittleIntermediate, JointId.LittleDistal, JointId.LittleTip }
        };
        // Double precision avoids float dot-product overflow and keeps the denominator explicit.
        public static bool TryAngle(Vector3 a, Vector3 b, Vector3 c, double minSegment, out double angle) =>
            TryVectorAngle(b,a,b,c,minSegment,out angle);
        public static bool TryVectorAngle(Vector3 startA,Vector3 endA,Vector3 startB,Vector3 endB,double minSegment,out double angle)
        {
            angle=0;
            if(!Numeric.Finite(startA) || !Numeric.Finite(endA) || !Numeric.Finite(startB) || !Numeric.Finite(endB) ||
                !Numeric.Finite(minSegment) || minSegment<=0) return false;
            double ax=(double)endA.x-startA.x, ay=(double)endA.y-startA.y, az=(double)endA.z-startA.z;
            double bx=(double)endB.x-startB.x, by=(double)endB.y-startB.y, bz=(double)endB.z-startB.z;
            double la=Math.Sqrt(ax*ax+ay*ay+az*az), lb=Math.Sqrt(bx*bx+by*by+bz*bz);
            if(la<minSegment || lb<minSegment || !Numeric.Finite(la*lb)) return false;
            double cos=(ax*bx+ay*by+az*bz)/(la*lb);
            angle=Math.Acos(Math.Max(-1,Math.Min(1,cos)))*180/Math.PI;
            return Numeric.Finite(angle);
        }
        public static FingerState Classify(double pip, double dip, FingerThreshold t)
        {
            if (!Numeric.Finite(pip) || !Numeric.Finite(dip)) return FingerState.Invalid;
            // Small tolerance compensates floating-point representation at exact angle boundaries.
            if (pip >= t.extendedDegrees-0.0001 && dip >= t.extendedDegrees-0.0001) return FingerState.Extended;
            if (pip <= t.bentDegrees+0.0001 || dip <= t.bentDegrees+0.0001) return FingerState.Bent;
            return FingerState.Intermediate;
        }
        public static FingerState ClassifyThumb(double angle,FingerThreshold threshold) => Classify(angle,angle,threshold);
        // The little-to-index MCP axis points toward the thumb on either hand.
        // Dot with the unnormalized axis and divide by width squared: rotation/scale invariant.
        public static bool TryThumbSpread(Vector3 tip,Vector3 indexBase,Vector3 littleBase,
            double minWidth,double maxWidth,out double ratio)
        {
            ratio=0;
            if(!Numeric.Finite(tip) || !Numeric.Finite(indexBase) || !Numeric.Finite(littleBase)) return false;
            double x=(double)indexBase.x-littleBase.x,y=(double)indexBase.y-littleBase.y,z=(double)indexBase.z-littleBase.z;
            double squared=x*x+y*y+z*z;
            if(!Numeric.Finite(squared) || squared<=0 || squared<minWidth*minWidth || squared>maxWidth*maxWidth) return false;
            ratio=(((double)tip.x-indexBase.x)*x+((double)tip.y-indexBase.y)*y+((double)tip.z-indexBase.z)*z)/squared;
            return Numeric.Finite(ratio);
        }
        public static HandFeatures Extract(HandFrame frame, PoseSettings settings)
        {
            var f = new HandFeatures();
            string reason = frame.HandValid ? "" : (string.IsNullOrEmpty(frame.InvalidReason) ? "HandInactive" : frame.InvalidReason);
            bool fingersValid = true;
            for (int i = 0; i < 4; i++)
            {
                var ids = FingerJoints[i];
                bool a = frame.TryPosition(ids[0], out var p0), b = frame.TryPosition(ids[1], out var p1);
                bool c = frame.TryPosition(ids[2], out var p2), d = frame.TryPosition(ids[3], out var p3);
                var ff = new FingerFeature();
                ff.PipValid = a && b && c && TryAngle(p0,p1,p2,settings.minSegmentMeters,out ff.Pip);
                ff.DipValid = b && c && d && TryAngle(p1,p2,p3,settings.minSegmentMeters,out ff.Dip);
                ff.State = ff.PipValid && ff.DipValid ? Classify(ff.Pip,ff.Dip,settings.fingers[i]) : FingerState.Invalid;
                f.Fingers[i] = ff;
                if (ff.State == FingerState.Invalid)
                {
                    fingersValid = false;
                    reason += (reason.Length > 0 ? ";" : "") + ids[0] + ":MissingNonFiniteOrDegenerate";
                }
            }
            bool tm = frame.TryPosition(JointId.ThumbMetacarpal,out var t0), tp = frame.TryPosition(JointId.ThumbProximal,out var t1);
            bool td = frame.TryPosition(JointId.ThumbDistal,out var t2), tt = frame.TryPosition(JointId.ThumbTip,out var t3);
            var thumbFeature = new ThumbFeature();
            thumbFeature.McpValid = tm && tp && td && TryAngle(t0,t1,t2,settings.minSegmentMeters,out thumbFeature.Mcp);
            thumbFeature.IpValid = tp && td && tt && TryAngle(t1,t2,t3,settings.minSegmentMeters,out thumbFeature.Ip);
            bool wrist=frame.TryPosition(JointId.Wrist,out var w), middle=frame.TryPosition(JointId.MiddleProximal,out var m);
            thumbFeature.BaseAngleValid=tm && tp && wrist && middle && TryVectorAngle(t0,t1,w,m,settings.minSegmentMeters,out thumbFeature.BaseAngle);
            // v6 measures base opening relative to the hand. Saved v1-v5 retain their original formulas.
            thumbFeature.State = settings.rulesVersion>=6 ?
                (thumbFeature.BaseAngleValid?ClassifyThumb(thumbFeature.BaseAngle,settings.thumbBase):FingerState.Invalid) : settings.rulesVersion>=5 ?
                (thumbFeature.McpValid?ClassifyThumb(thumbFeature.Mcp,settings.thumb):FingerState.Invalid) :
                (thumbFeature.McpValid && thumbFeature.IpValid?Classify(thumbFeature.Mcp,thumbFeature.Ip,settings.thumb):FingerState.Invalid);
            f.Thumb = thumbFeature;
            // v7: a curled IP joint overrides an open base. Missing IP data is not bent.
            if (settings.rulesVersion >= 7)
                f.Thumb.State = !thumbFeature.BaseAngleValid || !thumbFeature.IpValid ? FingerState.Invalid :
                    thumbFeature.Ip <= settings.thumb.bentDegrees + 0.0001 ? FingerState.Bent : thumbFeature.State;
            thumbFeature = f.Thumb;
            if(settings.RequiresThumb && thumbFeature.State == FingerState.Invalid)
                reason += (reason.Length > 0 ? ";" : "") + "Thumb:MissingNonFiniteOrDegenerate";
            bool scale = frame.TryPosition(JointId.IndexProximal,out var indexBase) & frame.TryPosition(JointId.LittleProximal,out var littleBase);
            bool tips = frame.TryPosition(JointId.ThumbTip,out var thumb) & frame.TryPosition(JointId.IndexTip,out var indexTip);
            if (scale) f.ScaleMeters = Vector3.Distance(indexBase,littleBase);
            if (tips) f.PinchMeters = Vector3.Distance(thumb,indexTip);
            f.ScaleMeasured = scale && Numeric.Finite(f.ScaleMeters);
            f.DistanceMeasured = tips && Numeric.Finite(f.PinchMeters);
            f.Thumb.SpreadValid=scale && tt && TryThumbSpread(t3,indexBase,littleBase,
                settings.minHandScaleMeters,settings.maxHandScaleMeters,out f.Thumb.SpreadRatio);
            if(settings.useThumbSpread && f.Thumb.State!=FingerState.Invalid)
            {
                if(!f.Thumb.SpreadValid) f.Thumb.State=FingerState.Invalid;
                else if(f.Thumb.BaseAngleValid && f.Thumb.BaseAngle>=settings.thumbSpreadMinBaseDegrees &&
                    f.Thumb.SpreadRatio>=settings.thumbSpreadExtendedRatio)
                    f.Thumb.State=FingerState.Extended;
            }
            f.PinchValid = scale && tips && Numeric.Finite(f.ScaleMeters) && Numeric.Finite(f.PinchMeters) &&
                f.ScaleMeters >= settings.minHandScaleMeters && f.ScaleMeters <= settings.maxHandScaleMeters;
            if (f.PinchValid)
            {
                f.PinchRatio = f.PinchMeters/f.ScaleMeters;
                f.PinchValid = Numeric.Finite(f.PinchRatio);
            }
            else reason += (reason.Length > 0 ? ";" : "") + "Pinch:MissingNonFiniteOrScaleOutOfRange";
            f.AllRequiredValid = frame.HandValid && fingersValid && (!settings.RequiresThumb || f.Thumb.State != FingerState.Invalid) && f.PinchValid && Numeric.Finite(frame.Timestamp) && frame.Timestamp >= 0;
            if (!Numeric.Finite(frame.Timestamp) || frame.Timestamp < 0) reason += ";InvalidTimestamp";
            f.InvalidReason = reason;
            return f;
        }
    }
}
