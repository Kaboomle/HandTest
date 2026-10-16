using System.Collections.Generic;

namespace HandPose
{
    // Feed once per input sample, independently of the display refresh rate.
    public sealed class PoseTransitionSequence
    {
        int requiredFrames = 3;
        public int RequiredFrames
        {
            get => requiredFrames;
            set
            {
                int frames = System.Math.Max(1, value);
                if (requiredFrames == frames) return;
                requiredFrames = frames;
                Interrupt();
            }
        }
        readonly List<PoseId> states = new List<PoseId>();
        public IReadOnlyList<PoseId> States => states;
        PoseId candidate;
        int consecutive;
        public double InactivitySeconds { get; set; }
        double lastChangeTime;

        public bool Update(PoseId pose) => Update(pose, 0);

        public bool Update(PoseId pose, double timestamp)
        {
            Expire(timestamp);
            if (pose == PoseId.TrackingUnavailable) { Interrupt(); return false; }
            if (consecutive == 0 || candidate != pose) { candidate = pose; consecutive = 1; }
            else if (consecutive < RequiredFrames) consecutive++;
            if (consecutive == RequiredFrames && (states.Count == 0 || states[states.Count - 1] != pose))
            { states.Add(pose); lastChangeTime = timestamp; return true; }
            return false;
        }

        // Call even without input samples so a stalled live provider cannot retain stale history.
        public bool Expire(double timestamp)
        {
            if (states.Count == 0 || InactivitySeconds <= 0 || timestamp - lastChangeTime < InactivitySeconds)
                return false;
            Clear();
            return true;
        }

        public bool MatchesSuffix(IReadOnlyList<PoseId> pattern)
        {
            if (pattern == null || pattern.Count < 2 || pattern.Count > states.Count) return false;
            int start = states.Count - pattern.Count;
            for (int i = 0; i < pattern.Count; i++)
                if (states[start + i] != pattern[i]) return false;
            return true;
        }

        public void Interrupt() { consecutive = 0; }
        public void Clear() { states.Clear(); Interrupt(); lastChangeTime = 0; }
    }
}
