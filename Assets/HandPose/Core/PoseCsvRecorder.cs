using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace HandPose
{
    [Serializable]
    public sealed class SessionMetadata
    {
        public int schema_version=2;
        public string session_id, created_utc, participant_id, unity_version, sdk_version, sdk_commit, os_version, source;
        public string coordinate_system="Unity left-handed tracking-origin local; x right, y up, z forward; meters; quaternion x,y,z,w";
        public string sdk_conversion="PICO native path: Vector3f.ToVector3=(x,y,-z), Quatf.ToQuat=(x,y,-z,-w), applied once. Synthetic/replay positions already use Unity coordinates.";
        public string sampling="Provider batch timestamps; inspect source. PICO live queries once per Update and may repeat sensor samples; Update rate is not sensor frequency. HandJointLocations has no SDK timestamp. Synthetic/replay batches are not sensor acquisitions.";
        public string validity="Provider validity plus numeric/geometry guards. PICO uses isActive and PositionValid/OrientationValid; synthetic fixtures use programmed flags. Original SDK flags are retained when available. Flags are not calibrated confidence.";
        public string settings_version;
        public PoseSettings settings;
        public bool recording_complete;
        public long dropped_batches, dropped_events;
        public string write_error="";
    }

    public sealed class PoseCsvRecorder : IDisposable
    {
        sealed class Packet
        {
            public PoseObservation[] Observations;
            public PoseEvent[] Events;
            public string Trial;
        }
        BlockingCollection<Packet> queue;
        Thread worker;
        SessionMetadata metadata;
        string sessionJson;
        long droppedBatches, droppedEvents;
        volatile string error="";
        public bool IsRecording => worker!=null && string.IsNullOrEmpty(error);
        public string DirectoryPath { get; private set; } = "";
        public string LastError => error;
        public long DroppedBatches => Interlocked.Read(ref droppedBatches);
        public long DroppedEvents => Interlocked.Read(ref droppedEvents);

        public bool Start(string root,SessionMetadata info,int capacity)
        {
            if(worker!=null) throw new InvalidOperationException("Recorder already started.");
            error=""; droppedBatches=0; droppedEvents=0;
            metadata=null; sessionJson=null; DirectoryPath="";
            var opened=new List<StreamWriter>();
            try
            {
                info.settings=info.settings.Copy(); info.settings_version=info.settings.version;
                info.session_id=DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ",Csv.Culture)+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
                info.created_utc=DateTime.UtcNow.ToString("O",Csv.Culture); info.recording_complete=false;
                info.dropped_batches=0; info.dropped_events=0; info.write_error="";
                metadata=info; DirectoryPath=Path.Combine(root,info.session_id); Directory.CreateDirectory(DirectoryPath);
                sessionJson=Path.Combine(DirectoryPath,"session.json"); SaveMetadata();
                // Open synchronously so permission/path errors are visible immediately; all frame writes run on the worker.
                var joints=Writer("joints.csv"); opened.Add(joints);
                var poses=Writer("poses.csv"); opened.Add(poses);
                var events=Writer("events.csv"); opened.Add(events);
                var trials=Writer("trials.csv"); opened.Add(trials);
                queue=new BlockingCollection<Packet>(Math.Max(8,capacity));
                worker=new Thread(()=>WriteLoop(joints,poses,events,trials)) { IsBackground=true,Name="Pose CSV writer" };
                worker.Start(); return true;
            }
            catch(Exception ex)
            {
                foreach(var writer in opened) try { writer.Dispose(); } catch { }
                error=ex.GetType().Name+": "+ex.Message;
                if(metadata!=null) metadata.write_error=error;
                TrySaveMetadata(); return false;
            }
        }
        StreamWriter Writer(string file) => new StreamWriter(Path.Combine(DirectoryPath,file),false,new UTF8Encoding(false),65536);
        void SaveMetadata() => File.WriteAllText(sessionJson,JsonUtility.ToJson(metadata,true),new UTF8Encoding(false));
        void TrySaveMetadata() { try { if(metadata!=null && sessionJson!=null) SaveMetadata(); } catch(Exception ex) { error=ex.GetType().Name+": "+ex.Message; } }
        public void Record(PoseObservation left,PoseObservation right,List<PoseEvent> events)
        {
            if(!IsRecording) return;
            if(!queue.TryAdd(new Packet { Observations=new[] {left,right}, Events=events.ToArray() }))
            { Interlocked.Increment(ref droppedBatches); Interlocked.Add(ref droppedEvents,events.Count); }
        }
        public void RecordEvents(List<PoseEvent> events)
        {
            if(!IsRecording || events.Count==0) return;
            if(!queue.TryAdd(new Packet { Events=events.ToArray() })) Interlocked.Add(ref droppedEvents,events.Count);
        }
        public void RecordTrial(string trialRow)
        {
            if(!IsRecording) return;
            if(!queue.TryAdd(new Packet { Trial=trialRow })) { Interlocked.Increment(ref droppedEvents); error="Trial annotation queue overflow; evaluation recording incomplete."; }
        }
        void WriteLoop(StreamWriter joints,StreamWriter poses,StreamWriter events,StreamWriter trials)
        {
            try
            {
                joints.WriteLine("frame_id,timestamp_s,hand_id,joint_id,hand_valid,position_valid,orientation_valid,x_m,y_m,z_m,qx,qy,qz,qw,sdk_active,sdk_flags,sdk_timestamp_s,provider_reason");
                var header=new List<string> {"frame_id","timestamp_s","hand_id","input_valid","raw_pose","stable_pose","stable_ready","pending_pose"};
                foreach(var rule in PoseCatalog.RulesForVersion(metadata.settings.rulesVersion)) header.Add("candidate_"+rule.CsvName);
                header.AddRange(new[] {"thumb_state","thumb_mcp_deg","thumb_ip_deg"});
                if(metadata.settings.rulesVersion>=6) header.Add("thumb_base_angle_deg");
                foreach(var name in new[] {"index","middle","ring","little"}) header.AddRange(new[] {name+"_state",name+"_pip_deg",name+"_dip_deg"});
                header.AddRange(new[] {"hand_scale_m","pinch_distance_m","pinch_ratio","pinch_valid","invalid_reason","settings_version","processing_both_hands_ms"});
                poses.WriteLine(Csv.Line(header.ToArray()));
                events.WriteLine("timestamp_s,hand_id,event_type,pose,duration_s,reason,after_frame_id");
                trials.WriteLine("trial_id,hand_id,target_pose,cue_s,hold_start_s,hold_end_s,status");
                var flushClock=System.Diagnostics.Stopwatch.StartNew();
                foreach(var packet in queue.GetConsumingEnumerable())
                {
                    if(packet.Observations!=null)
                        foreach(var o in packet.Observations) { WriteJoints(joints,o.Frame); WritePose(poses,o,metadata.settings_version,metadata.settings.rulesVersion); }
                    if(packet.Events!=null)
                        foreach(var e in packet.Events) events.WriteLine(Csv.Line(Csv.Number(e.Timestamp),e.Hand.ToString(),e.Type,e.Pose.ToString(),Csv.Number(e.Duration),e.Reason,e.AfterFrameId.ToString(Csv.Culture)));
                    if(packet.Trial!=null) trials.WriteLine(packet.Trial);
                    if(flushClock.Elapsed.TotalSeconds>=1)
                    { joints.Flush(); poses.Flush(); events.Flush(); trials.Flush(); flushClock.Restart(); }
                }
            }
            catch(Exception ex) { error=ex.GetType().Name+": "+ex.Message; }
            finally
            {
                foreach(var w in new[] {joints,poses,events,trials})
                    try { w.Dispose(); } catch(Exception ex) { error=ex.GetType().Name+": "+ex.Message; }
            }
        }
        static void WriteJoints(StreamWriter writer,HandFrame f)
        {
            for(int i=0;i<26;i++)
            {
                var j=f.Joints[i]; bool p=f.HandValid && j.HasPosition, q=f.HandValid && j.HasOrientation;
                writer.WriteLine(Csv.Line(f.FrameId.ToString(Csv.Culture),Csv.Number(f.Timestamp),f.Hand.ToString(),i.ToString(Csv.Culture),
                    Csv.Flag(f.HandValid),Csv.Flag(p),Csv.Flag(q),p?Csv.Number(j.Position.x):"",p?Csv.Number(j.Position.y):"",p?Csv.Number(j.Position.z):"",
                    q?Csv.Number(j.Orientation.x):"",q?Csv.Number(j.Orientation.y):"",q?Csv.Number(j.Orientation.z):"",q?Csv.Number(j.Orientation.w):"",
                    f.SdkActive.ToString(Csv.Culture),j.SdkFlags.ToString(Csv.Culture),f.SdkTimestamp.HasValue?Csv.Number(f.SdkTimestamp.Value):"",f.InvalidReason));
            }
        }
        static void WritePose(StreamWriter writer,PoseObservation o,string version,int rulesVersion)
        {
            var f=o.Frame; var r=o.Rules; var ft=r.Features;
            var cells=new List<string> {f.FrameId.ToString(Csv.Culture),Csv.Number(f.Timestamp),f.Hand.ToString(),Csv.Flag(r.InputValid),r.RawPose.ToString(),o.Stable.Pose.ToString(),Csv.Flag(o.Stable.Ready),o.Stable.HasPending?o.Stable.Pending.ToString():""};
            foreach(var rule in PoseCatalog.RulesForVersion(rulesVersion)) cells.Add(r.Candidates[(int)rule.Id].ToString());
            cells.AddRange(new[] {ft.Thumb.State.ToString(),ft.Thumb.McpValid?Csv.Number(ft.Thumb.Mcp):"",ft.Thumb.IpValid?Csv.Number(ft.Thumb.Ip):""});
            if(rulesVersion>=6) cells.Add(ft.Thumb.BaseAngleValid?Csv.Number(ft.Thumb.BaseAngle):"");
            foreach(var finger in ft.Fingers) cells.AddRange(new[] {finger.State.ToString(),finger.PipValid?Csv.Number(finger.Pip):"",finger.DipValid?Csv.Number(finger.Dip):""});
            cells.AddRange(new[] {ft.ScaleMeasured?Csv.Number(ft.ScaleMeters):"",ft.DistanceMeasured?Csv.Number(ft.PinchMeters):"",ft.PinchValid?Csv.Number(ft.PinchRatio):"",Csv.Flag(ft.PinchValid),ft.InvalidReason,version,Csv.Number(o.ProcessingMs)});
            writer.WriteLine(Csv.Line(cells.ToArray()));
        }
        public void Stop()
        {
            if(worker==null) return;
            queue.CompleteAdding(); worker.Join(); worker=null; queue.Dispose(); queue=null;
            metadata.dropped_batches=DroppedBatches; metadata.dropped_events=DroppedEvents; metadata.write_error=error;
            metadata.recording_complete=string.IsNullOrEmpty(error) && DroppedBatches==0 && DroppedEvents==0;
            TrySaveMetadata();
        }
        public void Dispose() => Stop();
    }
}
