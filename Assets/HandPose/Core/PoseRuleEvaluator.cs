using System;

namespace HandPose
{
    public sealed class RuleResult
    {
        public PoseId RawPose;
        public HandFeatures Features;
        public FingerState ThumbRuleState;
        public readonly RuleStatus[] Candidates = new RuleStatus[PoseCatalog.CandidateSlots];
        public bool InputValid => Features.AllRequiredValid;
    }
    public sealed class PoseRuleEvaluator
    {
        readonly PoseSettings settings;
        bool pinched;
        public PoseRuleEvaluator(PoseSettings settings) { this.settings=settings.Copy(); }
        public void Reset() { pinched=false; }
        public static JointId[] RequiredJoints(PoseId pose,int rulesVersion=PoseCatalog.CurrentRulesVersion)
        {
            if(rulesVersion<1 || rulesVersion>PoseCatalog.CurrentRulesVersion) throw new ArgumentOutOfRangeException(nameof(rulesVersion));
            var rule=PoseCatalog.Find(pose,rulesVersion);
            if(rule==null) throw new ArgumentException("Pose has no recognition rule.",nameof(pose));
            if(rulesVersion==1 && (int)pose>4) throw new ArgumentException("Pose is inactive in this rule version.",nameof(pose));
            if(rule.UsesDistance) return new[] {JointId.ThumbTip,JointId.IndexTip,JointId.IndexProximal,JointId.LittleProximal};
            var thumb=PoseCatalog.ThumbForVersion(rule,rulesVersion);
            // MiddleProximal is already in the four-finger set, so v6 adds three distinct joints.
            var thumbIds=rulesVersion>=6 ? new[] {JointId.ThumbMetacarpal,JointId.ThumbProximal,JointId.Wrist} : HandFeatureExtractor.ThumbJoints;
            int thumbCount=rulesVersion>=5?3:4;
            var ids=new JointId[thumb.HasValue?16+thumbCount:16];
            for(int i=0;i<4;i++) Array.Copy(HandFeatureExtractor.FingerJoints[i],0,ids,i*4,4);
            if(thumb.HasValue) Array.Copy(thumbIds,0,ids,16,thumbCount);
            return ids;
        }
        public RuleResult Evaluate(HandFrame frame)
        {
            var r=new RuleResult {Features=HandFeatureExtractor.Extract(frame,settings)};
            var f=r.Features;
            r.ThumbRuleState=PoseCatalog.ThumbStateForVersion(f.Thumb.State,settings.rulesVersion);
            if(!f.AllRequiredValid) pinched=false;
            r.Candidates[(int)PoseId.Fuck]=RuleStatus.Unavailable;
            foreach(var rule in PoseCatalog.RulesForVersion(settings.rulesVersion))
            {
                if(settings.rulesVersion==1 && (int)rule.Id>4)
                {r.Candidates[(int)rule.Id]=RuleStatus.Unavailable;continue;}
                if(rule.UsesDistance)
                {
                    if(f.PinchValid)
                    {
                        if(f.PinchRatio<=settings.pinchEnter) pinched=true;
                        else if(f.PinchRatio>=settings.pinchExit) pinched=false;
                    }
                    r.Candidates[(int)rule.Id]=!f.PinchValid?RuleStatus.Unavailable:(pinched?RuleStatus.Match:RuleStatus.NoMatch);
                    continue;
                }
                bool valid=true, match=true;
                for(int i=0;i<4;i++)
                { valid &= f.Fingers[i].State!=FingerState.Invalid; match &= f.Fingers[i].State==rule.Fingers[i]; }
                var thumb=PoseCatalog.ThumbForVersion(rule,settings.rulesVersion);
                if(thumb.HasValue)
                { valid &= f.Thumb.State!=FingerState.Invalid; match &= r.ThumbRuleState==thumb.Value; }
                r.Candidates[(int)rule.Id]=!valid?RuleStatus.Unavailable:(match?RuleStatus.Match:RuleStatus.NoMatch);
            }
            // Partial Pinch candidates are useful diagnostics but cannot seed next-frame history.
            if(!f.AllRequiredValid) pinched=false;
            r.RawPose=f.AllRequiredValid?PoseId.Other:PoseId.TrackingUnavailable;
            if(f.AllRequiredValid) foreach(var p in settings.priority)
                if(r.Candidates[(int)p]==RuleStatus.Match) {r.RawPose=p;break;}
            return r;
        }
    }
}
