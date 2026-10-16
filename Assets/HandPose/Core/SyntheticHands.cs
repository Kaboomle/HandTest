using System;
using UnityEngine;

namespace HandPose
{
    public static class SyntheticHands
    {
        // Geometric fixtures, not a hand-tracking accuracy model.
        public static HandFrame Create(PoseId pose,HandId hand,long frameId,double timestamp,float? ratio=null,int rulesVersion=PoseCatalog.CurrentRulesVersion)
        {
            var rule=PoseCatalog.Find(pose,rulesVersion);
            if(pose==PoseId.Fuck && rule==null) pose=PoseId.Other;
            var states=new FingerState[4];
            // Ring/little extended with index/middle bent matches no registered pose.
            for(int i=0;i<4;i++) states[i]=pose==PoseId.Other?(i>=2?FingerState.Extended:FingerState.Bent):
                rule!=null && !rule.UsesDistance?rule.Fingers[i]:FingerState.Extended;
            var thumb=pose==PoseId.Other?FingerState.Intermediate:(rule?.Thumb??FingerState.Bent);
            var f=CreatePattern(thumb,states,hand,frameId,timestamp,ratio??(pose==PoseId.Pinch?.15f:1.2f),rulesVersion:rulesVersion);
            f.HandValid=pose!=PoseId.TrackingUnavailable;
            return f;
        }
        public static HandFrame CreatePattern(FingerState thumb,FingerState[] fingers,HandId hand,long id,double time,float ratio=1.2f,float? thumbIpDegrees=null,int rulesVersion=PoseCatalog.CurrentRulesVersion)
        {
            if(fingers==null || fingers.Length!=4) throw new ArgumentException("Four finger states are required.");
            var f=new HandFrame {FrameId=id,Timestamp=time,Hand=hand,HandValid=true,SdkActive=1};
            for(int i=0;i<26;i++) f.Joints[i]=new JointSample {PositionValid=true,OrientationValid=true,Orientation=Quaternion.identity};
            f.Joints[0].Position=new Vector3(0,-.04f,0); f.Joints[1].Position=new Vector3(0,-.08f,0);
            for(int i=0;i<4;i++)
            {
                float angle=Angle(fingers[i]); int k=(int)HandFeatureExtractor.FingerJoints[i][0];
                var p=new Vector3(-.03f+i*.02f,0,0);
                f.Joints[k-1].Position=p-new Vector3(0,.02f,0); f.Joints[k].Position=p;
                f.Joints[k+1].Position=p+new Vector3(0,.03f,0);
                var v=new Vector3(0,Mathf.Cos((180-angle)*Mathf.Deg2Rad),Mathf.Sin((180-angle)*Mathf.Deg2Rad));
                f.Joints[k+2].Position=f.Joints[k+1].Position+v*.025f; f.Joints[k+3].Position=f.Joints[k+2].Position+v*.02f;
            }
            // Backward construction keeps MCP / IP angles independent of Pinch's tip distance.
            var tip=f.Joints[(int)JointId.IndexTip].Position+new Vector3(-.06f*ratio,0,0);
            var distalDirection=new Vector3(-.75f,.65f,0).normalized;
            var turn=Quaternion.AngleAxis(180-Angle(thumb),Vector3.forward);
            var proximalDirection=Quaternion.AngleAxis(180-(thumbIpDegrees??Angle(thumb)),Vector3.forward)*distalDirection; var metacarpalDirection=turn*proximalDirection;
            if(rulesVersion>=6)
            {
                var reference=(f.Joints[(int)JointId.MiddleProximal].Position-f.Joints[(int)JointId.Wrist].Position).normalized;
                float baseAngle=thumb==FingerState.Extended?65:thumb==FingerState.Bent?10:30;
                metacarpalDirection=Quaternion.AngleAxis(baseAngle,Vector3.forward)*reference;
                proximalDirection=Quaternion.AngleAxis(-(180-Angle(thumb)),Vector3.forward)*metacarpalDirection;
                distalDirection=Quaternion.AngleAxis(-(180-(thumbIpDegrees??Angle(thumb))),Vector3.forward)*proximalDirection;
            }
            f.Joints[(int)JointId.ThumbTip].Position=tip;
            f.Joints[(int)JointId.ThumbDistal].Position=tip-distalDirection*.025f;
            f.Joints[(int)JointId.ThumbProximal].Position=f.Joints[(int)JointId.ThumbDistal].Position-proximalDirection*.03f;
            f.Joints[(int)JointId.ThumbMetacarpal].Position=f.Joints[(int)JointId.ThumbProximal].Position-metacarpalDirection*.035f;
            if(hand==HandId.Left) for(int i=0;i<26;i++) {var j=f.Joints[i];j.Position.x=-j.Position.x;f.Joints[i]=j;}
            return f;
        }
        static float Angle(FingerState state) => state==FingerState.Extended?180:state==FingerState.Bent?95:140;
    }
}
