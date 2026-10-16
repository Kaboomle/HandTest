using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace HandPose
{
    public sealed class PoseDebugView : MonoBehaviour
    {
        public PoseApplication app;
        public bool showEditorControls=true;
        public Color transitionMatchColor=new Color(.08f,.35f,.18f);
        [Min(.1f)] public float transitionMatchSeconds=1.5f;
        Image transitionBackground;
        double transitionHighlightUntil;
        Canvas canvas;
        Canvas transitionCanvas;
        Text leftTransitionsText,rightTransitionsText,transitionRuleText;
        Text leftText,rightText,statusText,instructionText,definitionText;
        Font font;
        readonly List<Button> gazeButtons=new List<Button>();
        Button hovered,latched;
        float dwell;
        double nextTextUpdate;
        bool centeredOnTrackedHead;
        string replayPath="";
        static readonly Color Ink=new Color(.82f,.9f,1f), Dim=new Color(.43f,.55f,.68f), Accent=new Color(.25f,.85f,.75f);
        void Start()
        {
            app=app?app:GetComponent<PoseApplication>(); font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root=new GameObject("Pose dashboard",typeof(RectTransform),typeof(Canvas)); canvas=root.GetComponent<Canvas>();
            canvas.renderMode=RenderMode.WorldSpace; canvas.worldCamera=app.headCamera;
            var rect=(RectTransform)root.transform; rect.sizeDelta=new Vector2(1480,1050); rect.localScale=Vector3.one*.0011f;
            Panel("Background",rect,new Vector2(0,0),new Vector2(1480,1050),new Color(.025f,.045f,.08f));
            Label("Title",rect,new Vector2(0,465),new Vector2(1380,60),"HAND POSE / PICO 4 ULTRA",36,Accent);
            definitionText=Label("Definitions",rect,new Vector2(0,411),new Vector2(1380,45),"8 static poses | Thumb: CMC->MCP vs Wrist->Middle MCP",23,Dim);
            Panel("Left card",rect,new Vector2(-357,103),new Vector2(687,545),new Color(.055f,.09f,.14f));
            Panel("Right card",rect,new Vector2(357,103),new Vector2(687,545),new Color(.055f,.09f,.14f));
            leftText=Label("Left",rect,new Vector2(-357,103),new Vector2(650,510),"LEFT",23,Ink,TextAnchor.UpperLeft);
            rightText=Label("Right",rect,new Vector2(357,103),new Vector2(650,510),"RIGHT",23,Ink,TextAnchor.UpperLeft);
            statusText=Label("Status",rect,new Vector2(0,-245),new Vector2(1380,150),"",21,Ink,TextAnchor.UpperLeft);
            instructionText=Label("Trial instruction",rect,new Vector2(0,-365),new Vector2(1380,82),"",25,Accent);
            AddButton(rect,new Vector2(-490,-463),"START / STOP REC",()=> { if(app.Recorder.IsRecording) app.StopRecording(); else app.StartRecording(); });
            AddButton(rect,new Vector2(0,-463),$"BEGIN {(PoseCatalog.Rules.Length-1)*20} TRIALS",app.BeginEvaluation);
            AddButton(rect,new Vector2(490,-463),"RECENTER",Recenter);
            CreateTransitionMonitor();
            Recenter();
        }
        void CreateTransitionMonitor()
        {
            var root=new GameObject("Pose transition monitor",typeof(RectTransform),typeof(Canvas));
            transitionCanvas=root.GetComponent<Canvas>(); transitionCanvas.renderMode=RenderMode.WorldSpace;
            var rect=(RectTransform)root.transform; rect.sizeDelta=new Vector2(1480,1050); rect.localScale=Vector3.one*.0011f;
            transitionBackground=Rect("Background",rect,Vector2.zero,rect.sizeDelta).gameObject.AddComponent<Image>();
            transitionBackground.color=new Color(.025f,.045f,.08f);
            Label("Title",rect,new Vector2(0,465),new Vector2(1380,60),"POSE TRANSITION SEQUENCE",36,Accent);
            transitionRuleText=Label("Rule",rect,new Vector2(0,395),new Vector2(1380,60),"",23,Dim);
            leftTransitionsText=Label("Left sequence",rect,new Vector2(0,170),new Vector2(1360,340),"",30,Ink,TextAnchor.UpperLeft);
            rightTransitionsText=Label("Right sequence",rect,new Vector2(0,-220),new Vector2(1360,340),"",30,Ink,TextAnchor.UpperLeft);
        }
        static string DescribeTransitions(string hand,PoseTransitionSequence sequence)
        {
            var states=sequence.States;
            var text=new StringBuilder(hand+"    transitions: "+Math.Max(0,states.Count-1)+"\n\n[");
            int start=Math.Max(0,states.Count-24);
            if(start>0) text.Append("... , ");
            for(int i=start;i<states.Count;i++) { if(i>start) text.Append(", "); text.Append(states[i]); }
            return text.Append("]").ToString();
        }
        static RectTransform Rect(string name,Transform parent,Vector2 pos,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform)); var r=(RectTransform)go.transform;
            r.SetParent(parent,false); r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f); r.anchoredPosition=pos; r.sizeDelta=size; return r;
        }
        void Panel(string name,Transform parent,Vector2 pos,Vector2 size,Color color)
        { var r=Rect(name,parent,pos,size); r.gameObject.AddComponent<Image>().color=color; }
        Text Label(string name,Transform parent,Vector2 pos,Vector2 size,string value,int fontSize,Color color,TextAnchor anchor=TextAnchor.MiddleCenter)
        {
            var text=Rect(name,parent,pos,size).gameObject.AddComponent<Text>(); text.font=font; text.text=value; text.fontSize=fontSize;
            text.color=color; text.alignment=anchor; text.horizontalOverflow=HorizontalWrapMode.Wrap; text.verticalOverflow=VerticalWrapMode.Truncate; text.raycastTarget=false; return text;
        }
        void AddButton(Transform parent,Vector2 pos,string label,Action action)
        {
            var r=Rect(label,parent,pos,new Vector2(420,75)); var image=r.gameObject.AddComponent<Image>(); image.color=new Color(.1f,.2f,.27f);
            var b=r.gameObject.AddComponent<Button>(); b.targetGraphic=image; b.onClick.AddListener(()=>action()); gazeButtons.Add(b);
            Label("Label",r,Vector2.zero,new Vector2(410,70),label,24,Ink);
        }
        public void Recenter()
        {
            if(!canvas || !app.headCamera) return;
            var head=app.headCamera.transform;
            canvas.transform.position=head.position+head.forward*1.5f;
            canvas.transform.rotation=head.rotation;
            if(transitionCanvas)
            {
                var rotation=head.rotation*Quaternion.Euler(0,90,0);
                transitionCanvas.transform.SetPositionAndRotation(head.position+rotation*Vector3.forward*1.5f,rotation);
            }
        }
        void Update()
        {
            if(!app || !canvas) return;
            if(transitionBackground) transitionBackground.color=Time.realtimeSinceStartupAsDouble<transitionHighlightUntil
                ?transitionMatchColor:new Color(.025f,.045f,.08f);
            canvas.worldCamera=app.headCamera;
            if(transitionCanvas) transitionCanvas.worldCamera=app.headCamera;
            UpdateGaze();
            if(Time.realtimeSinceStartupAsDouble<nextTextUpdate) return;
            nextTextUpdate=Time.realtimeSinceStartupAsDouble+.1;
            transitionRuleText.text=$"Confirm after {Math.Max(1,app.transitionConfirmFrames)} consecutive input frames | Latest 24 states per hand";
            leftTransitionsText.text=DescribeTransitions("LEFT",app.LeftTransitions);
            rightTransitionsText.text=DescribeTransitions("RIGHT",app.RightTransitions);
            int version=app.ActiveSettings?.rulesVersion ?? PoseCatalog.CurrentRulesVersion;
            definitionText.text=version>=7?"8 static poses | Thumb Bent: base rule OR IP curl":version>=6?"8 static poses | Thumb: CMC->MCP vs Wrist->Middle MCP":
                (version>=5?"8 static poses | Thumb: MCP only | IP shown for reference":"Saved rules | Thumb: MCP and IP");
            if(app.ActiveSettings?.disablePinchRecognition==true)
                definitionText.text=app.ActiveSettings.useThumbSpread?"7 static poses | Pinch disabled | Thumb: base/IP + outward reach":"7 static poses | Pinch disabled | Thumb Bent: base rule OR IP curl";
            leftText.text=Describe("LEFT",app.Left,version); rightText.text=Describe("RIGHT",app.Right,version);
            statusText.text=$"{app.Source}\n{app.Status} | {(app.Recorder.IsRecording?"REC":"IDLE")} {app.RecordingElapsed:F1}s | dropped batches/events: {app.Recorder.DroppedBatches}/{app.Recorder.DroppedEvents}\n{app.Recorder.DirectoryPath}\n{app.Recorder.LastError}";
            instructionText.text=app.Trials.Instruction;
        }
        void LateUpdate()
        {
            if(!app || !app.headCamera || !canvas || centeredOnTrackedHead) return;
            var driver=app.headCamera.GetComponent<TrackingOriginCamera>();
            bool tracked=driver && driver.IsHeadTracked;
            if(!driver)
            {
                var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);
                tracked=head.isValid && head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool isTracked) && isTracked;
            }
            if(tracked) { Recenter(); centeredOnTrackedHead=true; }
        }
        static string Describe(string label,PoseObservation o,int version)
        {
            if(o==null) return label+"\nTrackingUnavailable\nstable_ready: false";
            var f=o.Rules.Features; var s=new StringBuilder();
            s.AppendLine(label+"    tracked: "+o.Frame.HandValid+" / input: "+o.Rules.InputValid);
            s.AppendLine("raw: "+o.Rules.RawPose);
            s.AppendLine("stable: "+o.Stable.Pose+"    ready: "+o.Stable.Ready);
            s.AppendLine("pending: "+(o.Stable.HasPending?o.Stable.Pending.ToString():"--"));
            s.AppendLine("Candidates (M=match, -=no, ?=unavailable)");
            for(int i=0;i<PoseCatalog.Rules.Length;i++)
            { var rule=PoseCatalog.Rules[i]; var candidate=o.Rules.Candidates[(int)rule.Id]; s.Append(rule.Id+":"+(candidate==RuleStatus.Match?"M":candidate==RuleStatus.Unavailable?"?":"-")+"  "); if(i%3==2) s.AppendLine(); }
            if(PoseCatalog.Rules.Length%3!=0) s.AppendLine();
            string thumbInterpretation=o.Rules.ThumbRuleState!=f.Thumb.State?" (as "+o.Rules.ThumbRuleState+")":"";
            if(version>=6) s.AppendLine("Thumb: "+f.Thumb.State+thumbInterpretation+"  Base angle "+(f.Thumb.BaseAngleValid?f.Thumb.BaseAngle.ToString("F1",Csv.Culture):"--")+" deg");
            else s.AppendLine("Thumb: "+f.Thumb.State+thumbInterpretation+"  MCP "+(f.Thumb.McpValid?f.Thumb.Mcp.ToString("F1",Csv.Culture):"--")+" / IP "+(f.Thumb.IpValid?f.Thumb.Ip.ToString("F1",Csv.Culture):"--"));
            if(version>=7) s.AppendLine("Thumb IP: "+(f.Thumb.IpValid?f.Thumb.Ip.ToString("F1",Csv.Culture):"--")+" deg");
            s.AppendLine("Thumb outward / width: "+(f.Thumb.SpreadValid?f.Thumb.SpreadRatio.ToString("F3",Csv.Culture):"--"));
            for(int i=0;i<4;i++)
            {
                var finger=f.Fingers[i];
                s.AppendLine(new[] {"Index","Middle","Ring","Little"}[i]+": "+finger.State+"  PIP "+(finger.PipValid?finger.Pip.ToString("F1",Csv.Culture):"--")+" / DIP "+(finger.DipValid?finger.Dip.ToString("F1",Csv.Culture):"--"));
            }
            s.AppendLine("Pinch m: "+(f.DistanceMeasured?f.PinchMeters.ToString("F4",Csv.Culture):"--")+" / scale m: "+(f.ScaleMeasured?f.ScaleMeters.ToString("F4",Csv.Culture):"--"));
            s.AppendLine("ratio: "+(f.PinchValid?f.PinchRatio.ToString("F3",Csv.Culture):"invalid")+"   t: "+o.Frame.Timestamp.ToString("F3",Csv.Culture)+"s");
            s.AppendLine("both hands: "+o.ProcessingMs.ToString("F3",Csv.Culture)+" ms"); s.AppendLine(f.InvalidReason);
            return s.ToString();
        }
        void UpdateGaze()
        {
            if(!app.headCamera) return;
            var ray=new Ray(app.headCamera.transform.position,app.headCamera.transform.forward); Button hit=null;
            foreach(var b in gazeButtons)
            {
                var r=(RectTransform)b.transform; var plane=new Plane(r.forward,r.position);
                if(plane.Raycast(ray,out float distance) && r.rect.Contains(r.InverseTransformPoint(ray.GetPoint(distance)))) { hit=b; break; }
            }
            if(hit!=hovered) { hovered=hit; dwell=0; if(hit!=latched) latched=null; }
            if(hovered && hovered!=latched)
            {
                dwell+=Time.unscaledDeltaTime;
                float required=app.configuration?app.configuration.gazeDwellSeconds:1.2f;
                if(dwell>=required) { latched=hovered; hovered.onClick.Invoke(); }
            }
            // Mouse support for Editor/Game view without depending on either hand's pose.
            if(Application.isEditor && Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame)
                foreach(var b in gazeButtons)
                    if(RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform,Mouse.current.position.ReadValue(),app.headCamera)) { b.onClick.Invoke(); break; }
            foreach(var b in gazeButtons) b.targetGraphic.color=b==hovered?Color.Lerp(new Color(.1f,.2f,.27f),Accent,Mathf.Clamp01(dwell/1.2f)):new Color(.1f,.2f,.27f);
        }
#if UNITY_EDITOR
        void OnGUI()
        {
            if(!app || !showEditorControls) return;
            GUILayout.BeginArea(new Rect(10,10,380,220),GUI.skin.box);
            GUILayout.Label("Editor controls / " + app.Source);
            if(GUILayout.Button(app.Recorder.IsRecording?"Stop recording":"Start recording")) { if(app.Recorder.IsRecording) app.StopRecording(); else app.StartRecording(); }
            replayPath=GUILayout.TextField(replayPath);
            if(GUILayout.Button("Load session folder")) app.LoadReplay(replayPath);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Step")) app.replay.Step();
            if(GUILayout.Button("Realtime")) { app.replay.realtime=true; app.replay.paused=false; }
            if(GUILayout.Button("Pause")) app.replay.paused=!app.replay.paused;
            if(GUILayout.Button("Rewind")) app.replay.Rewind();
            GUILayout.EndHorizontal();
            if(GUILayout.Button("Apply configuration")) app.ApplySettings();
            GUILayout.EndArea();
        }
#endif
        void OnTransitionMatched(HandId hand)
        { transitionHighlightUntil=Time.realtimeSinceStartupAsDouble+Math.Max(.1f,transitionMatchSeconds); }
        void OnEnable()
        {
            app=app?app:GetComponent<PoseApplication>();
            if(app) app.TransitionMatched+=OnTransitionMatched;
            if(canvas) canvas.gameObject.SetActive(true);
            if(transitionCanvas) transitionCanvas.gameObject.SetActive(true);
        }
        void OnDisable()
        {
            if(app) app.TransitionMatched-=OnTransitionMatched;
            transitionHighlightUntil=0;
            if(canvas) canvas.gameObject.SetActive(false);
            if(transitionCanvas) transitionCanvas.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if(canvas) Destroy(canvas.gameObject);
            if(transitionCanvas) Destroy(transitionCanvas.gameObject);
        }
    }
}
