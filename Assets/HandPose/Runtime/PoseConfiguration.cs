using UnityEngine;

namespace HandPose
{
    [CreateAssetMenu(menuName="Hand Pose/Configuration")]
    public sealed class PoseConfiguration : ScriptableObject
    {
        public PoseSettings settings = new PoseSettings();
        public bool autoRecord;
        [Min(8)] public int queueCapacity = 512;
        [Min(0.3f)] public float gazeDwellSeconds = 1.2f;
        public string participantId = "";
    }
}
