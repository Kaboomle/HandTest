using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HandPose
{
    public sealed class TrackingOriginCamera : MonoBehaviour
    {
        readonly List<XRInputSubsystem> subsystems=new List<XRInputSubsystem>();
        bool originRequested;
        public bool IsHeadTracked { get; private set; }
        void Update()
        {
            if(!originRequested)
            {
                SubsystemManager.GetSubsystems(subsystems);
                foreach(var s in subsystems) if(s.running) { s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor); originRequested=true; }
            }
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            IsHeadTracked=head.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked;
            if(IsHeadTracked)
            {
                if(head.TryGetFeatureValue(CommonUsages.devicePosition,out var p)) transform.localPosition=p;
                if(head.TryGetFeatureValue(CommonUsages.deviceRotation,out var q)) transform.localRotation=q;
            }
        }
    }
}
