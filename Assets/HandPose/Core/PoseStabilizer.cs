using System;
using System.Collections.Generic;

namespace HandPose
{
    public struct PoseEvent
    {
        public long AfterFrameId;
        public double Timestamp, Duration;
        public HandId Hand;
        public string Type, Reason;
        public PoseId Pose;
    }
    public struct StableState
    {
        public PoseId Pose, Pending;
        public bool Ready, HasPending;
    }
    public sealed class PoseStabilizer
    {
        readonly HandId hand;
        readonly double delay;
        double pendingSince, stableSince, lastTimestamp = double.NegativeInfinity;
        long lastFrame = -1;
        StableState state = new StableState { Pose = PoseId.TrackingUnavailable };
        public StableState State => state;
        public PoseStabilizer(HandId hand, double delay)
        {
            if (!Numeric.Finite(delay) || delay < 0) throw new ArgumentException("Invalid stabilization time.");
            this.hand = hand; this.delay = delay;
        }
        void Emit(List<PoseEvent> events, string type, PoseId pose, double t, double duration, string reason)
        {
            events.Add(new PoseEvent { AfterFrameId=lastFrame, Timestamp=t, Hand=hand, Type=type, Pose=pose, Duration=duration, Reason=reason });
        }
        public void Reset(double timestamp, string reason, List<PoseEvent> events)
        {
            double t = Numeric.Finite(timestamp) && timestamp >= 0 ? timestamp : (Numeric.Finite(lastTimestamp) ? lastTimestamp : 0);
            if (state.Ready) Emit(events,"End",state.Pose,t,Math.Max(0,t-stableSince),reason);
            Emit(events,"Reset",PoseId.TrackingUnavailable,t,0,reason);
            state = new StableState { Pose=PoseId.TrackingUnavailable };
            lastTimestamp = double.NegativeInfinity;
        }
        public StableState Update(long frame, double timestamp, PoseId raw, List<PoseEvent> events)
        {
            if (!Numeric.Finite(timestamp) || timestamp < 0)
            { Reset(timestamp,"InvalidTimestamp",events); return state; }
            if (timestamp < lastTimestamp) Reset(timestamp,"ReplayRewind",events);
            lastFrame = frame;
            lastTimestamp = timestamp;
            if (raw == PoseId.TrackingUnavailable)
            {
                if (state.Ready) Emit(events,"End",state.Pose,timestamp,Math.Max(0,timestamp-stableSince),"TrackingLost");
                state = new StableState { Pose=PoseId.TrackingUnavailable };
                return state;
            }
            if (state.Ready && state.Pose == raw) state.HasPending = false;
            else
            {
                if (!state.HasPending || state.Pending != raw)
                { state.Pending = raw; state.HasPending = true; pendingSince = timestamp; }
                if (timestamp-pendingSince >= delay-1e-9)
                {
                    if (state.Ready) Emit(events,"End",state.Pose,timestamp,Math.Max(0,timestamp-stableSince),"PoseChanged");
                    state.Pose = raw; state.Ready = true; state.HasPending = false; stableSince = timestamp;
                    Emit(events,"Start",raw,timestamp,0,"Stabilized");
                }
            }
            if (state.Ready) Emit(events,"Update",state.Pose,timestamp,Math.Max(0,timestamp-stableSince),"");
            return state;
        }
    }

    public sealed class PoseObservation
    {
        public HandFrame Frame;
        public RuleResult Rules;
        public StableState Stable;
        public double ProcessingMs;
    }

    public sealed class PosePipeline
    {
        readonly PoseRuleEvaluator[] rules;
        readonly PoseStabilizer[] stabilizers;
        readonly double[] lastTimes = { double.NegativeInfinity, double.NegativeInfinity };
        public event Action<PoseEvent> PoseEventRaised;
        public PosePipeline(PoseSettings settings)
        {
            settings.Validate();
            rules = new[] { new PoseRuleEvaluator(settings),new PoseRuleEvaluator(settings) };
            stabilizers = new[] { new PoseStabilizer(HandId.Left,settings.stabilizationSeconds),new PoseStabilizer(HandId.Right,settings.stabilizationSeconds) };
        }
        public PoseObservation Process(HandFrame frame, List<PoseEvent> events)
        {
            int h = (int)frame.Hand;
            if (h < 0 || h > 1) throw new ArgumentException("Invalid hand.");
            int first = events.Count;
            if (frame.Timestamp < lastTimes[h]) Reset(frame.Hand,frame.Timestamp,"ReplayRewind",events,false);
            lastTimes[h] = frame.Timestamp;
            var result = rules[h].Evaluate(frame);
            var stable = stabilizers[h].Update(frame.FrameId,frame.Timestamp,result.RawPose,events);
            Publish(events,first);
            return new PoseObservation { Frame=frame, Rules=result, Stable=stable };
        }
        void Publish(List<PoseEvent> events, int first)
        { for (int i=first;i<events.Count;i++) PoseEventRaised?.Invoke(events[i]); }
        public void Reset(HandId hand, double time, string reason, List<PoseEvent> events, bool publish=true)
        {
            int first=events.Count, h=(int)hand;
            rules[h].Reset(); stabilizers[h].Reset(time,reason,events); lastTimes[h]=double.NegativeInfinity;
            if (publish) Publish(events,first);
        }
    }
}
