using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace HandPose
{
    // Presentation only: consumes the same observed joints as the classifier.
    // It never supplies interpolated or rendered geometry back to recognition.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PoseApplication))]
    public sealed class HandPoseHandView : MonoBehaviour
    {
        public PoseApplication app;
        public bool showHandPreview = true;
        public bool showPoseLabels = true;
        [Min(.001f)] public float jointDiameterMeters = .008f;
        [Min(.001f)] public float boneDiameterMeters = .005f;
        [Min(0)] public float labelGapMeters = .035f;
        [Min(.0001f)] public float labelScale = .0007f;
        public Color leftColor = new Color(.25f, .85f, .75f);
        public Color rightColor = new Color(.97f, .63f, .28f);

        // Named OpenXR joints, including the palm outline and all five fingers.
        static readonly JointId[,] Bones = {
            {JointId.Wrist, JointId.Palm},
            {JointId.Wrist, JointId.ThumbMetacarpal},
            {JointId.ThumbMetacarpal, JointId.ThumbProximal},
            {JointId.ThumbProximal, JointId.ThumbDistal},
            {JointId.ThumbDistal, JointId.ThumbTip},
            {JointId.Wrist, JointId.IndexMetacarpal},
            {JointId.IndexMetacarpal, JointId.IndexProximal},
            {JointId.IndexProximal, JointId.IndexIntermediate},
            {JointId.IndexIntermediate, JointId.IndexDistal},
            {JointId.IndexDistal, JointId.IndexTip},
            {JointId.Wrist, JointId.MiddleMetacarpal},
            {JointId.MiddleMetacarpal, JointId.MiddleProximal},
            {JointId.MiddleProximal, JointId.MiddleIntermediate},
            {JointId.MiddleIntermediate, JointId.MiddleDistal},
            {JointId.MiddleDistal, JointId.MiddleTip},
            {JointId.Wrist, JointId.RingMetacarpal},
            {JointId.RingMetacarpal, JointId.RingProximal},
            {JointId.RingProximal, JointId.RingIntermediate},
            {JointId.RingIntermediate, JointId.RingDistal},
            {JointId.RingDistal, JointId.RingTip},
            {JointId.Wrist, JointId.LittleMetacarpal},
            {JointId.LittleMetacarpal, JointId.LittleProximal},
            {JointId.LittleProximal, JointId.LittleIntermediate},
            {JointId.LittleIntermediate, JointId.LittleDistal},
            {JointId.LittleDistal, JointId.LittleTip},
            {JointId.IndexProximal, JointId.MiddleProximal},
            {JointId.MiddleProximal, JointId.RingProximal},
            {JointId.RingProximal, JointId.LittleProximal}
        };
        sealed class HandVisual
        {
            public Transform Root, Geometry;
            public readonly Transform[] Joints = new Transform[HandFrame.JointCount];
            public readonly Transform[] Segments = new Transform[Bones.GetLength(0)];
            public Canvas Label;
            public Text Title, Pose, Detail;
            public Material Material;
        }
        readonly HandVisual[] hands = new HandVisual[2];
        Font font;

        void Start()
        {
            app = app ? app : GetComponent<PoseApplication>();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int h = 0; h < hands.Length; h++) hands[h] = CreateHand((HandId)h);
        }
        HandVisual CreateHand(HandId hand)
        {
            var v = new HandVisual();
            v.Root = new GameObject(hand + " hand preview").transform;
            v.Root.SetParent(app.InputOrigin ? app.InputOrigin : app.transform, false);
            v.Geometry = new GameObject("Observed skeleton").transform;
            v.Geometry.SetParent(v.Root, false);
            var source = Resources.Load<Material>("JointMarkerMaterial");
            v.Material = source ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            v.Material.color = hand == HandId.Left ? leftColor : rightColor;
            if (v.Material.HasProperty("_BaseColor")) v.Material.SetColor("_BaseColor", v.Material.color);
            for (int j = 0; j < v.Joints.Length; j++)
                v.Joints[j] = Primitive(PrimitiveType.Sphere, ((JointId)j).ToString(), v.Geometry, v.Material);
            for (int b = 0; b < v.Segments.Length; b++)
                v.Segments[b] = Primitive(PrimitiveType.Cylinder, Bones[b, 0] + " - " + Bones[b, 1], v.Geometry, v.Material);

            var label = new GameObject(hand + " pose label", typeof(RectTransform), typeof(Canvas));
            label.transform.SetParent(v.Root, false);
            v.Label = label.GetComponent<Canvas>();
            v.Label.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)label.transform;
            rect.sizeDelta = new Vector2(360, 126);
            rect.localScale = Vector3.one * labelScale;
            // Normal world-space depth testing: labels cannot show through other scene geometry.
            var background = label.AddComponent<Image>(); background.color = new Color(.025f, .045f, .08f, .96f); background.raycastTarget = false;
            var stripe = ChildRect("Hand color", rect, new Vector2(-177, 0), new Vector2(6, 126));
            var image = stripe.gameObject.AddComponent<Image>(); image.color = v.Material.color; image.raycastTarget = false;
            v.Title = Text("Hand / input", rect, 43, 22, 17, v.Material.color);
            v.Pose = Text("Current pose", rect, 4, 54, 38, Color.white);
            v.Detail = Text("Raw / pending", rect, -40, 28, 16, new Color(.7f, .78f, .88f));
            v.Root.gameObject.SetActive(false);
            return v;
        }
        Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var obj = GameObject.CreatePrimitive(type); obj.name = name; obj.transform.SetParent(parent, false);
            var collider = obj.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            var renderer = obj.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            return obj.transform;
        }
        static RectTransform ChildRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
        }
        Text Text(string name, Transform parent, float y, float height, int size, Color color)
        {
            var text = ChildRect(name, parent, new Vector2(0, y), new Vector2(336, height)).gameObject.AddComponent<Text>();
            text.font = font; text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow; text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
            return text;
        }
        void LateUpdate()
        {
            if (!app) return;
            UpdateHand(HandId.Left, app.Left); UpdateHand(HandId.Right, app.Right);
        }
        void UpdateHand(HandId hand, PoseObservation observation)
        {
            var v = hands[(int)hand]; if (v == null) return;
            // Native joints and the tracked head share XR tracking space. The camera's
            // parent also carries XR Origin's Device-mode height offset and recentering.
            var origin = app.InputOrigin ? app.InputOrigin : app.transform;
            if (v.Root.parent != origin) v.Root.SetParent(origin, false);
            var frame = observation?.Frame;
            bool visible = app.isActiveAndEnabled && frame != null && frame.HandValid && (showHandPreview || showPoseLabels);
            int validPoints = 0;
            if (visible) for (int j = 0; j < frame.Joints.Length; j++) if (frame.Joints[j].HasPosition) validPoints++;
            visible &= validPoints > 0;
            v.Root.gameObject.SetActive(visible); if (!visible) return;
            v.Geometry.gameObject.SetActive(showHandPreview);
            for (int j = 0; j < v.Joints.Length; j++)
            {
                bool valid = frame.Joints[j].HasPosition;
                v.Joints[j].gameObject.SetActive(valid);
                if (valid) { v.Joints[j].localPosition = frame.Joints[j].Position; v.Joints[j].localScale = Vector3.one * jointDiameterMeters; }
            }
            for (int b = 0; b < v.Segments.Length; b++)
            {
                bool valid = frame.TryPosition(Bones[b, 0], out var a) && frame.TryPosition(Bones[b, 1], out _);
                var end = frame.Joints[(int)Bones[b, 1]].Position;
                var delta = end - a; float length = valid ? delta.magnitude : 0;
                valid &= Numeric.Finite(length) && length > .000001f;
                var bone = v.Segments[b]; bone.gameObject.SetActive(valid);
                if (!valid) continue;
                bone.localPosition = (a + end) * .5f;
                bone.localRotation = Quaternion.FromToRotation(Vector3.up, delta / length);
                // Unity's cylinder has unit diameter and a height of two.
                bone.localScale = new Vector3(boneDiameterMeters, length * .5f, boneDiameterMeters);
            }
            var camera = app.headCamera;
            v.Label.gameObject.SetActive(showPoseLabels && camera && camera.isActiveAndEnabled);
            if (!v.Label.gameObject.activeSelf) return;
            v.Label.worldCamera = camera;
            Vector3 center = Vector3.zero; float top = float.NegativeInfinity;
            var up = camera.transform.up;
            for (int j = 0; j < frame.Joints.Length; j++)
            {
                if (!frame.Joints[j].HasPosition) continue;
                var world = v.Root.TransformPoint(frame.Joints[j].Position);
                center += world; top = Mathf.Max(top, Vector3.Dot(world, up));
            }
            center /= validPoints;
            float halfHeight = 63 * labelScale;
            var position = center + up * (top - Vector3.Dot(center, up) + labelGapMeters + halfHeight);
            var facing = position - camera.transform.position;
            if (facing.sqrMagnitude < .000001f) { v.Label.gameObject.SetActive(false); return; }
            v.Label.transform.position = position;
            v.Label.transform.rotation = Quaternion.LookRotation(facing, up);
            v.Label.transform.localScale = Vector3.one * labelScale;
            UpdateText(v, hand, observation);
        }
        void UpdateText(HandVisual v, HandId hand, PoseObservation observation)
        {
            string source = app.Source;
            string badge = source.IndexOf("synthetic", StringComparison.OrdinalIgnoreCase) >= 0 ? "SYNTHETIC" :
                source.IndexOf("replay", StringComparison.OrdinalIgnoreCase) >= 0 ? "REPLAY" : "PICO";
            SetText(v.Title, (hand == HandId.Left ? "LEFT" : "RIGHT") + " | " + badge);
            string pose;
            if (!observation.Rules.InputValid) pose = "TrackingUnavailable";
            else if (!observation.Stable.Ready) pose = "Waiting...";
            else pose = observation.Stable.Pose.ToString();
            v.Pose.fontSize = pose == "TrackingUnavailable" ? 24 : 38;
            v.Pose.color = !observation.Rules.InputValid ? new Color(1, .48f, .45f) : observation.Stable.Ready ? Color.white : new Color(1, .82f, .42f);
            SetText(v.Pose, pose);
            string detail = "raw: " + observation.Rules.RawPose;
            if (observation.Stable.HasPending) detail += " | pending: " + observation.Stable.Pending;
            SetText(v.Detail, detail);
        }
        static void SetText(Text text, string value) { if (text.text != value) text.text = value; }
        void OnDisable() { foreach (var v in hands) if (v != null && v.Root) v.Root.gameObject.SetActive(false); }
        void OnDestroy()
        {
            foreach (var v in hands)
            {
                if (v == null) continue;
                if (v.Root) Destroy(v.Root.gameObject);
                if (v.Material) Destroy(v.Material);
            }
        }
    }
}
