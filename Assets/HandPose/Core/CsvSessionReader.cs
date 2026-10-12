using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HandPose
{
    public sealed class RecordedSession
    {
        public SessionMetadata Metadata;
        public readonly List<HandInputBatch> Batches=new List<HandInputBatch>();
        public readonly List<ResetCommand> InitialResets=new List<ResetCommand>();
    }
    public static class CsvSessionReader
    {
        public static RecordedSession Read(string directory)
        {
            var s=new RecordedSession { Metadata=JsonUtility.FromJson<SessionMetadata>(File.ReadAllText(Path.Combine(directory,"session.json"))) };
            if(s.Metadata==null || (s.Metadata.schema_version!=1 && s.Metadata.schema_version!=2) || s.Metadata.settings==null) throw new FormatException("Unsupported session metadata.");
            if(s.Metadata.schema_version==1) s.Metadata.settings.rulesVersion=1;
            if(s.Metadata.settings.thumb==null) s.Metadata.settings.thumb=PoseSettings.DefaultThumbThreshold();
            s.Metadata.settings.Validate();
            var index=new Dictionary<long,HandInputBatch>();
            var seen=new Dictionary<long,ulong>();
            var counts=new Dictionary<HandFrame,int>();
            using(var t=new CsvTable(Path.Combine(directory,"joints.csv")))
                while(t.Next())
                {
                    long id=long.Parse(t.Get("frame_id"),Csv.Culture); double time=t.Double("timestamp_s");
                    if(time<0) throw new FormatException("Negative frame timestamp.");
                    var hand=(HandId)Enum.Parse(typeof(HandId),t.Get("hand_id"));
                    if(hand!=HandId.Left && hand!=HandId.Right) throw new FormatException("Invalid hand.");
                    if(!index.TryGetValue(id,out var b))
                    {
                        if(s.Batches.Count>0 && (id<=s.Batches[s.Batches.Count-1].Left.FrameId || time<s.Batches[s.Batches.Count-1].Left.Timestamp)) throw new FormatException("Frames are not monotonically ordered.");
                        b=new HandInputBatch { Left=new HandFrame {FrameId=id,Timestamp=time,Hand=HandId.Left},Right=new HandFrame {FrameId=id,Timestamp=time,Hand=HandId.Right} };
                        index.Add(id,b); s.Batches.Add(b);
                    }
                    var f=hand==HandId.Left?b.Left:b.Right;
                    int joint=int.Parse(t.Get("joint_id"),Csv.Culture);
                    if(joint<0 || joint>=26) throw new FormatException("Invalid joint row.");
                    ulong bit=1UL<<((int)hand*26+joint); seen.TryGetValue(id,out ulong mask);
                    if((mask&bit)!=0) throw new FormatException("Duplicate joint row.");
                    seen[id]=mask|bit;
                    if(f.Timestamp!=time) throw new FormatException("Inconsistent batch timestamp.");
                    bool hv=t.Bool("hand_valid"); string why=t.Get("provider_reason"); uint active=uint.Parse(t.Get("sdk_active"),Csv.Culture);
                    string sdk=t.Get("sdk_timestamp_s"); double? sdkTime=sdk.Length==0?(double?)null:t.Double("sdk_timestamp_s");
                    if(counts.TryGetValue(f,out int count) && (f.Timestamp!=time || f.HandValid!=hv || f.InvalidReason!=why || f.SdkActive!=active || f.SdkTimestamp!=sdkTime)) throw new FormatException("Inconsistent hand-frame metadata.");
                    f.HandValid=hv; f.InvalidReason=why; f.SdkActive=active; f.SdkTimestamp=sdkTime; counts[f]=count+1;
                    var j=new JointSample { PositionValid=t.Bool("position_valid"),OrientationValid=t.Bool("orientation_valid"),SdkFlags=ulong.Parse(t.Get("sdk_flags"),Csv.Culture) };
                    if(j.PositionValid)
                    {
                        j.Position=new Vector3((float)t.Double("x_m"),(float)t.Double("y_m"),(float)t.Double("z_m"));
                        if(!j.HasPosition || !hv) throw new FormatException("Invalid position marked valid.");
                    }
                    if(j.OrientationValid)
                    {
                        j.Orientation=new Quaternion((float)t.Double("qx"),(float)t.Double("qy"),(float)t.Double("qz"),(float)t.Double("qw"));
                        if(!j.HasOrientation || !hv) throw new FormatException("Invalid orientation marked valid.");
                    }
                    f.Joints[joint]=j;
                }
            foreach(var b in s.Batches)
                foreach(var f in new[] {b.Left,b.Right})
                    if(!counts.TryGetValue(f,out int c) || c!=26) throw new FormatException("Every batch requires 26 rows per hand, including unavailable hands.");
            using(var t=new CsvTable(Path.Combine(directory,"events.csv")))
                while(t.Next())
                    if(t.Get("event_type")=="Reset")
                    {
                        var reset=new ResetCommand { AfterFrameId=long.Parse(t.Get("after_frame_id"),Csv.Culture),Timestamp=t.Double("timestamp_s"),Hand=(HandId)Enum.Parse(typeof(HandId),t.Get("hand_id")),Reason=t.Get("reason") };
                        if(reset.Hand!=HandId.Left && reset.Hand!=HandId.Right) throw new FormatException("Invalid reset hand.");
                        if(index.TryGetValue(reset.AfterFrameId,out var b)) b.ResetsAfter.Add(reset);
                        else if(reset.AfterFrameId==-1) s.InitialResets.Add(reset);
                        else throw new FormatException("Reset references missing frame (possibly a dropped batch).");
                    }
            return s;
        }
    }
}
