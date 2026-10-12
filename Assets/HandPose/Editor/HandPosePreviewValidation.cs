using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace HandPose.Editor
{
    [InitializeOnLoad]
    public static class HandPosePreviewValidation
    {
        const string Key = "HandPose.PreviewValidation";
        static double started, phaseStarted;
        static int phase;
        static HandPosePreviewValidation()
        {
            if (!SessionState.GetBool(Key, false)) return;
            started = phaseStarted = EditorApplication.timeSinceStartup;
            phase = SessionState.GetInt(Key + ".Phase", 0);
            EditorApplication.update += Tick;
        }
        public static void Capture()
        {
            EditorSceneManager.OpenScene(HandPoseProjectSetup.DemoScene);
            SessionState.SetBool(Key, true); SessionState.SetInt(Key + ".Phase", 0);
            started = phaseStarted = EditorApplication.timeSinceStartup; phase = 0;
            EditorApplication.update += Tick; EditorApplication.isPlaying = true;
        }
        static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup - started > 50) throw new TimeoutException("Preview validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var app = UnityEngine.Object.FindFirstObjectByType<PoseApplication>();
                if (!app || app.Left == null || app.Right == null) return;
                var view = app.GetComponent<HandPoseHandView>();
                var left = app.trackingOrigin.Find("Left hand preview");
                var right = app.trackingOrigin.Find("Right hand preview");
                if (!view || !left || !right) return;
                var leftLabel = left.GetComponentInChildren<Canvas>(true);
                var rightLabel = right.GetComponentInChildren<Canvas>(true);
                var leftPose = leftLabel.transform.Find("Current pose").GetComponent<Text>();
                var rightPose = rightLabel.transform.Find("Current pose").GetComponent<Text>();
                if (phase == 0)
                {
                    if (!app.Left.Stable.Ready || !app.Right.Stable.Ready) return;
                    Require(app.headCamera && app.headCamera.isActiveAndEnabled && app.headCamera == Camera.main, "The preview must use the active XR camera.");
                    Require(app.GetComponents<HandPoseHandView>().Length == 1, "Duplicate hand preview component.");
                    Require(left.gameObject.activeSelf && right.gameObject.activeSelf, "Both hands must be visible.");
                    Require(leftPose.text == "Open" && rightPose.text == "Victory", "Expected stable Open / Victory labels.");
                    Require(left.Find("Observed skeleton/IndexProximal - IndexIntermediate").gameObject.activeSelf, "Finger bone missing.");
                    CheckFacingAndHeight(leftLabel, app.Left.Frame, left, app.headCamera);
                    // Isolate the hand UI for its screenshot; the saved scene and camera are unchanged.
                    app.GetComponent<PoseDebugView>().enabled = false;
                    app.headCamera.transform.position = new Vector3(0, 1.28f, 0);
                    app.headCamera.transform.rotation = Quaternion.identity;
                    view.SendMessage("LateUpdate");
                    CaptureImage(app.headCamera, "hand-preview.png");
                    view.enabled = false;
                    Require(!left.gameObject.activeSelf && !right.gameObject.activeSelf, "Disabled view left stale hand objects.");
                    view.enabled = true;
                    app.synthetic.leftPose = PoseId.TrackingUnavailable;
                    Next(1);
                }
                else if (phase == 1 && Elapsed > .3)
                {
                    Require(!left.gameObject.activeSelf && right.gameObject.activeSelf, "Left tracking loss must hide only the left hand.");
                    Require(rightPose.text == "Victory", "Left tracking loss changed the right pose label.");
                    app.synthetic.leftPose = PoseId.Open;
                    Next(2);
                }
                else if (phase == 2 && Elapsed > .3)
                {
                    Require(left.gameObject.activeSelf && leftPose.text == "Open", "Left preview did not recover.");
                    var providerField = typeof(PoseApplication).GetField("provider", BindingFlags.Instance | BindingFlags.NonPublic);
                    var originalProvider = providerField.GetValue(app);
                    providerField.SetValue(app, app.pico); view.SendMessage("LateUpdate");
                    Require(left.parent == app.headCamera.transform.parent, "Native preview did not use XR camera tracking space / height offset.");
                    var indexTip = left.Find("Observed skeleton/IndexTip");
                    var expectedTip = app.headCamera.transform.parent.TransformPoint(app.Left.Frame.Joints[(int)JointId.IndexTip].Position);
                    Require(Vector3.Distance(indexTip.position, expectedTip) < .00001f, "Native preview applied a second coordinate conversion.");
                    providerField.SetValue(app, originalProvider); view.SendMessage("LateUpdate");
                    var settings = new PoseSettings();
                    var sample = SyntheticHands.Create(PoseId.Open, HandId.Left, 1000, 100);
                    for (int j = 0; j < sample.Joints.Length; j++)
                    { var joint = sample.Joints[j]; joint.Position += new Vector3(-.15f, 1.15f, .6f); sample.Joints[j] = joint; }
                    var pipeline = new PosePipeline(settings);
                    var events = new System.Collections.Generic.List<PoseEvent>();
                    InjectLeft(app, pipeline.Process(sample, events)); view.SendMessage("LateUpdate");
                    Require(leftPose.text == "Waiting...", "A pending pose was shown as already stable.");
                    var missing = sample.Joints[(int)JointId.ThumbTip]; missing.PositionValid = false;
                    sample.Joints[(int)JointId.ThumbTip] = missing;
                    InjectLeft(app, pipeline.Process(sample, events)); view.SendMessage("LateUpdate");
                    Require(left.gameObject.activeSelf && leftPose.text == "TrackingUnavailable", "Partial tracking must retain observed geometry and show classification unavailable.");
                    Require(!left.Find("Observed skeleton/ThumbTip").gameObject.activeSelf &&
                            !left.Find("Observed skeleton/ThumbDistal - ThumbTip").gameObject.activeSelf,
                            "An invalid joint or its bone was rendered.");
                    view.showHandPreview = false;
                    view.SendMessage("LateUpdate");
                    Require(!left.Find("Observed skeleton").gameObject.activeSelf && leftLabel.gameObject.activeSelf, "Label visibility must be independent of skeleton visibility.");
                    view.showHandPreview = true;
                    for (int j = 0; j < sample.Joints.Length; j++) { var joint = sample.Joints[j]; joint.PositionValid = false; sample.Joints[j] = joint; }
                    InjectLeft(app, pipeline.Process(sample, events)); view.SendMessage("LateUpdate");
                    Require(!left.gameObject.activeSelf, "A hand without observed positions must not remain on screen.");
                    Directory.CreateDirectory("Validation/Logs");
                    File.WriteAllText("Validation/Logs/hand-preview-smoke.txt", "Passed: actual saved scene entered PlayMode; active XR camera reference; one preview; both skeletons and stable labels; billboard facing and gap above hand; view disable cleanup; independent left tracking loss and recovery; native presentation origin / height offset / single coordinate conversion using synthetic fixtures; pending label; partial invalid joint and bone hidden; independent label toggle; no positions hides stale preview.\nInput: synthetic. Screenshot uses a fixed inspection camera position (0,1.28,0) and hides the dashboard; saved scene is not modified.\nPICO device and physical hand tracking remain unverified.\n");
                    Finish(0, "Hand preview PlayMode validation passed.");
                }
            }
            catch (Exception ex) { Finish(1, ex.ToString()); }
        }
        static void InjectLeft(PoseApplication app, PoseObservation observation)
        { typeof(PoseApplication).GetProperty(nameof(PoseApplication.Left), BindingFlags.Instance | BindingFlags.Public).GetSetMethod(true).Invoke(app, new object[] { observation }); }
        static void CheckFacingAndHeight(Canvas label, HandFrame frame, Transform root, Camera camera)
        {
            var facing = (label.transform.position - camera.transform.position).normalized;
            Require(Vector3.Dot(label.transform.forward, facing) > .999f, "Label does not face the viewer.");
            var corners = new Vector3[4]; ((RectTransform)label.transform).GetWorldCorners(corners);
            float bottom = float.PositiveInfinity, topJoint = float.NegativeInfinity;
            foreach (var corner in corners) bottom = Mathf.Min(bottom, Vector3.Dot(corner, camera.transform.up));
            foreach (var joint in frame.Joints) if (joint.HasPosition) topJoint = Mathf.Max(topJoint, Vector3.Dot(root.TransformPoint(joint.Position), camera.transform.up));
            Require(bottom > topJoint + .02f, "Label overlaps the upper hand joints.");
        }
        static void CaptureImage(Camera camera, string name)
        {
            Directory.CreateDirectory("Validation/Logs");
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var target = new RenderTexture(1600, 1100, 24); var pixels = new Texture2D(1600, 1100, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1600, 1100), 0, 0); pixels.Apply();
                File.WriteAllBytes("Validation/Logs/" + name, pixels.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; RenderTexture.active = oldActive;
                UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
            }
        }
        static double Elapsed => EditorApplication.timeSinceStartup - phaseStarted;
        static void Next(int next) { phase = next; SessionState.SetInt(Key + ".Phase", phase); phaseStarted = EditorApplication.timeSinceStartup; }
        static void Finish(int code, string message)
        {
            SessionState.SetBool(Key, false); EditorApplication.update -= Tick;
            if (code == 0) Debug.Log(message); else Debug.LogError(message);
            EditorApplication.isPlaying = false; EditorApplication.Exit(code);
        }
    }
}
