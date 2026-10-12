using System;
using System.Collections.Generic;

namespace HandPose
{
    // Ground truth comes from a scheduled instruction, never from a classification output.
    public sealed class PoseTrialRunner
    {
        struct Target { public HandId Hand; public PoseId Pose; }
        readonly List<Target> schedule=new List<Target>();
        int index;
        double cue;
        public bool Active { get; private set; }
        public string Instruction { get; private set; } = $"Evaluation: {PoseCatalog.Rules.Length} poses x 10 trials x 2 hands";
        const double PrepareSeconds=1, HoldSeconds=2, RestSeconds=1;
        public void Begin(double time)
        {
            schedule.Clear();
            for(int h=0;h<2;h++) foreach(var rule in PoseCatalog.Rules) for(int r=0;r<10;r++) schedule.Add(new Target {Hand=(HandId)h,Pose=rule.Id});
            var random=new Random(20261010);
            for(int i=schedule.Count-1;i>0;i--) { int k=random.Next(i+1); var t=schedule[k]; schedule[k]=schedule[i]; schedule[i]=t; }
            index=0; cue=time; Active=true; SetInstruction(time);
        }
        void SetInstruction(double time)
        {
            var target=schedule[index];
            string phase=time<cue+PrepareSeconds?"PREPARE":(time<cue+PrepareSeconds+HoldSeconds?"HOLD":"REST");
            var rule=PoseCatalog.Find(target.Pose);
            string definition=rule.UsesDistance?"Thumb / index tip distance; other fingers free.":
                "Thumb MCP: "+rule.Thumb.Value+(rule.Thumb==FingerState.Bent?" (Intermediate accepted)":"")+"; follow the four-finger pattern.";
            Instruction=$"Trial {index+1}/{schedule.Count}  {target.Hand}  {target.Pose}  {phase}\n{definition} Use only the instructed hand.";
        }
        void Write(PoseCsvRecorder recorder,string status)
        {
            var t=schedule[index];
            recorder.RecordTrial(Csv.Line((index+1).ToString(Csv.Culture),t.Hand.ToString(),t.Pose.ToString(),Csv.Number(cue),Csv.Number(cue+PrepareSeconds),Csv.Number(cue+PrepareSeconds+HoldSeconds),status));
        }
        public bool Tick(double time,PoseCsvRecorder recorder)
        {
            if(!Active) return false;
            while(time>=cue+PrepareSeconds+HoldSeconds+RestSeconds)
            {
                Write(recorder,"Completed"); index++; cue+=PrepareSeconds+HoldSeconds+RestSeconds;
                if(index==schedule.Count) { Active=false; Instruction=$"Evaluation complete: {schedule.Count} instructed trials recorded."; return true; }
            }
            SetInstruction(time); return false;
        }
        public void Abort(double time,PoseCsvRecorder recorder)
        {
            if(!Active) return;
            Write(recorder,"Aborted"); Active=false; Instruction="Evaluation aborted; incomplete trial excluded from accuracy, reported separately.";
        }
    }
}
