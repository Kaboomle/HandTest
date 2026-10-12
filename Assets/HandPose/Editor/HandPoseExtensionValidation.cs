using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace HandPose.Editor
{
    [InitializeOnLoad]
    public static class HandPoseExtensionValidation
    {
        const string Key="HandPose.ThumbCapture";
        static double started, changed;
        static int index=-1;
        static HandPoseExtensionValidation()
        {
            if(!SessionState.GetBool(Key,false)) return;
            started=changed=EditorApplication.timeSinceStartup; index=SessionState.GetInt(Key+".Index",-1);
            EditorApplication.update+=Tick;
        }
        public static void Run()
        {
            HandPoseValidation.GenerateAndBenchmark();
            GenerateIntermediateProbe();
            GenerateMcpProbe();
            EditorSceneManager.OpenScene(HandPoseProjectSetup.DemoScene);
            SessionState.SetBool(Key,true); SessionState.SetInt(Key+".Index",-1);
            index=-1;started=changed=EditorApplication.timeSinceStartup;EditorApplication.update+=Tick;EditorApplication.isPlaying=true;
        }
        static PoseId RightPose(int i) => i==5?PoseId.ThumbsUp:PoseCatalog.Rules[(i+1)%PoseCatalog.Rules.Length].Id;
        static void Select(PoseApplication app,int next)
        {
            index=next;SessionState.SetInt(Key+".Index",index);changed=EditorApplication.timeSinceStartup;
            if(index<PoseCatalog.Rules.Length)
            { app.synthetic.leftPose=PoseCatalog.Rules[index].Id; app.synthetic.rightPose=RightPose(index); }
            else
            {
                app.LoadReplay(SessionState.GetString(Key+(index==PoseCatalog.Rules.Length?".McpReplayPath":".ReplayPath"),""));
                Require(app.replay.Session!=null,"Intermediate probe did not load: "+app.Status);
                app.replay.Step(); app.replay.Step(); app.replay.Step();
            }
        }
        static void GenerateIntermediateProbe()
        {
            var settings=new PoseSettings(); var pipeline=new PosePipeline(settings);var events=new List<PoseEvent>();
            using(var recorder=new PoseCsvRecorder())
            {
                Require(recorder.Start("Validation/IntermediateThumbSessions",new SessionMetadata {settings=settings,source="SYNTHETIC intermediate thumb fixture; not PICO sensor observations",unity_version=Application.unityVersion,sdk_version="3.4.0"},512),recorder.LastError);
                for(int i=0;i<3;i++)
                {
                    var left=SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Fist).Fingers,HandId.Left,i,i*.11);
                    var right=SyntheticHands.CreatePattern(FingerState.Intermediate,PoseCatalog.Find(PoseId.Point).Fingers,HandId.Right,i,i*.11);
                    foreach(var frame in new[] {left,right}) for(int j=0;j<26;j++)
                    {var sample=frame.Joints[j];sample.Position+=new Vector3(frame.Hand==HandId.Left?-.15f:.15f,1.15f,.6f);frame.Joints[j]=sample;}
                    events.Clear();recorder.Record(pipeline.Process(left,events),pipeline.Process(right,events),events);
                }
                recorder.Stop();Require(string.IsNullOrEmpty(recorder.LastError),recorder.LastError);
                string path=Path.GetFullPath(recorder.DirectoryPath);SessionState.SetString(Key+".ReplayPath",path);
                File.WriteAllText("Validation/Logs/thumb-base-angle-session-path.txt",path);
            }
        }
        static void GenerateMcpProbe()
        {
            var settings=new PoseSettings();var pipeline=new PosePipeline(settings);var events=new List<PoseEvent>();
            using(var recorder=new PoseCsvRecorder())
            {
                Require(recorder.Start("Validation/McpThumbSessions",new SessionMetadata {settings=settings,source="SYNTHETIC MCP 180 / IP 95 fixture; not PICO sensor observations",unity_version=Application.unityVersion,sdk_version="3.4.0"},512),recorder.LastError);
                for(int i=0;i<3;i++)
                {
                    var left=SyntheticHands.CreatePattern(FingerState.Extended,PoseCatalog.Find(PoseId.Gun).Fingers,HandId.Left,i,i*.11,thumbIpDegrees:95);
                    var right=SyntheticHands.CreatePattern(FingerState.Extended,PoseCatalog.Find(PoseId.ThumbsUp).Fingers,HandId.Right,i,i*.11,thumbIpDegrees:95);
                    foreach(var frame in new[] {left,right}) for(int j=0;j<26;j++)
                    {var sample=frame.Joints[j];sample.Position+=new Vector3(frame.Hand==HandId.Left?-.15f:.15f,1.15f,.6f);frame.Joints[j]=sample;}
                    events.Clear();recorder.Record(pipeline.Process(left,events),pipeline.Process(right,events),events);
                }
                recorder.Stop();Require(string.IsNullOrEmpty(recorder.LastError),recorder.LastError);
                string path=Path.GetFullPath(recorder.DirectoryPath);SessionState.SetString(Key+".McpReplayPath",path);
                File.WriteAllText("Validation/Logs/thumb-base-angle-mcp-session-path.txt",path);
            }
        }
        static void Require(bool value,string reason) {if(!value) throw new InvalidOperationException(reason);}
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup-started>90) throw new TimeoutException("Thumb UI capture timed out.");
                if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
                var app=UnityEngine.Object.FindFirstObjectByType<PoseApplication>();
                if(!app || app.Left==null || app.Right==null) return;
                if(index<0) {Select(app,0);return;}
                if(EditorApplication.timeSinceStartup-changed<.35) return;
                bool mcpProbe=index==PoseCatalog.Rules.Length;
                bool intermediateProbe=index==PoseCatalog.Rules.Length+1;
                var expectedLeft=intermediateProbe?PoseId.Fist:mcpProbe?PoseId.Gun:PoseCatalog.Rules[index].Id;
                var expectedRight=intermediateProbe?PoseId.Point:mcpProbe?PoseId.ThumbsUp:RightPose(index);
                // Editor callbacks can run before the next rendered Update, especially during startup.
                bool stabilized=app.Left.Stable.Ready && app.Left.Stable.Pose==expectedLeft && app.Right.Stable.Ready && app.Right.Stable.Pose==expectedRight;
                if(!stabilized && EditorApplication.timeSinceStartup-changed<8) return;
                Require(stabilized,$"Expected {expectedLeft}/{expectedRight}; observed raw={app.Left.Rules.RawPose}/{app.Right.Rules.RawPose}, stable={app.Left.Stable.Pose}/{app.Right.Stable.Pose}, ready={app.Left.Stable.Ready}/{app.Right.Stable.Ready}.");
                Require(app.ActiveSettings.rulesVersion==6,"Active rule version is not 6.");
                foreach(var observation in new[] {app.Left,app.Right})
                {
                    var rule=PoseCatalog.Find(observation.Rules.RawPose);
                    if(rule.Thumb.HasValue) Require(observation.Rules.ThumbRuleState==rule.Thumb.Value,"Rule thumb state differs from the pose definition.");
                    if(mcpProbe)
                    {Require(observation.Rules.Features.Thumb.State==FingerState.Extended,"IP affected base-angle classification.");Require(Math.Abs(observation.Rules.Features.Thumb.BaseAngle-65)<.001,"Base angle differs from fixture.");Require(Math.Abs(observation.Rules.Features.Thumb.Ip-95)<.001,"Bent IP diagnostic was not retained.");}
                    if(intermediateProbe) Require(observation.Rules.Features.Thumb.State==FingerState.Intermediate,"Measured thumb state was not preserved.");
                    if(rule.Id==PoseId.Gun) Require(observation.Rules.Candidates[(int)PoseId.Point]==RuleStatus.NoMatch,"Gun also matches Point.");
                    if(rule.Id==PoseId.ThumbsUp) Require(observation.Rules.Candidates[(int)PoseId.Fist]==RuleStatus.NoMatch,"ThumbsUp also matches Fist.");
                }
                var left=app.InputOrigin.Find("Left hand preview");var right=app.InputOrigin.Find("Right hand preview");
                Require(left && right,"Missing previews.");
                var lp=left.GetComponentInChildren<Canvas>().transform.Find("Current pose").GetComponent<Text>();
                var rp=right.GetComponentInChildren<Canvas>().transform.Find("Current pose").GetComponent<Text>();
                if((lp.text!=expectedLeft.ToString() || rp.text!=expectedRight.ToString()) && EditorApplication.timeSinceStartup-changed<8) return;
                Require(lp.text==expectedLeft.ToString() && rp.text==expectedRight.ToString(),"Hand labels differ from stable poses.");
                var dashboard=GameObject.Find("Pose dashboard");
                Require(dashboard,"Missing dashboard.");
                string details=dashboard.transform.Find("Left").GetComponent<Text>().text;
                string rightDetails=dashboard.transform.Find("Right").GetComponent<Text>().text;
                // Dashboard text is throttled to 10 Hz; wait until it reflects the same stable frame.
                bool synchronized=details.Contains("stable: "+expectedLeft+"    ready: True") && rightDetails.Contains("stable: "+expectedRight+"    ready: True");
                if(!synchronized && EditorApplication.timeSinceStartup-changed<8) return;
                Require(synchronized,"Dashboard stable labels have not caught up with hand labels.");
                Require(details.Contains("Thumb:") && details.Contains("Base angle"),"Thumb angles are absent.");
                foreach(var rule in PoseCatalog.Rules) Require(details.Contains(rule.Id+":"),"Missing candidate "+rule.Id);
                Require(!details.Contains("Fuck:"),"Retired candidate remains in dashboard.");
                Require(!dashboard.transform.Find("Definitions").GetComponent<Text>().text.Contains("9 static"),"Dashboard still lists nine poses.");
                if(intermediateProbe)
                    Require(details.Contains("Intermediate (as Bent)") && rightDetails.Contains("Intermediate (as Bent)"),"Intermediate thumb interpretation is missing.");
                if(mcpProbe)
                {
                    var view=app.GetComponent<HandPoseHandView>();var debug=app.GetComponent<PoseDebugView>();
                    var camera=app.headCamera;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
                    view.showHandPreview=view.showPoseLabels=false;view.SendMessage("LateUpdate");Capture(camera,"thumb-base-angle-dashboard.png");
                    debug.enabled=false;view.showHandPreview=view.showPoseLabels=true;
                    camera.transform.position=new Vector3(0,1.28f,0);camera.transform.rotation=Quaternion.identity;view.SendMessage("LateUpdate");
                    Capture(camera,"thumb-base-angle-hands.png");
                    camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;debug.enabled=true;
                }
                if(!intermediateProbe) Select(app,index+1);
                else
                {
                    Directory.CreateDirectory("Validation/Logs");
                    File.WriteAllText("Validation/Logs/thumb-base-angle-ui-smoke.txt","Passed: rulesVersion=6; all eight canonical synthetic poses stabilized independently for both hands in the saved demo scene; hand labels match stable poses; Gun excludes Point and ThumbsUp excludes Fist; a recorded intermediate-thumb probe replays as left Fist and right Point with stable hand labels; Base opening 65 / IP 95 replay recognizes Gun and ThumbsUp; dashboard excludes retired candidate and shows base-angle definition; Intermediate replay retains Intermediate (as Bent) and base opening; measured features remain Intermediate; dashboard and hand screenshots rendered.\nSynthetic input only; physical thumb tracking and thresholds are not validated. Screenshots use a temporary inspection camera and visibility controls; saved scene is unchanged.\n");
                    Finish(0,"Base-angle thumb rule version 6 PlayMode UI validation passed.");
                }
            }
            catch(Exception ex) {Finish(1,ex.ToString());}
        }
        static void Capture(Camera camera,string name)
        {
            Directory.CreateDirectory("Validation/Logs");var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
            var target=new RenderTexture(1600,1100,24);var pixels=new Texture2D(1600,1100,TextureFormat.RGB24,false);
            try
            {camera.targetTexture=target;camera.Render();RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,1600,1100),0,0);pixels.Apply();File.WriteAllBytes("Validation/Logs/"+name,pixels.EncodeToPNG());}
            finally
            {camera.targetTexture=previousTarget;RenderTexture.active=previousActive;UnityEngine.Object.DestroyImmediate(pixels);UnityEngine.Object.DestroyImmediate(target);}
        }
        static void Finish(int code,string text)
        {SessionState.SetBool(Key,false);EditorApplication.update-=Tick;if(code==0) Debug.Log(text);else Debug.LogError(text);EditorApplication.isPlaying=false;EditorApplication.Exit(code);}
    }
}
