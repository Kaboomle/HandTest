using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace HandPose.Tests
{
    public class IntermediateThumbTests
    {
        [TestCase(PoseId.Fist)] [TestCase(PoseId.Victory)] [TestCase(PoseId.Point)]
        [TestCase(PoseId.Rock)]
        public void IntermediateThumbMatchesBentPosesAndStabilizes(PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right})
            {
                var pipeline=new PosePipeline(new PoseSettings()); var events=new List<PoseEvent>();
                PoseObservation result=null;
                for(int i=0;i<2;i++)
                    result=pipeline.Process(SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(pose).Fingers,hand,i,i*.11),events);
                Assert.That(result.Rules.RawPose,Is.EqualTo(pose)); Assert.That(result.Stable.Pose,Is.EqualTo(pose));
                Assert.That(result.Stable.Ready,Is.True);
                Assert.That(result.Rules.Candidates[(int)pose],Is.EqualTo(RuleStatus.Match));
                Assert.That(result.Rules.ThumbRuleState,Is.EqualTo(FingerState.Bent));
                Assert.That(result.Rules.Features.Thumb.State,Is.EqualTo(FingerState.Intermediate));
                Assert.That(result.Rules.Features.Thumb.Mcp,Is.EqualTo(140).Within(.001));
                Assert.That(result.Rules.Features.Thumb.Ip,Is.EqualTo(140).Within(.001));
            }
        }

        [TestCase(PoseId.Open,PoseId.Other)] [TestCase(PoseId.Gun,PoseId.Point)]
        [TestCase(PoseId.ThumbsUp,PoseId.Fist)]
        public void IntermediateThumbCannotSatisfyExtendedRules(PoseId original,PoseId expected)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right})
            {
                var frame=SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(original).Fingers,hand,0,0);
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.RawPose,Is.EqualTo(expected));
                Assert.That(result.Candidates[(int)original],Is.EqualTo(RuleStatus.NoMatch));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void OtherFingersRemainIntermediateAndCannotSatisfyBent(int finger)
        {
            var states=new[] {FingerState.Bent,FingerState.Bent,FingerState.Bent,FingerState.Bent};
            states[finger]=FingerState.Intermediate;
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(
                SyntheticHands.CreatePattern(FingerState.Intermediate,states,HandId.Left,0,0));
            Assert.That(result.Features.Fingers[finger].State,Is.EqualTo(FingerState.Intermediate));
            Assert.That(result.RawPose,Is.EqualTo(PoseId.Other));
            Assert.That(result.Candidates[(int)PoseId.Fist],Is.EqualTo(RuleStatus.NoMatch));
        }

        [Test]
        public void MissingThumbRemainsInvalidInsteadOfBent()
        {
            var frame=SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Fist).Fingers,HandId.Left,0,0);
            frame.Joints[(int)JointId.ThumbProximal].PositionValid=false;
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(result.Features.Thumb.State,Is.EqualTo(FingerState.Invalid));
            Assert.That(result.ThumbRuleState,Is.EqualTo(FingerState.Invalid));
            Assert.That(result.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
            Assert.That(result.Candidates[(int)PoseId.Fist],Is.EqualTo(RuleStatus.Unavailable));
        }

        [TestCase(2)] [TestCase(3)]
        public void OldRuleVersionsDoNotConvertIntermediateThumb(int version)
        {
            var settings=new PoseSettings {rulesVersion=version,priority=PoseCatalog.DefaultPriority(version),version="saved-"+version};
            var result=new PoseRuleEvaluator(settings).Evaluate(SyntheticHands.CreatePattern(
                FingerState.Intermediate,PoseCatalog.Find(PoseId.Rock).Fingers,HandId.Right,0,0));
            Assert.That(result.ThumbRuleState,Is.EqualTo(FingerState.Intermediate));
            Assert.That(result.RawPose,Is.EqualTo(PoseId.Other));
            Assert.That(result.Candidates[(int)PoseId.Rock],Is.EqualTo(RuleStatus.NoMatch));
        }

        [Test]
        public void Version3LiveConfigurationAdoptsNewPolicyAndKeepsCalibration()
        {
            var saved=new PoseSettings {rulesVersion=3,priority=PoseCatalog.DefaultPriority(3),version="3.0-calibrated"};
            saved.thumb.extendedDegrees=155; saved.thumb.bentDegrees=120;
            saved.priority=saved.priority.Reverse().ToArray();
            var current=saved.CopyForLive();
            Assert.That(current.rulesVersion,Is.EqualTo(6)); Assert.That(saved.rulesVersion,Is.EqualTo(3));
            Assert.That(current.version,Is.Not.EqualTo(saved.version));
            Assert.That(current.priority,Is.EqualTo(saved.priority.Where(p=>p!=PoseId.Fuck)));
            Assert.That(current.thumb.extendedDegrees,Is.EqualTo(155));Assert.That(current.thumb.bentDegrees,Is.EqualTo(120));
            var frame=SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Fist).Fingers,HandId.Left,0,0);
            Assert.That(new PoseRuleEvaluator(saved).Evaluate(frame).RawPose,Is.EqualTo(PoseId.Other));
            Assert.That(new PoseRuleEvaluator(current).Evaluate(frame).RawPose,Is.EqualTo(PoseId.Fist));
        }

        [TestCase(3)] [TestCase(4)]
        public void CsvKeepsMeasuredIntermediateAndReplaysTheSavedPolicy(int version)
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-intermediate-"+Guid.NewGuid().ToString("N"));
            using(var recorder=new PoseCsvRecorder())
            try
            {
                var settings=new PoseSettings {rulesVersion=version,priority=PoseCatalog.DefaultPriority(version),version="saved-"+version};
                var pipeline=new PosePipeline(settings); var events=new List<PoseEvent>();var expected=new List<string>();
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},512),Is.True);
                for(int i=0;i<3;i++)
                {
                    events.Clear();
                    var left=pipeline.Process(SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Fist).Fingers,HandId.Left,i,i*.11),events);
                    var right=pipeline.Process(SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Rock).Fingers,HandId.Right,i,i*.11),events);
                    Assert.That(left.Rules.RawPose,Is.EqualTo(version==4?PoseId.Fist:PoseId.Other));
                    Assert.That(right.Rules.RawPose,Is.EqualTo(version==4?PoseId.Rock:PoseId.Other));
                    recorder.Record(left,right,events); expected.Add(Signature(left));expected.Add(Signature(right));
                }
                recorder.Stop(); var session=CsvSessionReader.Read(recorder.DirectoryPath);
                Assert.That(session.Metadata.settings.rulesVersion,Is.EqualTo(version));
                using(var csv=new CsvTable(Path.Combine(recorder.DirectoryPath,"poses.csv")))
                {
                    Assert.That(csv.Next(),Is.True); Assert.That(csv.Get("thumb_state"),Is.EqualTo("Intermediate"));
                    Assert.That(csv.Double("thumb_mcp_deg"),Is.EqualTo(140).Within(.001));
                    Assert.That(csv.Double("thumb_ip_deg"),Is.EqualTo(140).Within(.001));
                    Assert.That(csv.Get("raw_pose"),Is.EqualTo(version==4?"Fist":"Other"));
                }
                var replay=new PosePipeline(session.Metadata.settings); var actual=new List<string>();
                foreach(var batch in session.Batches)
                {events.Clear();actual.Add(Signature(replay.Process(batch.Left,events)));actual.Add(Signature(replay.Process(batch.Right,events)));}
                Assert.That(actual,Is.EqualTo(expected));
            }
            finally
            {
                recorder.Dispose();
                string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-intermediate-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
                if(Directory.Exists(full)) Directory.Delete(full,true);
            }
        }
        static string Signature(PoseObservation o) => o.Rules.Features.Thumb.State+":"+o.Rules.ThumbRuleState+":"+o.Rules.RawPose+":"+o.Stable.Pose+":"+o.Stable.Ready;
    }
}
