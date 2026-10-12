#if PICO_LIVE_PREVIEW
using System;
using UnityEngine;
using UnityEngine.XR.Management;
using Unity.XR.PICO.LivePreview;
using PreviewJoint = Unity.XR.PICO.LivePreview.HandJoint;

namespace HandPose.Editor
{
    // Editor transport only. Android builds continue to use PicoHandJointProvider.
    public sealed class LivePreviewHandJointProvider : IHandJointProvider
    {
        static readonly PreviewJoint[] Mapping = {
            PreviewJoint.JointPalm, PreviewJoint.JointWrist,
            PreviewJoint.JointThumbMetacarpal, PreviewJoint.JointThumbProximal, PreviewJoint.JointThumbDistal, PreviewJoint.JointThumbTip,
            PreviewJoint.JointIndexMetacarpal, PreviewJoint.JointIndexProximal, PreviewJoint.JointIndexIntermediate, PreviewJoint.JointIndexDistal, PreviewJoint.JointIndexTip,
            PreviewJoint.JointMiddleMetacarpal, PreviewJoint.JointMiddleProximal, PreviewJoint.JointMiddleIntermediate, PreviewJoint.JointMiddleDistal, PreviewJoint.JointMiddleTip,
            PreviewJoint.JointRingMetacarpal, PreviewJoint.JointRingProximal, PreviewJoint.JointRingIntermediate, PreviewJoint.JointRingDistal, PreviewJoint.JointRingTip,
            PreviewJoint.JointLittleMetacarpal, PreviewJoint.JointLittleProximal, PreviewJoint.JointLittleIntermediate, PreviewJoint.JointLittleDistal, PreviewJoint.JointLittleTip
        };
        HandJointLocations left = NewLocations(), right = NewLocations();
        readonly string[] lastState = new string[2];
        long nextFrame;
        public string SourceDescription => "PICO Live Preview GetJointLocations / PDC / plugin 1.0.5";

        [UnityEditor.InitializeOnLoadMethod]
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => PoseApplication.EditorLivePreviewProviderFactory = CreateIfActive;

        static IHandJointProvider CreateIfActive() => ActiveLoader() != null ? new LivePreviewHandJointProvider() : null;
        static PXR_PTLoader ActiveLoader() => XRGeneralSettings.Instance?.Manager?.activeLoader as PXR_PTLoader;
        public static HandJointLocations NewLocations() => new HandJointLocations {
            jointCount = HandFrame.JointCount,
            jointLocations = new HandJointLocation[HandFrame.JointCount]
        };

        public bool TryGetBatch(double appTime, out HandInputBatch batch)
        {
            long id = nextFrame++;
            batch = new HandInputBatch {
                Left = Read(HandId.Left, id, appTime, ref left),
                Right = Read(HandId.Right, id, appTime, ref right)
            };
            return true;
        }

        HandFrame Read(HandId hand, long id, double time, ref HandJointLocations locations)
        {
            HandFrame frame;
            try
            {
                ClearSample(ref locations);
                var loader = ActiveLoader();
                if (loader?.displaySubsystem == null || !loader.displaySubsystem.running)
                    frame = new HandFrame { Hand=hand, FrameId=id, Timestamp=time, InvalidReason="LivePreviewNotRunning" };
                else
                {
                    PXR_PTApi.UPxr_GetHandTrackerJointLocations(hand == HandId.Left ? 0 : 1, ref locations);
                    frame = ConvertFrame(locations, hand, id, time);
                }
            }
            catch (Exception ex)
            {
                frame = new HandFrame { Hand=hand, FrameId=id, Timestamp=time, InvalidReason="LivePreviewSdkException:"+ex.GetType().Name };
            }
            string state = $"active={frame.SdkActive}, joints={locations.jointCount}, valid={frame.HandValid}, {frame.InvalidReason}";
            if (lastState[(int)hand] != state)
            {
                lastState[(int)hand] = state;
                Debug.Log($"[HandPose Live Preview] {hand}: {state}");
            }
            return frame;
        }

        public static void ClearSample(ref HandJointLocations locations)
        {
            locations.isActive = 0;
            // Preserve the SDK's fixed array capacity while clearing every previous validity flag.
            locations.jointCount = HandFrame.JointCount;
            if (locations.jointLocations == null || locations.jointLocations.Length != HandFrame.JointCount)
                locations.jointLocations = new HandJointLocation[HandFrame.JointCount];
            else Array.Clear(locations.jointLocations, 0, locations.jointLocations.Length);
        }

        public static HandFrame ConvertFrame(HandJointLocations locations, HandId hand, long id, double time)
        {
            var frame = new HandFrame { Hand=hand, FrameId=id, Timestamp=time, SdkActive=locations.isActive };
            frame.HandValid = locations.isActive != 0 && locations.jointCount > 0 && locations.jointLocations != null;
            frame.InvalidReason = locations.isActive == 0 ? "LivePreviewHandInactive" : (!frame.HandValid ? "LivePreviewMissingJoints" : "");
            if (locations.jointLocations == null) return frame;
            for (int i=0; i<Mapping.Length; i++)
            {
                int nativeIndex = (int)Mapping[i];
                if (nativeIndex >= locations.jointCount || nativeIndex >= locations.jointLocations.Length) continue;
                var joint = locations.jointLocations[nativeIndex];
                // SDK conversion changes handedness once, exactly as its XR Hands provider does.
                var sample = new JointSample {
                    Position=joint.pose.Position.ToVector3(), Orientation=joint.pose.Orientation.ToQuat(),
                    SdkFlags=(ulong)joint.locationStatus
                };
                sample.PositionValid = frame.HandValid && (joint.locationStatus & HandLocationStatus.PositionValid) != 0 && Numeric.Finite(sample.Position);
                sample.OrientationValid = frame.HandValid && (joint.locationStatus & HandLocationStatus.OrientationValid) != 0 && Numeric.ValidQuaternion(sample.Orientation);
                frame.Joints[i] = sample;
            }
            return frame;
        }
    }
}
#endif
