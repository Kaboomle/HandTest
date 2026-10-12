using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HandPose.Editor
{
    // Retired serialized IDs remain readable, but cannot be chosen for new settings or fixtures.
    [CustomPropertyDrawer(typeof(PoseId))]
    public sealed class PoseIdDrawer : PropertyDrawer
    {
        static readonly GUIContent[] labels=PoseCatalog.DemoPoses.Select(p=>new GUIContent(p.ToString())).ToArray();
        static readonly int[] values=PoseCatalog.DemoPoses.Select(p=>(int)p).ToArray();
        public override void OnGUI(Rect position,SerializedProperty property,GUIContent label)
        {
            EditorGUI.BeginProperty(position,label,property);
            EditorGUI.BeginChangeCheck();
            int selected=EditorGUI.IntPopup(position,label,property.intValue,labels,values);
            if(EditorGUI.EndChangeCheck()) property.intValue=selected;
            EditorGUI.EndProperty();
        }
    }
}
