using System;
using UnityEngine;

namespace HandPose
{
    public sealed class ReplayHandJointProvider : MonoBehaviour,IHandJointProvider
    {
        public string sessionDirectory;
        public bool realtime=true;
        [Min(0.01f)] public float speed=1;
        public bool paused;
        public RecordedSession Session { get; private set; }
        public string LastError { get; private set; } = "";
        public int Position { get; private set; }
        public bool AtEnd => Session!=null && Position>=Session.Batches.Count;
        int steps;
        double startedAt, savedStart;
        bool clockStarted;
        double lastAppTime;
        public event Action Rewound;
        public string SourceDescription => "CSV replay: "+sessionDirectory;
        public bool Load(string path)
        {
            try { var session=CsvSessionReader.Read(path); Session=session; sessionDirectory=path; LastError=""; Rewind(); return true; }
            catch(Exception ex) { LastError=ex.GetType().Name+": "+ex.Message; return false; }
        }
        public void Rewind() { Position=0; steps=0; clockStarted=false; Rewound?.Invoke(); }
        public void Step() { realtime=false; paused=false; steps++; }
        public bool TryGetBatch(double appTime,out HandInputBatch batch)
        {
            batch=null;
            if(Session==null || AtEnd) return false;
            if(!clockStarted) { startedAt=appTime; savedStart=Session.Batches[Position].Left.Timestamp; lastAppTime=appTime; clockStarted=true; }
            if(paused) { startedAt+=appTime-lastAppTime; lastAppTime=appTime; return false; }
            lastAppTime=appTime;
            if(!realtime && steps<=0) return false;
            var next=Session.Batches[Position];
            if(realtime && next.Left.Timestamp-savedStart>(appTime-startedAt)*Math.Max(0.01,speed)) return false;
            if(!realtime) steps--;
            batch=next; Position++; return true;
        }
    }
}
