#if PICO_LIVE_PREVIEW
using System.Collections.Generic;
using HandPose.Editor;
using NUnit.Framework;
using UnityEngine;
using Unity.XR.PICO.LivePreview;

namespace HandPose.Tests
{
    public class LivePreviewHandTests
    {
        [Test]
        public void ConvertsAllNamedJointsAndHandednessOnce()
        {
            var locations = LivePreviewHandJointProvider.NewLocations(); locations.isActive=1;
            for (int i=0; i<26; i++) locations.jointLocations[i] = new HandJointLocation {
                locationStatus=HandLocationStatus.PositionValid | HandLocationStatus.OrientationValid,
                pose=new Posef { Position=new Vector3f {x=i+.1f,y=i+.2f,z=i+.3f}, Orientation=new Quatf {x=.1f,y=.2f,z=.3f,w=.9f} }
            };
            var frame = LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Right,42,1.25);
            Assert.That(frame.HandValid,Is.True); Assert.That(frame.Hand,Is.EqualTo(HandId.Right));
            Assert.That(frame.FrameId,Is.EqualTo(42)); Assert.That(frame.Timestamp,Is.EqualTo(1.25));
            for (int i=0; i<26; i++) {
                Assert.That(frame.Joints[i].Position,Is.EqualTo(new Vector3(i+.1f,i+.2f,-i-.3f)));
                Assert.That(frame.Joints[i].Orientation,Is.EqualTo(new Quaternion(.1f,.2f,-.3f,-.9f)));
                Assert.That(frame.Joints[i].HasPosition && frame.Joints[i].HasOrientation,Is.True);
            }
        }

        [Test]
        public void PositionFlagsDoNotRequireOrientationAndRejectNonFiniteData()
        {
            var locations=FromSynthetic(SyntheticHands.Create(PoseId.Open,HandId.Left,0,0));
            var joint=locations.jointLocations[2]; joint.locationStatus=HandLocationStatus.PositionValid;
            locations.jointLocations[2]=joint;
            joint=locations.jointLocations[3]; joint.pose.Position.x=float.NaN; locations.jointLocations[3]=joint;
            joint=locations.jointLocations[4]; joint.locationStatus=HandLocationStatus.PositionTracked; locations.jointLocations[4]=joint;
            var frame=LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Left,0,0);
            Assert.That(frame.Joints[2].HasPosition,Is.True); Assert.That(frame.Joints[2].HasOrientation,Is.False);
            Assert.That(frame.Joints[3].HasPosition,Is.False); Assert.That(frame.Joints[4].HasPosition,Is.False);
        }

        [Test]
        public void InactiveOrMissingHandCannotReusePreviousCoordinates()
        {
            var locations=FromSynthetic(SyntheticHands.Create(PoseId.Open,HandId.Left,0,0));
            locations.isActive=0;
            var inactive=LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Left,1,.1);
            Assert.That(inactive.HandValid,Is.False); Assert.That(inactive.Joints[2].HasPosition,Is.False);
            locations.isActive=1; locations.jointCount=2;
            Assert.That(LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Left,2,.2).Joints[2].HasPosition,Is.False);
            locations.jointLocations=null;
            Assert.That(LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Left,3,.3).HandValid,Is.False);
            locations=FromSynthetic(SyntheticHands.Create(PoseId.Open,HandId.Left,0,0));
            LivePreviewHandJointProvider.ClearSample(ref locations);
            var cleared=LivePreviewHandJointProvider.ConvertFrame(locations,HandId.Left,4,.4);
            Assert.That(cleared.HandValid,Is.False); Assert.That(cleared.Joints[2].SdkFlags,Is.Zero);
            Assert.That(locations.jointCount,Is.EqualTo(26));
        }

        [Test]
        public void ConvertedHandsReachAllEightPoseRulesAndRemainIndependentOnLoss()
        {
            foreach(var rule in PoseCatalog.Rules) {
                var pipeline=new PosePipeline(new PoseSettings()); var events=new List<PoseEvent>();
                for(int i=0; i<3; i++) foreach(var hand in new[] {HandId.Left,HandId.Right}) {
                    var locations=FromSynthetic(SyntheticHands.Create(rule.Id,hand,i,i*.11));
                    var frame=LivePreviewHandJointProvider.ConvertFrame(locations,hand,i,i*.11);
                    var result=pipeline.Process(frame,events);
                    Assert.That(result.Rules.RawPose,Is.EqualTo(rule.Id));
                    if(i==2) Assert.That(result.Stable.Ready,Is.True);
                }
                var lost=LivePreviewHandJointProvider.ConvertFrame(LivePreviewHandJointProvider.NewLocations(),HandId.Left,3,.33);
                Assert.That(pipeline.Process(lost,events).Rules.RawPose,Is.EqualTo(PoseId.TrackingUnavailable));
                var right=LivePreviewHandJointProvider.ConvertFrame(FromSynthetic(SyntheticHands.Create(rule.Id,HandId.Right,3,.33)),HandId.Right,3,.33);
                Assert.That(pipeline.Process(right,events).Stable.Pose,Is.EqualTo(rule.Id));
            }
        }

        [Test]
        public void AutomaticInputUsesPreviewAndExplicitSyntheticStaysSynthetic()
        {
            var original=PoseApplication.EditorLivePreviewProviderFactory;
            var synthetic=new FakeProvider(); var pico=new FakeProvider(); var live=new FakeProvider();
            try {
                PoseApplication.EditorLivePreviewProviderFactory=()=>live;
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Auto,synthetic,pico),Is.SameAs(live));
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Pico,synthetic,pico),Is.SameAs(live));
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Synthetic,synthetic,pico),Is.SameAs(synthetic));
                PoseApplication.EditorLivePreviewProviderFactory=()=>null;
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Auto,synthetic,pico),Is.SameAs(synthetic));
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Pico,synthetic,pico),Is.SameAs(pico));
                Assert.That(PoseApplication.SelectEditorProvider(EditorInputSource.Replay,synthetic,pico),Is.SameAs(synthetic));
            } finally {PoseApplication.EditorLivePreviewProviderFactory=original;}
        }

        static HandJointLocations FromSynthetic(HandFrame frame)
        {
            var locations=LivePreviewHandJointProvider.NewLocations(); locations.isActive=1;
            for(int i=0;i<26;i++) {
                var p=frame.Joints[i].Position;
                locations.jointLocations[i]=new HandJointLocation {
                    locationStatus=HandLocationStatus.PositionValid | HandLocationStatus.OrientationValid,
                    pose=new Posef {Position=new Vector3f {x=p.x,y=p.y,z=-p.z},Orientation=new Quatf {w=-1}}
                };
            }
            return locations;
        }
        sealed class FakeProvider : IHandJointProvider {
            public string SourceDescription => "Test";
            public bool TryGetBatch(double time,out HandInputBatch batch) {batch=null;return false;}
        }
    }
}
#endif
