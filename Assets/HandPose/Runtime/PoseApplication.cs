using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace HandPose
{
    public enum EditorInputSource { Synthetic=0, Pico=1, Replay=2, Auto=3 }
    [Serializable]
    public sealed class PoseTransitionMatchEvent : UnityEvent<HandId> { }

    public sealed class PoseApplication : MonoBehaviour
    {
        public PoseConfiguration configuration;
        public EditorInputSource editorInput=EditorInputSource.Auto;
        public PicoHandJointProvider pico;
        public SyntheticHandJointProvider synthetic;
        public ReplayHandJointProvider replay;
        public Camera headCamera;
        public Transform trackingOrigin;
        [Min(1), Tooltip("Consecutive input frames required to confirm a pose transition (both hands).")]
        public int transitionConfirmFrames = 3;
        [Min(0), Tooltip("Reset each hand's sequence after this many seconds without a confirmed change. 0 disables the timeout.")]
        public float transitionResetSeconds = 5f;
        [Tooltip("Ordered confirmed poses to recognize independently on each hand. Use at least two entries; adjacent entries must differ.")]
        public PoseId[] transitionPattern = { PoseId.Open, PoseId.Fist, PoseId.Open };
        public PoseTransitionMatchEvent onTransitionMatched = new PoseTransitionMatchEvent();
        public event Action<HandId> TransitionMatched;
        public PoseObservation Left { get; private set; }
        public PoseObservation Right { get; private set; }
        public PoseTransitionSequence LeftTransitions { get; } = new PoseTransitionSequence();
        public PoseTransitionSequence RightTransitions { get; } = new PoseTransitionSequence();
        public PoseCsvRecorder Recorder { get; private set; } = new PoseCsvRecorder();
        public PoseTrialRunner Trials { get; private set; } = new PoseTrialRunner();
        public string Status { get; private set; } = "Initializing";
        public double DataTime { get; private set; }
        public double RecordingElapsed => Recorder.IsRecording ? Math.Max(0,DataTime-recordingStarted) : 0;
        public string Source => provider?.SourceDescription ?? "No provider";
        public Transform InputOrigin => provider!=null && !ReferenceEquals(provider,synthetic) && !ReferenceEquals(provider,replay) && headCamera && headCamera.transform.parent ? headCamera.transform.parent : trackingOrigin;
#if UNITY_EDITOR
        // Editor transport is registered by HandPose.Editor, keeping it out of Android builds.
        public static Func<IHandJointProvider> EditorLivePreviewProviderFactory;
        public static IHandJointProvider SelectEditorProvider(EditorInputSource source,IHandJointProvider synthetic,IHandJointProvider pico)
        {
            if(source==EditorInputSource.Synthetic || source==EditorInputSource.Replay) return synthetic;
            var live=EditorLivePreviewProviderFactory?.Invoke();
            return live ?? (source==EditorInputSource.Auto ? synthetic : pico);
        }
#endif
        public PoseSettings ActiveSettings => settings?.Copy();
        public event Action<PoseEvent> PoseEventRaised;
        IHandJointProvider provider;
        PosePipeline pipeline;
        PoseSettings settings;
        readonly List<PoseEvent> events=new List<PoseEvent>(8);
        readonly Stopwatch timer=new Stopwatch();
        double recordingStarted;
        bool initialized;
        bool recordingHasFrame;

        void Start()
        {
            if(configuration==null) configuration=Resources.Load<PoseConfiguration>("DefaultPoseConfiguration");
            if(configuration==null) configuration=ScriptableObject.CreateInstance<PoseConfiguration>();
            pico=pico ? pico : gameObject.AddComponent<PicoHandJointProvider>();
            synthetic=synthetic ? synthetic : gameObject.AddComponent<SyntheticHandJointProvider>();
            replay=replay ? replay : gameObject.AddComponent<ReplayHandJointProvider>();
            replay.Rewound+=OnReplayRewound;
            if(!headCamera || !headCamera.isActiveAndEnabled) headCamera=Camera.main;
            trackingOrigin=trackingOrigin ? trackingOrigin : (headCamera ? headCamera.transform.parent : transform);
            // Keep existing scenes working when the preview component has not been serialized yet.
            if(!GetComponent<HandPoseHandView>()) gameObject.AddComponent<HandPoseHandView>().app=this;
            try
            {
                settings=configuration.settings.CopyForLive(); CreatePipeline(); initialized=true;
                #if UNITY_EDITOR
                provider=SelectEditorProvider(editorInput,synthetic,pico);
#else
                provider=pico;
#endif
                UnityEngine.Debug.Log("[HandPose] Input source: "+Source);
                Status="Ready";
                if(Application.isEditor && editorInput==EditorInputSource.Replay) LoadReplay(replay.sessionDirectory);
                if(configuration.autoRecord) StartRecording();
            }
            catch(Exception ex) { Status="Configuration error: "+ex.Message; }
        }
        void CreatePipeline()
        { pipeline=new PosePipeline(settings); pipeline.PoseEventRaised+=e=>PoseEventRaised?.Invoke(e); Left=null; Right=null; LeftTransitions.Clear(); RightTransitions.Clear(); }
        void Update()
        {
            LeftTransitions.RequiredFrames=RightTransitions.RequiredFrames=Math.Max(1,transitionConfirmFrames);
            LeftTransitions.InactivitySeconds=RightTransitions.InactivitySeconds=Math.Max(0,transitionResetSeconds);
            if(!initialized || provider==null || pipeline==null) return;
            double now=Time.realtimeSinceStartupAsDouble;
            // Drain all due replay samples without skipping. Live input has one batch per rendered Update.
            int limit=ReferenceEquals(provider,replay)?256:1;
            for(int i=0;i<limit && provider.TryGetBatch(now,out var batch);i++)
            {
                DataTime=batch.Left.Timestamp; events.Clear(); timer.Restart();
                if(Recorder.IsRecording && !recordingHasFrame) { recordingStarted=DataTime; recordingHasFrame=true; }
                Left=pipeline.Process(batch.Left,events); Right=pipeline.Process(batch.Right,events);
                // Replay uses recorded time; live input uses a monotonic clock, including input outages.
                double transitionTime=ReferenceEquals(provider,replay)?DataTime:now;
                bool leftChanged=LeftTransitions.Update(Left.Rules.RawPose,transitionTime);
                bool rightChanged=RightTransitions.Update(Right.Rules.RawPose,transitionTime);
                timer.Stop(); Left.ProcessingMs=Right.ProcessingMs=timer.Elapsed.TotalMilliseconds;
                Recorder.Record(Left,Right,events);
                bool leftMatched=leftChanged && LeftTransitions.MatchesSuffix(transitionPattern);
                bool rightMatched=rightChanged && RightTransitions.MatchesSuffix(transitionPattern);
                if(leftMatched) RaiseTransitionMatch(HandId.Left);
                if(rightMatched) RaiseTransitionMatch(HandId.Right);
                foreach(var reset in batch.ResetsAfter)
                {
                    events.Clear(); pipeline.Reset(reset.Hand,reset.Timestamp,reset.Reason,events); Recorder.RecordEvents(events);
                    if(reset.Hand==HandId.Left) Left=null; else Right=null;
                    if(reset.Hand==HandId.Left) LeftTransitions.Interrupt(); else RightTransitions.Interrupt();
                }
                if(Trials.Tick(DataTime,Recorder)) StopRecording();
            }
            if(!ReferenceEquals(provider,replay)) { LeftTransitions.Expire(now); RightTransitions.Expire(now); }
            if(!string.IsNullOrEmpty(Recorder.LastError)) { Trials.Abort(DataTime,Recorder); Status="Recording error: "+Recorder.LastError; Recorder.Stop(); }
        }
        void RaiseTransitionMatch(HandId hand)
        {
            TransitionMatched?.Invoke(hand);
            onTransitionMatched?.Invoke(hand);
        }
        SessionMetadata Metadata()
        {
            string os=SystemInfo.operatingSystem;
#if UNITY_ANDROID && !UNITY_EDITOR
            try { using(var build=new AndroidJavaClass("android.os.Build")) os+="; build DISPLAY="+build.GetStatic<string>("DISPLAY"); } catch(Exception ex) { os+="; DISPLAY unavailable: "+ex.GetType().Name; }
#endif
            return new SessionMetadata { settings=settings,unity_version=Application.unityVersion,sdk_version="3.4.0",sdk_commit="e0740bf309b26a0b9adae33c506514569bfe5d38",os_version=os,source=Source,participant_id=configuration.participantId };
        }
        public void StartRecording()
        {
            if(!initialized || Recorder.IsRecording) return;
            ResetAll("RecordingStarted"); CreatePipeline();
            recordingStarted=DataTime;
            recordingHasFrame=false;
            if(Recorder.Start(Path.Combine(Application.persistentDataPath,"HandPoseSessions"),Metadata(),configuration.queueCapacity)) Status="Recording";
            else Status="Recording error: "+Recorder.LastError;
        }
        public void StopRecording()
        {
            Trials.Abort(DataTime,Recorder);
            ResetAll("RecordingStopped"); Recorder.Stop();
            Status=string.IsNullOrEmpty(Recorder.LastError)?"Recording stopped":"Recording error: "+Recorder.LastError;
        }
        public void BeginEvaluation()
        {
            if(ReferenceEquals(provider,replay)) { Status="Evaluation requires live or synthetic input, not replay."; return; }
            if(Recorder.IsRecording) StopRecording();
            StartRecording(); if(Recorder.IsRecording) Trials.Begin(DataTime,settings.disablePinchRecognition);
        }
        public void ApplySettings()
        {
            try
            {
                var updated=configuration.settings.CopyForLive();
                if(Recorder.IsRecording) StopRecording();
                ResetAll("SettingsChanged"); settings=updated; CreatePipeline(); Status="Settings applied: "+settings.version;
            }
            catch(Exception ex) { Status="Settings rejected: "+ex.Message; }
        }
        public void LoadReplay(string path)
        {
            if(!initialized) return;
            if(Recorder.IsRecording) StopRecording();
            if(!replay.Load(path)) { Status="Replay error: "+replay.LastError; return; }
            settings=replay.Session.Metadata.settings.Copy(); CreatePipeline(); provider=replay; ApplyInitialResets();
            Status="Replay loaded; recorded settings applied: "+settings.version;
        }
        void ApplyInitialResets()
        {
            foreach(var reset in replay.Session.InitialResets)
            { events.Clear(); pipeline.Reset(reset.Hand,reset.Timestamp,reset.Reason,events); }
        }
        void OnReplayRewound()
        {
            if(!ReferenceEquals(provider,replay)) return;
            Trials.Abort(DataTime,Recorder); ResetAll("ReplayRewind"); Recorder.Stop(); CreatePipeline(); ApplyInitialResets();
            Status="Replay rewound; active recording closed before timestamps repeat.";
        }
        public void ResetAll(string reason)
        {
            if(pipeline==null) return;
            if(!ReferenceEquals(provider,replay)) DataTime=Time.realtimeSinceStartupAsDouble;
            events.Clear(); pipeline.Reset(HandId.Left,DataTime,reason,events); pipeline.Reset(HandId.Right,DataTime,reason,events);
            Recorder.RecordEvents(events); Left=null; Right=null;
            LeftTransitions.Clear(); RightTransitions.Clear();
        }
        void OnApplicationPause(bool pause)
        { if(pause) { Trials.Abort(DataTime,Recorder); ResetAll("ApplicationPaused"); } }
        void OnDisable() { if(initialized) { Trials.Abort(DataTime,Recorder); ResetAll("ComponentDisabled"); Recorder.Stop(); } }
        void OnDestroy() { if(replay) replay.Rewound-=OnReplayRewound; Recorder.Dispose(); }
    }
}
