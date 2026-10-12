using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace HandPose.Tests
{
    public class ThumbPoseTests
    {
        [TestCase(PoseId.Gun)] [TestCase(PoseId.Rock)] [TestCase(PoseId.ThumbsUp)]
        public void NewPosesAreInvariantForBothHands(PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right}) foreach(float scale in new[] {.6f,1f,1.8f})
            {
                var frame=SyntheticHands.Create(pose,hand,0,0);
                foreach(int j in Enumerable.Range(0,26))
                {var sample=frame.Joints[j]; sample.Position=Quaternion.Euler(42,97,11)*(sample.Position*scale)+new Vector3(.1f,-.2f,.3f); frame.Joints[j]=sample;}
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.RawPose,Is.EqualTo(pose));
                Assert.That(result.Candidates[(int)pose],Is.EqualTo(RuleStatus.Match));
                Assert.That(result.Features.Thumb.McpValid && result.Features.Thumb.IpValid,Is.True);
            }
        }
        [TestCase(150,150,FingerState.Extended)] [TestCase(150,149,FingerState.Extended)]
        [TestCase(125,180,FingerState.Bent)] [TestCase(180,125,FingerState.Extended)] [TestCase(140,140,FingerState.Intermediate)]
        public void ThumbHasIndependentThresholds(double mcp,double ip,FingerState expected)
        {Assert.That(HandFeatureExtractor.ClassifyThumb(mcp,PoseSettings.DefaultThumbThreshold()),Is.EqualTo(expected));}
        [TestCase("Missing")] [TestCase("NaN")] [TestCase("Infinity")] [TestCase("Degenerate")]
        public void BadThumbCannotProduceAValidNewRepresentative(string kind)
        {
            var frame=SyntheticHands.Create(PoseId.Gun,HandId.Right,0,0);
            int joint=(int)JointId.ThumbMetacarpal;
            if(kind=="Missing") frame.Joints[joint].PositionValid=false;
            else if(kind=="Degenerate") frame.Joints[joint].Position=frame.Joints[(int)JointId.ThumbProximal].Position;
            else frame.Joints[joint].Position=new Vector3(kind=="NaN"?float.NaN:float.PositiveInfinity,0,0);
            var r=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(r.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
            Assert.That(r.Candidates[(int)PoseId.Gun],Is.EqualTo(RuleStatus.Unavailable));
            Assert.That(r.Candidates[(int)PoseId.Point],Is.EqualTo(RuleStatus.Unavailable));
            Assert.That(r.Features.InvalidReason,Does.Contain("Thumb:"));
        }
        [Test]
        public void Version2KeepsOverlappingLegacyCandidatesAndPriority()
        {
            foreach(var pair in new[] {new[] {PoseId.Gun,PoseId.Point},new[] {PoseId.ThumbsUp,PoseId.Fist}})
            {
                var settings=new PoseSettings {rulesVersion=2,priority=PoseCatalog.DefaultPriority(2),version="2.0-thumb"}; var frame=SyntheticHands.Create(pair[0],HandId.Left,0,0);
                var result=new PoseRuleEvaluator(settings).Evaluate(frame);
                Assert.That(result.RawPose,Is.EqualTo(pair[0]));
                Assert.That(result.Candidates[(int)pair[1]],Is.EqualTo(RuleStatus.Match));
                settings.priority=new[] {pair[1]}.Concat(settings.priority.Where(p=>p!=pair[1])).ToArray();
                Assert.That(new PoseRuleEvaluator(settings).Evaluate(frame).RawPose,Is.EqualTo(pair[1]));
            }
        }
        [Test]
        public void All32BinaryFingerPatternsObeyAllSevenExactDefinitions()
        {
            var expected=new Dictionary<int,PoseId> {{31,PoseId.Open},{0,PoseId.Fist},{6,PoseId.Victory},{2,PoseId.Point},
                {3,PoseId.Gun},{18,PoseId.Rock},{1,PoseId.ThumbsUp}};
            for(int mask=0;mask<32;mask++)
            {
                var states=Enumerable.Range(1,4).Select(bit=>(mask&(1<<bit))!=0?FingerState.Extended:FingerState.Bent).ToArray();
                var frame=SyntheticHands.CreatePattern((mask&1)!=0?FingerState.Extended:FingerState.Bent,states,HandId.Right,mask,mask);
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.RawPose,Is.EqualTo(expected.TryGetValue(mask,out var raw)?raw:PoseId.Other),"mask="+mask);
                foreach(var pose in expected.Values)
                    Assert.That(result.Candidates[(int)pose],Is.EqualTo(expected.TryGetValue(mask,out var p) && p==pose?RuleStatus.Match:RuleStatus.NoMatch),"mask="+mask+", pose="+pose);
            }
        }
        [Test]
        public void MissingThumbClearsPinchHistoryEvenWhenPartialCandidateMatches()
        {
            var rules=new PoseRuleEvaluator(new PoseSettings());
            var missing=SyntheticHands.Create(PoseId.Open,HandId.Left,0,0,.1f);
            missing.Joints[(int)JointId.ThumbMetacarpal].PositionValid=false;
            var partial=rules.Evaluate(missing);
            Assert.That(partial.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
            Assert.That(partial.Candidates[(int)PoseId.Pinch],Is.EqualTo(RuleStatus.Match));
            Assert.That(rules.Evaluate(SyntheticHands.Create(PoseId.Open,HandId.Left,1,.01,.3f)).RawPose,Is.EqualTo(PoseId.Open));
        }
        [Test]
        public void LegacyLiveUpgradeKeepsOldOrderThresholdsAndCopiesThumb()
        {
            var legacy=Legacy(); legacy.fingers[0].extendedDegrees=172;
            var current=legacy.CopyForLive();
            Assert.That(current.rulesVersion,Is.EqualTo(6)); Assert.That(current.priority.Length,Is.EqualTo(8));
            Assert.That(current.priority.Where(p=>(int)p<5),Is.EqualTo(legacy.priority));
            Assert.That(current.fingers[0].extendedDegrees,Is.EqualTo(172));
            current.thumb.extendedDegrees=170; Assert.That(legacy.thumb.extendedDegrees,Is.EqualTo(150));
            Assert.That(legacy.rulesVersion,Is.EqualTo(1));
        }
        [Test]
        public void Schema2CsvRecordsCurrentThumbFeaturesAndReplaysAllPoses()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-thumbcsv-"+Guid.NewGuid().ToString("N"));
            using(var recorder=new PoseCsvRecorder())
            try
            {
                var settings=new PoseSettings(); var pipeline=new PosePipeline(settings); var expected=new List<string>(); var events=new List<PoseEvent>();
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},4096),Is.True);
                long id=0;
                foreach(var rule in PoseCatalog.Rules) foreach(double offset in new[] {0d,.11,.2})
                {
                    double time=(id/3)+offset; events.Clear();
                    var left=pipeline.Process(SyntheticHands.Create(rule.Id,HandId.Left,id,time),events);
                    var right=pipeline.Process(SyntheticHands.Create(rule.Id,HandId.Right,id++,time),events);
                    recorder.Record(left,right,events); expected.Add(Signature(left)); expected.Add(Signature(right));
                }
                recorder.Stop(); var loaded=CsvSessionReader.Read(recorder.DirectoryPath);
                Assert.That(loaded.Metadata.schema_version,Is.EqualTo(2)); Assert.That(loaded.Metadata.recording_complete,Is.True);
                Assert.That(loaded.Metadata.settings.rulesVersion,Is.EqualTo(6));
                var replay=new PosePipeline(loaded.Metadata.settings); var actual=new List<string>();
                foreach(var batch in loaded.Batches) {events.Clear();actual.Add(Signature(replay.Process(batch.Left,events)));actual.Add(Signature(replay.Process(batch.Right,events)));}
                Assert.That(actual,Is.EqualTo(expected));
                using(var table=new CsvTable(Path.Combine(recorder.DirectoryPath,"poses.csv")))
                {
                    Assert.That(table.Next(),Is.True); Assert.That(table.Get("thumb_state"),Is.EqualTo("Extended"));
                    Assert.That(table.Double("thumb_mcp_deg"),Is.EqualTo(180).Within(.001)); Assert.That(table.Double("thumb_ip_deg"),Is.EqualTo(180).Within(.001));
                    foreach(var rule in PoseCatalog.Rules) Assert.That(table.Get("candidate_"+rule.CsvName),Is.Not.Empty);
                }
            }
            finally {recorder.Dispose();Cleanup(root);}
        }
        [Test]
        public void OldMetadataWithoutThumbKeepsFiveRuleReplayBehavior()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-legacythumb-"+Guid.NewGuid().ToString("N"));
            using(var recorder=new PoseCsvRecorder())
            try
            {
                var settings=Legacy(); var pipeline=new PosePipeline(settings); var events=new List<PoseEvent>();
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},512),Is.True);
                var left=pipeline.Process(SyntheticHands.Create(PoseId.Gun,HandId.Left,0,0),events);
                var right=pipeline.Process(SyntheticHands.Create(PoseId.ThumbsUp,HandId.Right,0,0),events);
                Assert.That(left.Rules.RawPose,Is.EqualTo(PoseId.Point)); Assert.That(right.Rules.RawPose,Is.EqualTo(PoseId.Fist));
                recorder.Record(left,right,events); recorder.Stop();
                string path=Path.Combine(recorder.DirectoryPath,"session.json");
                var meta=JsonUtility.FromJson<SessionMetadata>(File.ReadAllText(path));meta.schema_version=1;
                string old=Regex.Replace(JsonUtility.ToJson(meta),"\"rulesVersion\":1,","");
                old=Regex.Replace(old,"\"thumb\":\\{[^}]*\\},","");File.WriteAllText(path,old);
                var loaded=CsvSessionReader.Read(recorder.DirectoryPath);
                Assert.That(loaded.Metadata.settings.rulesVersion,Is.EqualTo(1)); Assert.That(loaded.Metadata.settings.priority.Length,Is.EqualTo(5));
                var frame=loaded.Batches[0].Left;frame.Joints[(int)JointId.ThumbMetacarpal].PositionValid=false;
                Assert.That(new PoseRuleEvaluator(loaded.Metadata.settings).Evaluate(frame).RawPose,Is.EqualTo(PoseId.Point));
            }
            finally {recorder.Dispose();Cleanup(root);}
        }
        [Test]
        public void EnumIdsAndRequiredThumbPointsRemainExplicit()
        {
            Assert.That((int)PoseId.Other,Is.EqualTo(5));Assert.That((int)PoseId.TrackingUnavailable,Is.EqualTo(6));
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun).Contains(JointId.ThumbMetacarpal),Is.True);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Point).Contains(JointId.ThumbMetacarpal),Is.True);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Point,2).Contains(JointId.ThumbMetacarpal),Is.False);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Pinch).Length,Is.EqualTo(4));
        }
        static PoseSettings Legacy() => new PoseSettings {rulesVersion=1,version="1.0-default",priority=new[] {PoseId.Pinch,PoseId.Point,PoseId.Victory,PoseId.Fist,PoseId.Open}};
        static string Signature(PoseObservation observation) => observation.Frame.Hand+":"+observation.Rules.RawPose+":"+observation.Stable.Pose+":"+observation.Stable.Ready+":"+string.Join("/",observation.Rules.Candidates);
        static void Cleanup(string root)
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if(Directory.Exists(full)) Directory.Delete(full,true);
        }
    }
}
