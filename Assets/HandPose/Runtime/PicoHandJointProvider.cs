using System;
using UnityEngine;
using Unity.XR.PXR;
using PicoJoint = Unity.XR.PXR.HandJoint;

namespace HandPose
{
    public sealed class PicoHandJointProvider : MonoBehaviour, IHandJointProvider
    {
        // Name-to-name mapping is checked against the installed SDK, not a presumed index convention.
        static readonly PicoJoint[] Mapping = {
            PicoJoint.JointPalm, PicoJoint.JointWrist, PicoJoint.JointThumbMetacarpal, PicoJoint.JointThumbProximal,
            PicoJoint.JointThumbDistal, PicoJoint.JointThumbTip, PicoJoint.JointIndexMetacarpal,
            PicoJoint.JointIndexProximal, PicoJoint.JointIndexIntermediate, PicoJoint.JointIndexDistal, PicoJoint.JointIndexTip,
            PicoJoint.JointMiddleMetacarpal, PicoJoint.JointMiddleProximal, PicoJoint.JointMiddleIntermediate,
            PicoJoint.JointMiddleDistal, PicoJoint.JointMiddleTip, PicoJoint.JointRingMetacarpal,
            PicoJoint.JointRingProximal, PicoJoint.JointRingIntermediate, PicoJoint.JointRingDistal, PicoJoint.JointRingTip,
            PicoJoint.JointLittleMetacarpal, PicoJoint.JointLittleProximal, PicoJoint.JointLittleIntermediate,
            PicoJoint.JointLittleDistal, PicoJoint.JointLittleTip
        };
        HandJointLocations left = new HandJointLocations { jointLocations = new HandJointLocation[26] };
        HandJointLocations right = new HandJointLocations { jointLocations = new HandJointLocation[26] };
        long nextFrame;
        public string SourceDescription => "PICO native GetJointLocations / Integration 3.4.0";
        public bool TryGetBatch(double appTime, out HandInputBatch batch)
        {
            long id = nextFrame++;
            batch = new HandInputBatch { Left=Read(HandId.Left,id,appTime,ref left), Right=Read(HandId.Right,id,appTime,ref right) };
            return true;
        }
        static HandFrame Read(HandId hand, long id, double t, ref HandJointLocations locations)
        {
            var f = new HandFrame { FrameId=id, Timestamp=t, Hand=hand };
#if UNITY_ANDROID && !UNITY_EDITOR && !PICO_OPENXR_SDK
            try
            {
                // Clear active/count: never treat a failed call's previous sample as fresh valid data.
                locations.isActive = 0; locations.jointCount = 0;
                if (!PXR_ProjectSetting.GetProjectConfig().handTracking)
                {
                    f.InvalidReason = "HandTrackingDisabledInProjectSettings";
                    return f;
                }
                bool ok = PXR_HandTracking.GetJointLocations(hand == HandId.Left ? HandType.HandLeft : HandType.HandRight,ref locations);
                f.SdkActive = locations.isActive;
                f.HandValid = ok && locations.isActive != 0 && locations.jointLocations != null;
                f.InvalidReason = !ok ? "SdkCallFailedOrHandTrackingDisabled" : (!f.HandValid ? "SdkHandInactive" : "");
                if (!ok || locations.jointLocations == null) return f;
                for (int i=0;i<26;i++)
                {
                    int nativeIndex=(int)Mapping[i];
                    if (nativeIndex >= locations.jointCount || nativeIndex >= locations.jointLocations.Length) continue;
                    var j = locations.jointLocations[nativeIndex];
                    // SDK ToVector3 flips native Z; ToQuat returns (x,y,-z,-w). Do not convert again.
                    var sample = new JointSample { Position=j.pose.Position.ToVector3(), Orientation=j.pose.Orientation.ToQuat(), SdkFlags=(ulong)j.locationStatus };
                    sample.PositionValid = f.HandValid && (j.locationStatus & HandLocationStatus.PositionValid) != 0 && Numeric.Finite(sample.Position);
                    sample.OrientationValid = f.HandValid && (j.locationStatus & HandLocationStatus.OrientationValid) != 0 && Numeric.ValidQuaternion(sample.Orientation);
                    f.Joints[i] = sample;
                }
            }
            catch (Exception ex) { f.HandValid=false; f.InvalidReason="SdkException:"+ex.GetType().Name; }
#else
            f.InvalidReason = "PicoNativeInputRequiresStandaloneAndroid";
#endif
            return f;
        }
    }
}
