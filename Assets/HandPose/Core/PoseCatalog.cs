namespace HandPose
{
    public sealed class PoseRuleDefinition
    {
        public readonly PoseId Id;
        public readonly string CsvName;
        public readonly FingerState? Thumb;
        public readonly FingerState[] Fingers;
        public bool UsesDistance => Id == PoseId.Pinch;
        public PoseRuleDefinition(PoseId id,string csvName,FingerState? thumb,params FingerState[] fingers)
        { Id=id; CsvName=csvName; Thumb=thumb; Fingers=fingers; }
    }
    public static class PoseCatalog
    {
        public const int CurrentRulesVersion=6;
        const FingerState E=FingerState.Extended, B=FingerState.Bent;
        // Four-finger order is index, middle, ring, little. Pinch uses tip distance alone.
        public static readonly PoseRuleDefinition[] Rules = {
            new PoseRuleDefinition(PoseId.Open,"open",E,E,E,E,E),
            new PoseRuleDefinition(PoseId.Fist,"fist",B,B,B,B,B),
            new PoseRuleDefinition(PoseId.Victory,"victory",B,E,E,B,B),
            new PoseRuleDefinition(PoseId.Point,"point",B,E,B,B,B),
            new PoseRuleDefinition(PoseId.Pinch,"pinch",null),
            new PoseRuleDefinition(PoseId.Gun,"gun",E,E,B,B,B),
            new PoseRuleDefinition(PoseId.Rock,"rock",B,E,B,B,E),
            new PoseRuleDefinition(PoseId.ThumbsUp,"thumbs_up",E,B,B,B,B)
        };
        public static readonly PoseId[] DemoPoses = {
            PoseId.Open,PoseId.Fist,PoseId.Victory,PoseId.Point,PoseId.Pinch,
            PoseId.Gun,PoseId.Rock,PoseId.ThumbsUp,PoseId.Other,PoseId.TrackingUnavailable
        };
        public static readonly int CandidateSlots = CountCandidateSlots();
        static int CountCandidateSlots()
        { int count=0; foreach(var rule in Rules) count=System.Math.Max(count,(int)rule.Id+1); return count; }
        public static PoseId[] DefaultPriority(int version=CurrentRulesVersion) => version==1 ? new[] {
            PoseId.Pinch,PoseId.Point,PoseId.Victory,PoseId.Fist,PoseId.Open
        } : version<5 ? new[] {
            PoseId.Pinch,PoseId.Gun,PoseId.ThumbsUp,PoseId.Rock,PoseId.Fuck,
            PoseId.Point,PoseId.Victory,PoseId.Fist,PoseId.Open
        } : new[] {
            PoseId.Pinch,PoseId.Gun,PoseId.ThumbsUp,PoseId.Rock,
            PoseId.Point,PoseId.Victory,PoseId.Fist,PoseId.Open
        };
        // The retired ID is available only when replaying saved v1-v4 sessions.
        static readonly PoseRuleDefinition[] historicalRules = HistoricalRules();
        static PoseRuleDefinition[] HistoricalRules()
        {
            var rules=new System.Collections.Generic.List<PoseRuleDefinition>(Rules);
            rules.Insert(7,new PoseRuleDefinition(PoseId.Fuck,"fuck",B,B,E,B,B));
            return rules.ToArray();
        }
        public static PoseRuleDefinition[] RulesForVersion(int version) => version<5?historicalRules:Rules;
        public static PoseRuleDefinition Find(PoseId id,int version=CurrentRulesVersion)
        { foreach(var rule in RulesForVersion(version)) if(rule.Id==id) return rule; return null; }
        // Saved v1/v2 sessions keep the original thumb-free Open/Fist/Victory/Point rules.
        public static FingerState? ThumbForVersion(PoseRuleDefinition rule,int version) =>
            version<3 && (int)rule.Id<4 ? (FingerState?)null : rule.Thumb;
        // Preserve measured features; v4 and newer interpret an intermediate thumb as bent.
        public static FingerState ThumbStateForVersion(FingerState observed,int version) =>
            version>=4 && observed==FingerState.Intermediate ? FingerState.Bent : observed;
        public static bool IsRule(PoseId id,int version=CurrentRulesVersion) => Find(id,version)!=null;
    }
}
