using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HandPose.Tests
{
    public class McpOnlyThumbTests
    {
        [TestCase(180,95,FingerState.Extended,PoseId.Gun)]
        [TestCase(160,140,FingerState.Extended,PoseId.Gun)]
        [TestCase(140,180,FingerState.Intermediate,PoseId.Point)]
        [TestCase(110,180,FingerState.Bent,PoseId.Point)]
        public void McpControlsClassificationRegardlessOfIp(double mcp,double ip,FingerState measured,PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right})
            {
                var pipeline=new PosePipeline(new PoseSettings {rulesVersion=5});var events=new List<PoseEvent>();
                for(int i=0;i<3;i++)
                {
                    var result=pipeline.Process(CreateAngles(mcp,ip,hand,i,i*.11),events);
                    Assert.That(result.Rules.Features.Thumb.Mcp,Is.EqualTo(mcp).Within(.001));
                    Assert.That(result.Rules.Features.Thumb.Ip,Is.EqualTo(ip).Within(.001));
                    Assert.That(result.Rules.Features.Thumb.State,Is.EqualTo(measured));
                    Assert.That(result.Rules.RawPose,Is.EqualTo(pose));
                    if(i==2) {Assert.That(result.Stable.Ready,Is.True);Assert.That(result.Stable.Pose,Is.EqualTo(pose));}
                }
            }
        }

        [Test]
        public void InvalidIpAngleDoesNotInvalidateMcpStateOrGunCandidate()
        {
            var frame=CreateAngles(180,95,HandId.Right,0,0);
            frame.Joints[(int)JointId.ThumbTip].Position=frame.Joints[(int)JointId.ThumbDistal].Position;
            var result=new PoseRuleEvaluator(new PoseSettings {rulesVersion=5}).Evaluate(frame);
            Assert.That(result.Features.Thumb.McpValid,Is.True);Assert.That(result.Features.Thumb.IpValid,Is.False);
            Assert.That(result.Features.Thumb.State,Is.EqualTo(FingerState.Extended));
            Assert.That(result.Candidates[(int)PoseId.Gun],Is.EqualTo(RuleStatus.Match));
            Assert.That(result.InputValid,Is.True);Assert.That(result.RawPose,Is.EqualTo(PoseId.Gun));
        }

        [Test]
        public void CurrentRuleRequirementsUseThreeThumbPointsAndRetainPinchTips()
        {
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun).Length,Is.EqualTo(19));
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun).Contains(JointId.ThumbTip),Is.False);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun,4).Length,Is.EqualTo(20));
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Pinch).Contains(JointId.ThumbTip),Is.True);
        }

        [Test]
        public void RetiredMiddleFingerPatternIsOtherAndCannotBeSelectedAsCurrentRule()
        {
            var frame=SyntheticHands.CreatePattern(FingerState.Bent,new[] {FingerState.Bent,FingerState.Extended,FingerState.Bent,FingerState.Bent},HandId.Right,0,0);
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(result.RawPose,Is.EqualTo(PoseId.Other));
            Assert.That(result.Candidates[(int)PoseId.Fuck],Is.EqualTo(RuleStatus.Unavailable));
            Assert.That(PoseCatalog.Rules.Length,Is.EqualTo(8));
            Assert.That(PoseCatalog.Find(PoseId.Fuck),Is.Null);Assert.That(PoseCatalog.DemoPoses.Contains(PoseId.Fuck),Is.False);
            Assert.Throws<ArgumentException>(()=>PoseRuleEvaluator.RequiredJoints(PoseId.Fuck));
            var settings=new PoseSettings();settings.priority[0]=PoseId.Fuck;
            Assert.Throws<ArgumentException>(()=>settings.Validate());
        }

        [Test]
        public void EvaluationSchedules160TrialsWithoutRetiredPose()
        {
            string root=NewRoot();
            using(var recorder=new PoseCsvRecorder())
            try
            {
                Assert.That(recorder.Start(root,new SessionMetadata {settings=new PoseSettings()},512),Is.True);
                var runner=new PoseTrialRunner();runner.Begin(0);Assert.That(runner.Tick(640,recorder),Is.True);recorder.Stop();
                using(var csv=new CsvTable(Path.Combine(recorder.DirectoryPath,"trials.csv")))
                {
                    int count=0;while(csv.Next()) {Assert.That(csv.Get("target_pose"),Is.Not.EqualTo("Fuck"));count++;}
                    Assert.That(count,Is.EqualTo(160));
                }
            }
            finally {recorder.Dispose();Cleanup(root);}
        }

        [TestCase(4,PoseId.Point)] [TestCase(5,PoseId.Gun)]
        public void CsvKeepsBothAnglesAndReplaysItsVersionWithCorrectCandidateColumns(int version,PoseId expected)
        {
            string root=NewRoot();
            using(var recorder=new PoseCsvRecorder())
            try
            {
                var settings=new PoseSettings {rulesVersion=version,priority=PoseCatalog.DefaultPriority(version)};
                var pipeline=new PosePipeline(settings);var events=new List<PoseEvent>();
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},512),Is.True);
                for(int i=0;i<3;i++)
                {
                    var left=pipeline.Process(CreateAngles(180,95,HandId.Left,i,i*.11),events);
                    var right=pipeline.Process(CreateAngles(180,95,HandId.Right,i,i*.11),events);
                    Assert.That(left.Rules.RawPose,Is.EqualTo(expected));recorder.Record(left,right,events);
                }
                recorder.Stop();string csvPath=Path.Combine(recorder.DirectoryPath,"poses.csv");
                Assert.That(File.ReadLines(csvPath).First().Contains("candidate_fuck"),Is.EqualTo(version<5));
                using(var csv=new CsvTable(csvPath))
                {Assert.That(csv.Next(),Is.True);Assert.That(csv.Double("thumb_mcp_deg"),Is.EqualTo(180).Within(.001));Assert.That(csv.Double("thumb_ip_deg"),Is.EqualTo(95).Within(.001));}
                var session=CsvSessionReader.Read(recorder.DirectoryPath);var replay=new PosePipeline(session.Metadata.settings);
                foreach(var batch in session.Batches) Assert.That(replay.Process(batch.Left,events).Rules.RawPose,Is.EqualTo(expected));
            }
            finally {recorder.Dispose();Cleanup(root);}
        }

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void OldVersionsKeepRemovedRuleAndTwoAngleClassification(int version)
        {
            var settings=new PoseSettings {rulesVersion=version,priority=PoseCatalog.DefaultPriority(version)};
            var rule=new PoseRuleEvaluator(settings);
            Assert.That(rule.Evaluate(SyntheticHands.Create(PoseId.Fuck,HandId.Left,0,0,rulesVersion:version)).RawPose,Is.EqualTo(PoseId.Fuck));
            var result=rule.Evaluate(CreateAngles(180,95,HandId.Left,1,.11));
            Assert.That(result.Features.Thumb.State,Is.EqualTo(FingerState.Bent));
            var live=settings.CopyForLive();Assert.That(live.priority.Contains(PoseId.Fuck),Is.False);Assert.That(live.priority.Length,Is.EqualTo(8));
        }

        public static HandFrame CreateAngles(double mcp,double ip,HandId hand,long id,double time)
        {
            var frame=SyntheticHands.Create(PoseId.Gun,hand,id,time);
            var tip=frame.Joints[(int)JointId.ThumbTip].Position;
            var distal=new Vector3(hand==HandId.Left?.75f:-.75f,.65f,0).normalized;
            float sign=hand==HandId.Left?-1:1;
            var proximal=Quaternion.AngleAxis(sign*(float)(180-ip),Vector3.forward)*distal;
            var metacarpal=Quaternion.AngleAxis(sign*(float)(180-mcp),Vector3.forward)*proximal;
            frame.Joints[(int)JointId.ThumbDistal].Position=tip-distal*.025f;
            frame.Joints[(int)JointId.ThumbProximal].Position=frame.Joints[(int)JointId.ThumbDistal].Position-proximal*.03f;
            frame.Joints[(int)JointId.ThumbMetacarpal].Position=frame.Joints[(int)JointId.ThumbProximal].Position-metacarpal*.035f;
            return frame;
        }
        static string NewRoot() => Path.Combine(Path.GetTempPath(),"handpose-mcp-"+Guid.NewGuid().ToString("N"));
        static void Cleanup(string root)
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-mcp-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
            if(Directory.Exists(full)) Directory.Delete(full,true);
        }
    }
}
