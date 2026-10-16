using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace HandPose.Tests
{
    public class RequiredThumbTests
    {
        [TestCase(PoseId.Open,FingerState.Extended)]
        [TestCase(PoseId.Fist,FingerState.Bent)]
        [TestCase(PoseId.Victory,FingerState.Bent)]
        [TestCase(PoseId.Point,FingerState.Bent)]
        public void OriginalPosesRequireTheirSpecifiedThumb(PoseId pose,FingerState required)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right})
            foreach(var state in new[] {FingerState.Extended,FingerState.Bent,FingerState.Intermediate})
            {
                var frame=SyntheticHands.CreatePattern(state,PoseCatalog.Find(pose).Fingers,hand,0,0);
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.InputValid,Is.True);
                bool matches=state==required || (required==FingerState.Bent && state==FingerState.Intermediate);
                Assert.That(result.Candidates[(int)pose],Is.EqualTo(matches?RuleStatus.Match:RuleStatus.NoMatch));
                Assert.That(result.Features.Thumb.State,Is.EqualTo(state));
                if(matches) Assert.That(result.RawPose,Is.EqualTo(pose));
                else Assert.That(result.RawPose,Is.Not.EqualTo(pose));
            }
        }

        [TestCase(PoseId.Open)] [TestCase(PoseId.Fist)]
        [TestCase(PoseId.Victory)] [TestCase(PoseId.Point)]
        public void MissingThumbMakesOriginalPoseCandidateUnavailable(PoseId pose)
        {
            foreach(var hand in new[] {HandId.Left,HandId.Right})
            {
                var frame=SyntheticHands.Create(pose,hand,0,0);
                frame.Joints[(int)JointId.ThumbMetacarpal].PositionValid=false;
                var result=new PoseRuleEvaluator(new PoseSettings()).Evaluate(frame);
                Assert.That(result.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
                Assert.That(result.Candidates[(int)pose],Is.EqualTo(RuleStatus.Unavailable));
                Assert.That(PoseRuleEvaluator.RequiredJoints(pose).Length,Is.EqualTo(21));
            }
        }

        [Test]
        public void ThumbSeparatesGunFromPointAndThumbsUpFromFistRegardlessOfPriority()
        {
            foreach(var pair in new[] {new[] {PoseId.Gun,PoseId.Point},new[] {PoseId.ThumbsUp,PoseId.Fist}})
            foreach(var pose in pair) foreach(var hand in new[] {HandId.Left,HandId.Right})
            {
                var settings=new PoseSettings();
                var opposite=pair.Single(id=>id!=pose);
                settings.priority=new[] {opposite}.Concat(settings.priority.Where(id=>id!=opposite)).ToArray();
                var result=new PoseRuleEvaluator(settings).Evaluate(SyntheticHands.Create(pose,hand,0,0));
                Assert.That(result.RawPose,Is.EqualTo(pose));
                Assert.That(result.Candidates[(int)opposite],Is.EqualTo(RuleStatus.NoMatch));
            }
        }

        [Test]
        public void LiveVersion2UpgradesWithoutChangingThresholdsOrOrder()
        {
            var saved=new PoseSettings {rulesVersion=2,priority=PoseCatalog.DefaultPriority(2),version="2.0-custom"};
            saved.thumb.extendedDegrees=155; saved.thumb.bentDegrees=120;
            saved.priority=saved.priority.Reverse().ToArray();
            var current=saved.CopyForLive();
            Assert.That(current.rulesVersion,Is.EqualTo(7)); Assert.That(current.version,Is.Not.EqualTo(saved.version));
            Assert.That(current.priority,Is.EqualTo(saved.priority.Where(p=>p!=PoseId.Fuck)));
            Assert.That(current.thumb.extendedDegrees,Is.EqualTo(155)); Assert.That(current.thumb.bentDegrees,Is.EqualTo(120));
            Assert.That(saved.rulesVersion,Is.EqualTo(2));
            var frame=SyntheticHands.CreatePattern(FingerState.Bent,PoseCatalog.Find(PoseId.Open).Fingers,HandId.Left,0,0);
            Assert.That(new PoseRuleEvaluator(current).Evaluate(frame).RawPose,Is.EqualTo(PoseId.Other));
            Assert.That(new PoseRuleEvaluator(saved).Evaluate(frame).RawPose,Is.EqualTo(PoseId.Open));
        }

        [Test]
        public void SavedVersion2CsvPreservesThumbFreeRulesOnReplay()
        {
            string root=Path.Combine(Path.GetTempPath(),"handpose-v2replay-"+Guid.NewGuid().ToString("N"));
            using(var recorder=new PoseCsvRecorder())
            try
            {
                var settings=new PoseSettings {rulesVersion=2,priority=PoseCatalog.DefaultPriority(2),version="2.0-thumb"};
                var pipeline=new PosePipeline(settings); var events=new List<PoseEvent>();var expected=new List<string>();
                Assert.That(recorder.Start(root,new SessionMetadata {settings=settings},512),Is.True);
                int id=0;
                foreach(var pose in new[] {PoseId.Open,PoseId.Fist,PoseId.Victory,PoseId.Point})
                foreach(var thumb in new[] {FingerState.Bent,FingerState.Extended})
                {
                    events.Clear();
                    var left=pipeline.Process(SyntheticHands.CreatePattern(thumb,PoseCatalog.Find(pose).Fingers,HandId.Left,id,id),events);
                    var right=pipeline.Process(SyntheticHands.CreatePattern(thumb,PoseCatalog.Find(pose).Fingers,HandId.Right,id,id),events);
                    Assert.That(left.Rules.Candidates[(int)pose],Is.EqualTo(RuleStatus.Match));
                    recorder.Record(left,right,events); expected.Add(Signature(left));expected.Add(Signature(right));id++;
                }
                recorder.Stop();var session=CsvSessionReader.Read(recorder.DirectoryPath);
                Assert.That(session.Metadata.settings.rulesVersion,Is.EqualTo(2));
                var replay=new PosePipeline(session.Metadata.settings); var actual=new List<string>();
                foreach(var batch in session.Batches)
                {events.Clear();actual.Add(Signature(replay.Process(batch.Left,events)));actual.Add(Signature(replay.Process(batch.Right,events)));}
                Assert.That(actual,Is.EqualTo(expected));
            }
            finally
            {
                recorder.Dispose();
                string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
                if(!string.Equals(Path.GetDirectoryName(full),temp,StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("handpose-v2replay-",StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe test cleanup path.");
                if(Directory.Exists(full)) Directory.Delete(full,true);
            }
        }
        static string Signature(PoseObservation o) => o.Rules.RawPose+":"+o.Stable.Pose+":"+o.Stable.Ready+":"+string.Join("/",o.Rules.Candidates);
    }
}
