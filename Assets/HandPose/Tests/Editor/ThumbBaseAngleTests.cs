using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HandPose.Tests
{
    public class ThumbBaseAngleTests
    {
        [TestCase(0,FingerState.Bent,PoseId.Point)]
        [TestCase(20,FingerState.Bent,PoseId.Point)]
        [TestCase(20.01,FingerState.Intermediate,PoseId.Point)]
        [TestCase(44.99,FingerState.Intermediate,PoseId.Point)]
        [TestCase(45,FingerState.Extended,PoseId.Gun)]
        [TestCase(90,FingerState.Extended,PoseId.Gun)]
        [TestCase(180,FingerState.Extended,PoseId.Gun)]
        public void DirectedBaseAngleControlsMeasuredStateAndPose(double angle,FingerState expected,PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right}) {
                var frame=CreateAngles(angle,95,95,hand);
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.Features.Thumb.BaseAngleValid,Is.True);
                Assert.That(result.Features.Thumb.BaseAngle,Is.EqualTo(angle).Within(.001));
                Assert.That(result.Features.Thumb.State,Is.EqualTo(expected));
                Assert.That(result.RawPose,Is.EqualTo(pose));
                if(expected==FingerState.Intermediate) Assert.That(result.ThumbRuleState,Is.EqualTo(FingerState.Bent));
            }
        }

        [TestCase(180,180)] [TestCase(95,95)] [TestCase(20,15)]
        public void ThumbMcpAndIpCurlDoNotChangeBaseState(double mcp,double ip)
        {
            var frame=CreateAngles(65,mcp,ip,HandId.Left);
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(result.Features.Thumb.Mcp,Is.EqualTo(mcp).Within(.001));
            Assert.That(result.Features.Thumb.Ip,Is.EqualTo(ip).Within(.001));
            Assert.That(result.Features.Thumb.State,Is.EqualTo(FingerState.Extended));
            Assert.That(result.RawPose,Is.EqualTo(PoseId.Gun));
            frame.Joints[(int)JointId.ThumbDistal].PositionValid=false;
            var missingDistal=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(missingDistal.Features.Thumb.State,Is.EqualTo(FingerState.Extended));
            Assert.That(missingDistal.RawPose,Is.EqualTo(PoseId.Gun));
            frame.Joints[(int)JointId.ThumbTip].PositionValid=false;
            var missingTip=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(missingTip.Features.Thumb.State,Is.EqualTo(FingerState.Extended));
            Assert.That(missingTip.Candidates[(int)PoseId.Gun],Is.EqualTo(RuleStatus.Match));
            Assert.That(missingTip.Candidates[(int)PoseId.Pinch],Is.EqualTo(RuleStatus.Unavailable));
        }

        [TestCase(JointId.ThumbMetacarpal)] [TestCase(JointId.ThumbProximal)]
        [TestCase(JointId.Wrist)] [TestCase(JointId.MiddleProximal)]
        public void MissingBaseOrReferenceJointCannotBecomeBent(JointId joint)
        {
            var frame=CreateAngles(65,180,180,HandId.Right);
            frame.Joints[(int)joint].PositionValid=false;
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
            Assert.That(result.Features.Thumb.BaseAngleValid,Is.False);
            Assert.That(result.Features.Thumb.State,Is.EqualTo(FingerState.Invalid));
            Assert.That(result.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
        }

        [Test]
        public void DegenerateOrNonFiniteVectorsAreRejected()
        {
            var frame=CreateAngles(65,180,180,HandId.Right);
            frame.Joints[(int)JointId.Wrist].Position=frame.Joints[(int)JointId.MiddleProximal].Position;
            Assert.That(HandFeatureExtractor.Extract(frame,new PoseSettings()).Thumb.BaseAngleValid,Is.False);
            Assert.That(HandFeatureExtractor.TryVectorAngle(Vector3.zero,Vector3.zero,Vector3.zero,Vector3.up,1e-6,out _),Is.False);
            Assert.That(HandFeatureExtractor.TryVectorAngle(Vector3.zero,new Vector3(float.NaN,1,0),Vector3.zero,Vector3.up,1e-6,out _),Is.False);
            Assert.That(HandFeatureExtractor.TryVectorAngle(new Vector3(10,2,3),new Vector3(10,3,3),new Vector3(-2,4,8),new Vector3(-2,5,8),1e-6,out var angle),Is.True);
            Assert.That(angle,Is.EqualTo(0).Within(.0001));
        }

        [Test]
        public void AngleIsInvariantUnderRigidMotionScaleAndReflection()
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right}) foreach(float scale in new[] {.6f,1f,1.8f}) {
                var frame=CreateAngles(65,95,20,hand); var rotation=Quaternion.Euler(31,124,77);
                for(int i=0;i<26;i++) {
                    var sample=frame.Joints[i]; var p=sample.Position; p.x=-p.x;
                    sample.Position=rotation*(p*scale)+new Vector3(1.2f,-.1f,.2f); frame.Joints[i]=sample;
                }
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.Features.Thumb.BaseAngle,Is.EqualTo(65).Within(.001));
                Assert.That(result.RawPose,Is.EqualTo(PoseId.Gun));
            }
        }

        [Test]
        public void LiveUpgradeInitializesBaseThresholdAndKeepsHistoricalCalibration()
        {
            var old=new PoseSettings {rulesVersion=5,version="saved-v5",thumbBase=null};
            old.thumb.extendedDegrees=155; old.thumb.bentDegrees=120;
            var live=old.CopyForLive();
            Assert.That(live.rulesVersion,Is.EqualTo(6)); Assert.That(old.rulesVersion,Is.EqualTo(5));
            Assert.That(live.thumbBase.extendedDegrees,Is.EqualTo(45)); Assert.That(live.thumbBase.bentDegrees,Is.EqualTo(20));
            Assert.That(live.thumb.extendedDegrees,Is.EqualTo(155));
            live.thumbBase.extendedDegrees=60;
            var again=live.CopyForLive(); Assert.That(again.thumbBase.extendedDegrees,Is.EqualTo(60));
            again.thumbBase.extendedDegrees=70; Assert.That(live.thumbBase.extendedDegrees,Is.EqualTo(60));
            live.thumbBase.bentDegrees=65; Assert.Throws<ArgumentException>(()=>live.Validate());
        }

        [Test]
        public void RuleRequirementsIncludeWristAndExcludeThumbDistal()
        {
            var ids=PoseRuleEvaluator.RequiredJoints(PoseId.Gun);
            Assert.That(ids.Length,Is.EqualTo(19)); Assert.That(ids.Distinct().Count(),Is.EqualTo(19));
            foreach(var id in HandFeatureExtractor.ThumbBaseJoints) Assert.That(ids.Contains(id),Is.True);
            Assert.That(ids.Contains(JointId.ThumbDistal),Is.False); Assert.That(ids.Contains(JointId.ThumbTip),Is.False);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun,5).Contains(JointId.ThumbDistal),Is.True);
            Assert.That(PoseRuleEvaluator.RequiredJoints(PoseId.Gun,5).Contains(JointId.Wrist),Is.False);
        }

        [Test]
        public void CsvStoresBaseAngleAndReplaysNewAndHistoricalRules()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-base-"+Guid.NewGuid().ToString("N"));
            try {
                foreach(int version in new[] {5,6}) using(var recorder=new PoseCsvRecorder()) {
                    var settings=new PoseSettings {rulesVersion=version}; var pipeline=new PosePipeline(settings);
                    var events=new List<PoseEvent>(); var expected=version==6?PoseId.Gun:PoseId.Point;
                    Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},512),Is.True);
                    for(int i=0;i<3;i++) {
                        var left=CreateAngles(65,95,95,HandId.Left); var right=CreateAngles(65,95,95,HandId.Right);
                        left.FrameId=right.FrameId=i; left.Timestamp=right.Timestamp=i*.11;
                        events.Clear(); var l=pipeline.Process(left,events); var r=pipeline.Process(right,events);
                        Assert.That(l.Rules.RawPose,Is.EqualTo(expected)); recorder.Record(l,r,events);
                    }
                    recorder.Stop(); var path=Path.Combine(recorder.DirectoryPath,"poses.csv");
                    Assert.That(File.ReadLines(path).First().Contains("thumb_base_angle_deg"),Is.EqualTo(version==6));
                    using(var csv=new CsvTable(path)) {
                        Assert.That(csv.Next(),Is.True); Assert.That(csv.Double("thumb_mcp_deg"),Is.EqualTo(95).Within(.001));
                        if(version==6) Assert.That(csv.Double("thumb_base_angle_deg"),Is.EqualTo(65).Within(.001));
                    }
                    var session=CsvSessionReader.Read(recorder.DirectoryPath); var replay=new PosePipeline(session.Metadata.settings);
                    Assert.That(session.Metadata.settings.rulesVersion,Is.EqualTo(version));
                    foreach(var batch in session.Batches) Assert.That(replay.Process(batch.Left,events).Rules.RawPose,Is.EqualTo(expected));
                }
            } finally {
                string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-base-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
                if(Directory.Exists(full)) Directory.Delete(full,true);
            }
        }

        static HandFrame CreateAngles(double angle,double mcp,double ip,HandId hand)
        {
            var frame=SyntheticHands.Create(PoseId.Gun,hand,0,0);
            var reference=(frame.Joints[(int)JointId.MiddleProximal].Position-frame.Joints[(int)JointId.Wrist].Position).normalized;
            float sign=hand==HandId.Left?-1:1;
            var baseDirection=Quaternion.AngleAxis(sign*(float)angle,Vector3.forward)*reference;
            var proximal=Quaternion.AngleAxis(-sign*(float)(180-mcp),Vector3.forward)*baseDirection;
            var distal=Quaternion.AngleAxis(-sign*(float)(180-ip),Vector3.forward)*proximal;
            var cmc=frame.Joints[(int)JointId.ThumbMetacarpal].Position;
            frame.Joints[(int)JointId.ThumbProximal].Position=cmc+baseDirection*.035f;
            frame.Joints[(int)JointId.ThumbDistal].Position=frame.Joints[(int)JointId.ThumbProximal].Position+proximal*.03f;
            frame.Joints[(int)JointId.ThumbTip].Position=frame.Joints[(int)JointId.ThumbDistal].Position+distal*.025f;
            // Keep this angle fixture outside Pinch regardless of distal curl.
            var shift=frame.Joints[(int)JointId.IndexTip].Position+new Vector3(sign*.08f,0,0)-frame.Joints[(int)JointId.ThumbTip].Position;
            foreach(var id in HandFeatureExtractor.ThumbJoints) frame.Joints[(int)id].Position+=shift;
            return frame;
        }
    }
}
