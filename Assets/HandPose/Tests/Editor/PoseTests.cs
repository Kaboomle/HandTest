using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace HandPose.Tests
{
    public class PoseTests
    {
        [TestCase(PoseId.Open)] [TestCase(PoseId.Fist)] [TestCase(PoseId.Victory)]
        [TestCase(PoseId.Point)] [TestCase(PoseId.Pinch)] [TestCase(PoseId.Other)]
        public void A02SyntheticRules(PoseId pose)
        {
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(SyntheticHands.Create(pose,HandId.Left,0,0));
            Assert.That(result.RawPose,Is.EqualTo(pose)); Assert.That(result.InputValid,Is.True);
            if(pose!=PoseId.Other) Assert.That(result.Candidates[(int)pose],Is.EqualTo(RuleStatus.Match));
        }
        [TestCase(PoseId.Open)] [TestCase(PoseId.Fist)] [TestCase(PoseId.Victory)]
        [TestCase(PoseId.Point)] [TestCase(PoseId.Pinch)] [TestCase(PoseId.Other)]
        public void A03RigidScaleAndMirrorInvariance(PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right}) foreach(float scale in new[] {.6f,1f,1.8f})
            {
                var frame=SyntheticHands.Create(pose,hand,0,0); var rotation=Quaternion.Euler(31,124,77);
                for(int i=0;i<26;i++) { var j=frame.Joints[i]; var p=j.Position; p.x=-p.x; j.Position=rotation*(p*scale)+new Vector3(.12f,-.1f,.2f); frame.Joints[i]=j; }
                Assert.That(new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame).RawPose,Is.EqualTo(pose));
            }
        }
        [TestCase(160,160,FingerState.Extended)] [TestCase(160,159,FingerState.Intermediate)]
        [TestCase(130,180,FingerState.Bent)] [TestCase(180,130,FingerState.Bent)] [TestCase(145,145,FingerState.Intermediate)]
        public void A04AngleBoundaries(double a,double b,FingerState expected)
        { Assert.That(HandFeatureExtractor.Classify(a,b,new FingerThreshold()),Is.EqualTo(expected)); }
        [Test]
        public void A04AngleDefinitionAndDegeneracy()
        {
            Assert.That(HandFeatureExtractor.TryAngle(Vector3.down,Vector3.zero,Vector3.up,1e-6,out var angle),Is.True); Assert.That(angle,Is.EqualTo(180).Within(1e-8));
            Assert.That(HandFeatureExtractor.TryAngle(Vector3.zero,Vector3.zero,Vector3.up,1e-6,out _),Is.False);
            Assert.That(HandFeatureExtractor.TryAngle(new Vector3(float.NaN,0,0),Vector3.zero,Vector3.up,1e-6,out _),Is.False);
        }
        [TestCase("Nan")] [TestCase("Infinity")] [TestCase("ZeroLength")] [TestCase("InvalidJoint")] [TestCase("ScaleZero")] [TestCase("ScaleLarge")] [TestCase("Inactive")]
        public void A04InvalidInputsNeverBecomeOther(string mode)
        {
            var f=SyntheticHands.Create(PoseId.Open,HandId.Left,0,0);
            if(mode=="Inactive") f.HandValid=false;
            else if(mode=="ScaleZero") f.Joints[22].Position=f.Joints[7].Position;
            else if(mode=="ScaleLarge") f.Joints[22].Position=new Vector3(5,0,0);
            else if(mode=="ZeroLength") f.Joints[8].Position=f.Joints[7].Position;
            else if(mode=="InvalidJoint") f.Joints[8].PositionValid=false;
            else f.Joints[8].Position=new Vector3(mode=="Nan"?float.NaN:float.PositiveInfinity,0,0);
            var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(f);
            Assert.That(result.RawPose,Is.EqualTo(PoseId.TrackingUnavailable)); Assert.That(result.InputValid,Is.False); Assert.That(result.Features.InvalidReason,Is.Not.Empty);
        }
        [Test]
        public void A06OverlapPriorityAndPartialCandidates()
        {
            var settings=new PoseSettings(); var f=SyntheticHands.Create(PoseId.Open,HandId.Right,0,0,.1f);
            var r=new PoseRuleEvaluator(settings).Evaluate(f); Assert.That(r.RawPose,Is.EqualTo(PoseId.Pinch));
            Assert.That(r.Candidates[(int)PoseId.Open],Is.EqualTo(RuleStatus.Match));
            settings.priority=new[] {PoseId.Open,PoseId.Point,PoseId.Pinch,PoseId.Fist,PoseId.Victory,PoseId.Gun,PoseId.Rock,PoseId.ThumbsUp};
            Assert.That(new PoseRuleEvaluator(settings).Evaluate(f).RawPose,Is.EqualTo(PoseId.Open));
            f.Joints[18].PositionValid=false; r=new PoseRuleEvaluator(settings).Evaluate(f);
            Assert.That(r.RawPose,Is.EqualTo(PoseId.TrackingUnavailable)); Assert.That(r.Candidates[4],Is.EqualTo(RuleStatus.Match)); Assert.That(r.Candidates[0],Is.EqualTo(RuleStatus.Unavailable));
        }
        [Test]
        public void A04PinchHysteresisAndTrackingReset()
        {
            var evaluator=new PoseRuleEvaluator(new PoseSettings());
            RuleResult Eval(float ratio) => evaluator.Evaluate(SyntheticHands.Create(PoseId.Open,HandId.Right,0,0,ratio));
            Assert.That(Eval(.30f).RawPose,Is.EqualTo(PoseId.Open));
            Assert.That(Eval(.25f).RawPose,Is.EqualTo(PoseId.Pinch));
            Assert.That(Eval(.30f).RawPose,Is.EqualTo(PoseId.Pinch));
            Assert.That(Eval(.35f).RawPose,Is.EqualTo(PoseId.Open));
            Eval(.1f); evaluator.Evaluate(SyntheticHands.Create(PoseId.TrackingUnavailable,HandId.Right,1,1));
            Assert.That(Eval(.30f).RawPose,Is.EqualTo(PoseId.Open));
        }
        [Test]
        public void A05VariableTimesAndTransientPending()
        {
            var s=new PoseStabilizer(HandId.Left,.1); var events=new List<PoseEvent>();
            Assert.That(s.Update(0,0,PoseId.Open,events).Ready,Is.False);
            Assert.That(s.Update(1,.07,PoseId.Fist,events).Ready,Is.False);
            Assert.That(s.Update(2,.09,PoseId.Open,events).Ready,Is.False);
            Assert.That(s.Update(3,.18,PoseId.Open,events).Ready,Is.False);
            Assert.That(s.Update(4,.191,PoseId.Open,events).Pose,Is.EqualTo(PoseId.Open));
            var state=s.Update(5,.23,PoseId.Other,events); Assert.That(state.Ready,Is.True); Assert.That(state.Pose,Is.EqualTo(PoseId.Open)); Assert.That(state.Pending,Is.EqualTo(PoseId.Other));
            s.Update(6,.34,PoseId.Other,events);
            Assert.That(events.Count(e=>e.Type=="Start"),Is.EqualTo(2)); Assert.That(events.Count(e=>e.Type=="End"),Is.EqualTo(1));
            Assert.That(events.Last(e=>e.Type=="Start").Pose,Is.EqualTo(PoseId.Other));
        }
        [Test]
        public void A07TrackingLossEndsOnceAndRecoveryWaits()
        {
            var s=new PoseStabilizer(HandId.Left,.1); var e=new List<PoseEvent>();
            s.Update(0,0,PoseId.Open,e); s.Update(1,.1,PoseId.Open,e);
            Assert.That(s.Update(2,.12,PoseId.TrackingUnavailable,e).Ready,Is.False);
            s.Update(3,.14,PoseId.TrackingUnavailable,e);
            Assert.That(e.Count(x=>x.Type=="End"),Is.EqualTo(1)); Assert.That(e.Single(x=>x.Type=="End").Reason,Is.EqualTo("TrackingLost"));
            Assert.That(s.Update(4,.2,PoseId.Open,e).Ready,Is.False); Assert.That(s.Update(5,.29,PoseId.Open,e).Ready,Is.False);
            Assert.That(s.Update(6,.3,PoseId.Open,e).Ready,Is.True);
        }
        [TestCase("ApplicationPaused")] [TestCase("ComponentDisabled")] [TestCase("ReplayRewind")]
        public void ResetEndsExactlyOnce(string reason)
        {
            var s=new PoseStabilizer(HandId.Right,.1); var e=new List<PoseEvent>();
            s.Update(0,0,PoseId.Other,e); s.Update(1,.1,PoseId.Other,e); s.Reset(.2,reason,e); s.Reset(.2,reason,e);
            Assert.That(e.Count(x=>x.Type=="End"),Is.EqualTo(1)); Assert.That(s.State.Ready,Is.False);
        }
        [Test]
        public void A09IndependentHandsAndRewindResetsPinch()
        {
            var p=new PosePipeline(new PoseSettings()); var e=new List<PoseEvent>();
            p.Process(SyntheticHands.Create(PoseId.Open,HandId.Left,0,0),e); p.Process(SyntheticHands.Create(PoseId.Pinch,HandId.Right,0,0),e);
            p.Process(SyntheticHands.Create(PoseId.TrackingUnavailable,HandId.Left,1,.08),e);
            Assert.That(p.Process(SyntheticHands.Create(PoseId.Pinch,HandId.Right,1,.11),e).Stable.Ready,Is.True);
            Assert.That(p.Process(SyntheticHands.Create(PoseId.Open,HandId.Right,0,0,.3f),e).Rules.RawPose,Is.EqualTo(PoseId.Open));
            Assert.That(e.Count(x=>x.Type=="End" && x.Hand==HandId.Right),Is.EqualTo(1));
        }
        [Test]
        public void BadSettingsRejectedAndSnapshotsIndependent()
        {
            var s=new PoseSettings(); var copy=s.Copy(); s.fingers[0].extendedDegrees=170; Assert.That(copy.fingers[0].extendedDegrees,Is.EqualTo(160));
            s.priority=new[] {PoseId.Open,PoseId.Open,PoseId.Pinch,PoseId.Fist,PoseId.Point}; Assert.Throws<ArgumentException>(()=>s.Validate());
            s=new PoseSettings {pinchExit=float.NaN}; Assert.Throws<ArgumentException>(()=>s.Validate());
        }
        [Test]
        public void A08CsvQuotedFieldsAndInvariantCulture()
        {
            var old=CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture=new CultureInfo("fr-FR"); Assert.That(Csv.Number(1.25),Is.EqualTo("1.25"));
                var cells=new[] {"a,b","line1\r\nline2","quote\"here",""};
                var row=Csv.Rows(new StringReader(Csv.Line(cells)+"\r\n")).Single(); Assert.That(row,Is.EqualTo(cells));
            }
            finally { CultureInfo.CurrentCulture=old; }
        }
        [Test]
        public void A08RecordedInputsReproducePosesAndLifecycleEvents()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-test-"+Guid.NewGuid().ToString("N")); var settings=new PoseSettings();
            var recorder=new PoseCsvRecorder();
            try
            {
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings,source="synthetic test",participant_id="anon,\"one\""},512),Is.True);
                var pipeline=new PosePipeline(settings); var expected=new List<string>(); var expectedEvents=new List<PoseEvent>();
                double[] times={0,.05,.11,.13,.20,.31,.40,.43,.55};
                for(int i=0;i<times.Length;i++)
                {
                    var e=new List<PoseEvent>();
                    var l=pipeline.Process(SyntheticHands.Create(i==3?PoseId.TrackingUnavailable:(i>=4?PoseId.Fist:PoseId.Open),HandId.Left,i,times[i]),e);
                    var r=pipeline.Process(SyntheticHands.Create(i>=6?PoseId.Point:PoseId.Pinch,HandId.Right,i,times[i]),e);
                    expected.Add(Signature(l)); expected.Add(Signature(r)); recorder.Record(l,r,e); expectedEvents.AddRange(e);
                    if(i==5)
                    {
                        e.Clear(); pipeline.Reset(HandId.Left,times[i],"ApplicationPaused",e); pipeline.Reset(HandId.Right,times[i],"ApplicationPaused",e);
                        recorder.RecordEvents(e); expectedEvents.AddRange(e);
                    }
                }
                var tail=new List<PoseEvent>(); pipeline.Reset(HandId.Left,.55,"RecordingStopped",tail); pipeline.Reset(HandId.Right,.55,"RecordingStopped",tail);
                recorder.RecordEvents(tail); expectedEvents.AddRange(tail); recorder.Stop();
                var session=CsvSessionReader.Read(recorder.DirectoryPath); Assert.That(session.Metadata.recording_complete,Is.True); Assert.That(session.Batches.Count,Is.EqualTo(times.Length));
                var replay=new PosePipeline(session.Metadata.settings); var actual=new List<string>(); var actualEvents=new List<PoseEvent>();
                foreach(var b in session.Batches)
                {
                    actual.Add(Signature(replay.Process(b.Left,actualEvents))); actual.Add(Signature(replay.Process(b.Right,actualEvents)));
                    foreach(var reset in b.ResetsAfter) replay.Reset(reset.Hand,reset.Timestamp,reset.Reason,actualEvents);
                }
                Assert.That(actual,Is.EqualTo(expected)); Assert.That(actualEvents.Select(EventSignature),Is.EqualTo(expectedEvents.Select(EventSignature)));
                using(var poses=new CsvTable(Path.Combine(recorder.DirectoryPath,"poses.csv"))) { Assert.That(poses.Next(),Is.True); Assert.That(poses.Get("settings_version"),Is.EqualTo(settings.version)); }
                string joints=File.ReadAllText(Path.Combine(recorder.DirectoryPath,"joints.csv")); Assert.That(joints,Does.Not.Contain("NaN"));
            }
            finally { recorder.Dispose(); CleanupTestDirectory(root); }
        }
        static string Signature(PoseObservation o) => o.Frame.Hand+":"+o.Frame.Timestamp+":"+o.Rules.RawPose+":"+o.Stable.Pose+":"+o.Stable.Ready+":"+o.Stable.HasPending+":"+o.Stable.Pending+":"+string.Join("/",o.Rules.Candidates);
        static string EventSignature(PoseEvent e) => e.Hand+":"+e.Timestamp+":"+e.Type+":"+e.Pose+":"+e.Duration+":"+e.Reason+":"+e.AfterFrameId;
        [Test]
        public void RecorderPathFailureIsVisible()
        {
            string path=Path.Combine(Path.GetTempPath(),"handpose-block-"+Guid.NewGuid().ToString("N")); File.WriteAllText(path,"block directory");
            using(var r=new PoseCsvRecorder())
            { try { Assert.That(r.Start(path,new SessionMetadata {settings=new PoseSettings()},8),Is.False); Assert.That(r.LastError,Is.Not.Empty); } finally { File.Delete(path); } }
        }
        [Test]
        public void A08OptionalInvalidJointsAreBlankAndDoNotInvalidatePose()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-blank-"+Guid.NewGuid().ToString("N")); var recorder=new PoseCsvRecorder();
            try
            {
                recorder.Start(root,new SessionMetadata {settings=new PoseSettings()},512);
                var p=new PosePipeline(new PoseSettings()); var e=new List<PoseEvent>();
                var frame=SyntheticHands.Create(PoseId.Open,HandId.Left,0,0);
                frame.Joints[0].Position=new Vector3(float.NaN,0,0); frame.Joints[2].Orientation=default;
                var left=p.Process(frame,e); var right=p.Process(SyntheticHands.Create(PoseId.Fist,HandId.Right,0,0),e);
                Assert.That(left.Rules.RawPose,Is.EqualTo(PoseId.Open)); recorder.Record(left,right,e); recorder.Stop();
                using(var csv=new CsvTable(Path.Combine(recorder.DirectoryPath,"joints.csv")))
                    while(csv.Next())
                    {
                        if(csv.Get("hand_id")!="Left") continue;
                        if(csv.Get("joint_id")=="0") { Assert.That(csv.Get("position_valid"),Is.EqualTo("false")); Assert.That(csv.Get("x_m"),Is.Empty); }
                        if(csv.Get("joint_id")=="2") { Assert.That(csv.Get("orientation_valid"),Is.EqualTo("false")); Assert.That(csv.Get("qw"),Is.Empty); }
                    }
                var replay=CsvSessionReader.Read(recorder.DirectoryPath); Assert.That(new PoseRuleEvaluator(new PoseSettings()).Evaluate(replay.Batches[0].Left).RawPose,Is.EqualTo(PoseId.Open));
            }
            finally { recorder.Dispose(); CleanupTestDirectory(root); }
        }
        [Test]
        public void QueueOverflowMarksRecordingIncomplete()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-queue-"+Guid.NewGuid().ToString("N")); var r=new PoseCsvRecorder();
            try
            {
                r.Start(root,new SessionMetadata {settings=new PoseSettings()},8); var p=new PosePipeline(new PoseSettings()); var e=new List<PoseEvent>();
                var left=p.Process(SyntheticHands.Create(PoseId.Open,HandId.Left,0,0),e); var right=p.Process(SyntheticHands.Create(PoseId.Fist,HandId.Right,0,0),e);
                for(int i=0;i<10000;i++) r.Record(left,right,e);
                r.Stop(); Assert.That(r.DroppedBatches,Is.GreaterThan(0));
                var metadata=JsonUtility.FromJson<SessionMetadata>(File.ReadAllText(Path.Combine(r.DirectoryPath,"session.json")));
                Assert.That(metadata.recording_complete,Is.False); Assert.That(metadata.dropped_batches,Is.EqualTo(r.DroppedBatches));
            }
            finally { r.Dispose(); CleanupTestDirectory(root); }
        }
        [Test]
        public void FailedRestartDoesNotOverwritePreviousSession()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-restart-"+Guid.NewGuid().ToString("N")); var r=new PoseCsvRecorder();
            try
            {
                Assert.That(r.Start(root,new SessionMetadata {settings=new PoseSettings()},8),Is.True); r.Stop();
                string original=Path.Combine(r.DirectoryPath,"session.json"), before=File.ReadAllText(original);
                string blocked=Path.Combine(root,"blocked"); File.WriteAllText(blocked,"file");
                Assert.That(r.Start(blocked,new SessionMetadata {settings=new PoseSettings()},8),Is.False);
                Assert.That(File.ReadAllText(original),Is.EqualTo(before));
            }
            finally { r.Dispose(); CleanupTestDirectory(root); }
        }
        [Test]
        public void A08ReplayStepAndRealtimeUseStoredTimes()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-replay-"+Guid.NewGuid().ToString("N")); var recorder=new PoseCsvRecorder(); var go=new GameObject("Replay test");
            try
            {
                recorder.Start(root,new SessionMetadata {settings=new PoseSettings()},512); var p=new PosePipeline(new PoseSettings());
                foreach(double t in new[] {5.0,5.1,5.3})
                {
                    long id=(long)((t-5)*1000); var e=new List<PoseEvent>(); var l=p.Process(SyntheticHands.Create(PoseId.Open,HandId.Left,id,t),e); var r=p.Process(SyntheticHands.Create(PoseId.Fist,HandId.Right,id,t),e); recorder.Record(l,r,e);
                }
                recorder.Stop(); var provider=go.AddComponent<ReplayHandJointProvider>(); Assert.That(provider.Load(recorder.DirectoryPath),Is.True);
                Assert.That(provider.TryGetBatch(100,out var b),Is.True); Assert.That(b.Left.Timestamp,Is.EqualTo(5));
                Assert.That(provider.TryGetBatch(100.05,out _),Is.False); Assert.That(provider.TryGetBatch(100.101,out b),Is.True); Assert.That(b.Left.Timestamp,Is.EqualTo(5.1));
                provider.Rewind(); provider.realtime=false; Assert.That(provider.TryGetBatch(200,out _),Is.False);
                provider.Step(); Assert.That(provider.TryGetBatch(200,out b),Is.True); Assert.That(b.Left.Timestamp,Is.EqualTo(5)); Assert.That(provider.TryGetBatch(200,out _),Is.False);
            }
            finally { recorder.Dispose(); UnityEngine.Object.DestroyImmediate(go); CleanupTestDirectory(root); }
        }
        static void CleanupTestDirectory(string root)
        {
            string full=Path.GetFullPath(root);
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-",StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing cleanup outside the generated test directory.");
            if(Directory.Exists(full)) Directory.Delete(full,true);
        }
    }
}
