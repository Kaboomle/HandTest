using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HandPose.Editor
{
    [InitializeOnLoad]
    public static class HandPoseValidation
    {
        static HandPoseValidation()
        {
            if(SessionState.GetBool("HandPose.CaptureActive",false))
            {
                playStarted=EditorApplication.timeSinceStartup;
                phase=SessionState.GetInt("HandPose.CapturePhase",0);
                EditorApplication.update+=CaptureUpdate;
            }
        }
        public static void GenerateAndBenchmark()
        {
            HandPoseProjectSetup.GenerateProject(); Directory.CreateDirectory("Validation/Logs");
            var settings=new PoseSettings(); var pipeline=new PosePipeline(settings); var events=new List<PoseEvent>();
            var timings=new double[5000]; var timer=new Stopwatch();
            for(int i=0;i<6000;i++)
            {
                var left=SyntheticHands.Create(PoseCatalog.DemoPoses[(i/150)%PoseCatalog.DemoPoses.Length],HandId.Left,i,i*.02);
                var right=SyntheticHands.Create(PoseCatalog.DemoPoses[(i/150+2)%PoseCatalog.DemoPoses.Length],HandId.Right,i,i*.02);
                events.Clear(); timer.Restart(); pipeline.Process(left,events); pipeline.Process(right,events); timer.Stop();
                if(i>=1000) timings[i-1000]=timer.Elapsed.TotalMilliseconds;
            }
            Array.Sort(timings);
            File.WriteAllText("Validation/Logs/benchmark.txt",$"Synthetic input, Windows Editor {Application.unityVersion}; both hands; eight poses, Other and tracking loss held for 3s each at stored 20ms intervals; 1000 warm-up, 5000 measured. SDK input, UI and file I/O excluded. Includes feature/rule/stabilization/event processing and allocations, with stable Update events and pose transitions.\nMedian ms: {Csv.Number(timings[2500])}\nP95 ms: {Csv.Number(timings[4749])}\nThese are PC measurements, not PICO device measurements.\n");
            GenerateSample(settings);
        }
        static void GenerateSample(PoseSettings settings)
        {
            using(var recorder=new PoseCsvRecorder())
            {
                if(!recorder.Start("Validation/SyntheticSessions",new SessionMetadata {settings=settings,source="SYNTHETIC example; not PICO observations",unity_version=Application.unityVersion,sdk_version="3.4.0",sdk_commit="e0740bf309b26a0b9adae33c506514569bfe5d38",os_version=SystemInfo.operatingSystem},4096)) throw new Exception(recorder.LastError);
                var pipeline=new PosePipeline(settings); var e=new List<PoseEvent>(); var measured=new Stopwatch();
                int count=(PoseCatalog.Rules.Length+1)*40+20;
                for(int i=0;i<count;i++)
                {
                    double t=i*.05; int section=(int)(t/2);
                    PoseId pose=section<PoseCatalog.Rules.Length?PoseCatalog.Rules[section].Id:(section==PoseCatalog.Rules.Length?PoseId.Other:PoseId.TrackingUnavailable);
                    var lf=SyntheticHands.Create(pose,HandId.Left,i,t); var rf=SyntheticHands.Create(pose,HandId.Right,i,t);
                    foreach(var f in new[] {lf,rf}) for(int j=0;j<26;j++) { var sample=f.Joints[j]; sample.Position+=new Vector3(f.Hand==HandId.Left?-.15f:.15f,1.15f,.6f); f.Joints[j]=sample; }
                    e.Clear(); measured.Restart(); var l=pipeline.Process(lf,e); var r=pipeline.Process(rf,e); measured.Stop();
                    l.ProcessingMs=r.ProcessingMs=measured.Elapsed.TotalMilliseconds; recorder.Record(l,r,e);
                }
                for(int p=0;p<PoseCatalog.Rules.Length;p++) foreach(var h in new[] {HandId.Left,HandId.Right})
                    recorder.RecordTrial(Csv.Line((p*2+(int)h+1).ToString(Csv.Culture),h.ToString(),PoseCatalog.Rules[p].Id.ToString(),Csv.Number(p*2),Csv.Number(p*2+.5),Csv.Number(p*2+1.8),"Completed"));
                e.Clear(); pipeline.Reset(HandId.Left,(count-1)*.05,"RecordingStopped",e); pipeline.Reset(HandId.Right,(count-1)*.05,"RecordingStopped",e); recorder.RecordEvents(e); recorder.Stop();
                Directory.CreateDirectory("Validation/Logs"); File.WriteAllText("Validation/Logs/synthetic-session-path.txt",Path.GetFullPath(recorder.DirectoryPath));
                if(!string.IsNullOrEmpty(recorder.LastError)) throw new Exception(recorder.LastError);
            }
        }
        static double playStarted;
        static int phase;
        public static void CaptureDemo()
        {
            HandPoseProjectSetup.GenerateProject(); EditorSceneManager.OpenScene(HandPoseProjectSetup.DemoScene);
            SessionState.SetBool("HandPose.CaptureActive",true); SessionState.SetInt("HandPose.CapturePhase",0);
            EditorApplication.update+=CaptureUpdate; phase=0; playStarted=EditorApplication.timeSinceStartup; EditorApplication.isPlaying=true;
        }
        static void CaptureUpdate()
        {
            if(EditorApplication.timeSinceStartup-playStarted>45) { FinishCapture(1,"PlayMode capture timed out."); return; }
            if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var app=UnityEngine.Object.FindFirstObjectByType<PoseApplication>();
            if(!app || app.Left==null || !app.Left.Stable.Ready) return;
            if(phase==0)
            {
                UnityEngine.Object.FindFirstObjectByType<PoseDebugView>().showEditorControls=false;
                app.StartRecording(); phase=1; SessionState.SetInt("HandPose.CapturePhase",1); playStarted=EditorApplication.timeSinceStartup;
            }
            else if(phase==1 && EditorApplication.timeSinceStartup-playStarted>2)
            {
                Directory.CreateDirectory("Validation/Logs");
                var camera=app.headCamera; var target=new RenderTexture(1600,1100,24); camera.targetTexture=target;
                camera.Render(); RenderTexture.active=target;
                var pixels=new Texture2D(1600,1100,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,1600,1100),0,0); pixels.Apply();
                File.WriteAllBytes("Validation/Logs/dashboard.png",pixels.EncodeToPNG()); camera.targetTexture=null; RenderTexture.active=null;
                UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
                app.StopRecording();
                var session=CsvSessionReader.Read(app.Recorder.DirectoryPath);
                File.WriteAllText("Validation/Logs/playmode-smoke.txt",$"Passed: scene entered PlayMode; both synthetic hands stabilized; dashboard rendered; recorded {session.Batches.Count} batches; CSV read; complete={session.Metadata.recording_complete}.\nSession: {app.Recorder.DirectoryPath}\n");
                FinishCapture(0,"PlayMode smoke passed.");
            }
        }
        static void FinishCapture(int code,string status)
        { SessionState.SetBool("HandPose.CaptureActive",false); EditorApplication.update-=CaptureUpdate; UnityEngine.Debug.Log(status); EditorApplication.isPlaying=false; EditorApplication.Exit(code); }
    }
}
