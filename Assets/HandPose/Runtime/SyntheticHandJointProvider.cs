using UnityEngine;

namespace HandPose
{
    public sealed class SyntheticHandJointProvider : MonoBehaviour, IHandJointProvider
    {
        public PoseId leftPose=PoseId.Open, rightPose=PoseId.Victory;
        public bool cyclePoses;
        long frameId;
        public string SourceDescription => "SYNTHETIC fixtures (not sensor data)";
        public bool TryGetBatch(double appTime,out HandInputBatch batch)
        {
            if (cyclePoses) { leftPose=PoseCatalog.DemoPoses[(int)(appTime/3)%PoseCatalog.DemoPoses.Length]; rightPose=PoseCatalog.DemoPoses[(int)(appTime/3+2)%PoseCatalog.DemoPoses.Length]; }
            batch=new HandInputBatch { Left=SyntheticHands.Create(leftPose,HandId.Left,frameId,appTime), Right=SyntheticHands.Create(rightPose,HandId.Right,frameId++,appTime) };
            foreach (var f in new[] {batch.Left,batch.Right})
                for(int i=0;i<26;i++) { var j=f.Joints[i]; j.Position+=new Vector3(f.Hand==HandId.Left ? -0.15f : 0.15f,1.15f,0.6f); f.Joints[i]=j; }
            return true;
        }
    }
}
