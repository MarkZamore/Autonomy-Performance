using System;
using System.Collections.Generic;
using WindowsPreferences;

// Production NightLightControl with fake bool/UInt32 sessions; no native setter runs.
static class NightLightPreferenceTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    static TrayPreferenceSnapshot Mode(bool value){return new TrayPreferenceSnapshot(value,true,true,false,false);}
    static UInt32PreferenceSnapshot Amount(uint value){return new UInt32PreferenceSnapshot(value,true,true,false,false);}
    static int Main()
    {
        try{
            foreach(bool active in new[]{false,true})foreach(int desired in new[]{0,48,100}){
                Fake mode=new Fake(active,50);
                Check(mode.Apply(true,desired).Status=="ok"&&mode.Boolean.Value&&mode.Number.Value==desired,"Enable must apply and verify exact0/mid/100 strength for either initial mode.");
                Check(active||mode.Calls.IndexOf("strength:"+desired)<mode.Calls.IndexOf("mode:True"),"Strength must be confirmed before enabling Night Light.");
                int writes=mode.ModeSets+mode.StrengthSets;
                Check(mode.Apply(true,desired).Status=="ok"&&mode.ModeSets+mode.StrengthSets==writes,"A repeated matching action must not write either value again.");
                Check(mode.ModeDisposed&&mode.StrengthDisposed,"Every opened session must be disposed.");
            }
            Fake disabled=new Fake(true,73);
            Check(disabled.Apply(false,25).Status=="ok"&&!disabled.Boolean.Value&&disabled.Number.Value==73&&disabled.StrengthSets==0&&disabled.StrengthOpens==0,"A disable action must preserve strength and never open its setter.");
            foreach(int invalid in new[]{-1,101}){
                Fake state=new Fake(false,50);
                Check(state.Apply(true,invalid).Status=="skipped"&&state.ModeOpens==0&&state.StrengthOpens==0,"Out-of-range strength must fail before opening any session.");
            }
            foreach(TrayPreferenceSnapshot blocked in new[]{null,new TrayPreferenceSnapshot(false,false,true,false,false),new TrayPreferenceSnapshot(false,true,false,false,false),new TrayPreferenceSnapshot(false,true,true,true,false),new TrayPreferenceSnapshot(false,true,true,false,true)}){
                Fake state=new Fake(false,50);state.Boolean=blocked;
                Check(state.Apply(true,100).Status=="skipped"&&state.ModeSets==0&&state.StrengthSets==0,"Unavailable, disabled, managed, updating or unknown mode must not change anything.");
                Check(!NightLightControl.Available(state.OpenMode,state.OpenStrength),"An unavailable mode must not be offered in the constructor.");
            }
            foreach(UInt32PreferenceSnapshot blocked in new[]{null,new UInt32PreferenceSnapshot(50,false,true,false,false),new UInt32PreferenceSnapshot(50,true,false,false,false),new UInt32PreferenceSnapshot(50,true,true,true,false),new UInt32PreferenceSnapshot(50,true,true,false,true),Amount(101)}){
                Fake state=new Fake(false,50);state.Number=blocked;
                Check(state.Apply(true,100).Status=="skipped"&&state.ModeSets==0&&state.StrengthSets==0,"Unavailable, disabled, managed, updating, unknown or malformed strength must not enable mode.");
            }
            Fake unchanged=new Fake(false,50);unchanged.IgnoreStrength=true;
            Check(unchanged.Apply(true,100).Status=="skipped"&&!unchanged.Boolean.Value&&unchanged.ModeSets==0,"Accepted strength setter without readback must not enable mode.");
            Fake partial=new Fake(false,50);partial.ThrowAfterStrength=true;
            Check(partial.Apply(true,100).Status=="skipped"&&!partial.Boolean.Value&&partial.ModeSets==0&&partial.Number.Value==100,"A partial strength failure must not enable mode or report success.");
            partial.ThrowAfterStrength=false;
            Check(NightLightControl.Restore(new NightLightSnapshot(false,50),partial.OpenMode,partial.OpenStrength).Status=="ok"&&!partial.Boolean.Value&&partial.Number.Value==50,"Restore must restore strength even when the original mode was off.");
            Fake modeFailure=new Fake(false,50);modeFailure.IgnoreMode=true;
            Check(modeFailure.Apply(true,100).Status=="skipped"&&!modeFailure.Boolean.Value,"Unconfirmed mode setter must not report success.");
            Fake changedAfterMode=new Fake(false,50);changedAfterMode.ChangeStrengthOnEnable=true;
            Check(changedAfterMode.Apply(true,100).Status=="skipped","Final mode activation must not hide an unexpected strength change.");
            Fake changedAfterStrength=new Fake(true,50);changedAfterStrength.DisableOnStrengthSet=true;
            Check(changedAfterStrength.Apply(true,100).Status=="ok"&&changedAfterStrength.Boolean.Value&&changedAfterStrength.Number.Value==100&&changedAfterStrength.ModeSets==1,"Initial mode state must not replace readback after a strength change.");
            Fake failedOpen=new Fake(false,50);
            Check(NightLightControl.Apply(true,100,false,failedOpen.OpenMode,delegate{throw new Exception("No intensity interface");}).Status=="skipped"&&failedOpen.ModeSets==0&&failedOpen.ModeDisposed,"A missing strength interface must not enable mode and must close the mode session.");
            Fake restoreOn=new Fake(false,100);
            Check(NightLightControl.Restore(new NightLightSnapshot(true,0),restoreOn.OpenMode,restoreOn.OpenStrength).Status=="ok"&&restoreOn.Boolean.Value&&restoreOn.Number.Value==0&&restoreOn.Calls.IndexOf("strength:0")<restoreOn.Calls.IndexOf("mode:True"),"Restore an enabled snapshot with strength before activation.");
            Fake restoreOff=new Fake(true,0);
            Check(NightLightControl.Restore(new NightLightSnapshot(false,100),restoreOff.OpenMode,restoreOff.OpenStrength).Status=="ok"&&!restoreOff.Boolean.Value&&restoreOff.Number.Value==100&&restoreOff.Calls.IndexOf("mode:False")<restoreOff.Calls.IndexOf("strength:100"),"Restore a disabled snapshot by disabling before restoring intensity.");
            Fake capture=new Fake(false,100);
            NightLightSnapshot saved=NightLightControl.Capture(capture.OpenMode,capture.OpenStrength);
            Check(!saved.Enabled&&saved.Strength==100&&capture.ModeSets==0&&capture.StrengthSets==0&&capture.ModeDisposed&&capture.StrengthDisposed,"Capture must be read-only, typed and complete.");
            Check(NightLightControl.Available(capture.OpenMode,capture.OpenStrength)&&capture.ModeSets==0&&capture.StrengthSets==0,"Availability must not change mode or strength.");
            DelayedFake delayed=new DelayedFake(false,50);
            StepResult delayedResult=NightLightControl.Apply(true,100,false,delayed.OpenMode,delayed.OpenStrength,delayed.Tick,delegate{return delayed.Ticks<12;});
            Check(delayedResult.Status=="ok"&&delayed.CommittedMode&&delayed.CommittedStrength==100&&!delayed.EnabledBeforeStrengthCommit&&delayed.PostSetReads==0&&!delayed.ObservedWithWriterOpen,
                "Optimistic item values must not substitute for fresh committed mode/strength, and strength must commit before enable.");
            DelayedFake restorePending=new DelayedFake(false,25);restorePending.OptimisticInitialStrength=100;
            StepResult restored=NightLightControl.Restore(new NightLightSnapshot(false,100),restorePending.OpenMode,restorePending.OpenStrength,restorePending.Tick,delegate{return restorePending.Ticks<12;});
            NightLightSnapshot immediate=NightLightControl.Capture(restorePending.OpenMode,restorePending.OpenStrength);
            Check(restored.Status=="ok"&&restorePending.OptimisticInitialReads>0&&restorePending.ModeSets==1&&restorePending.StrengthSets==1&&!immediate.Enabled&&immediate.Strength==100,
                "Explicit restore must issue both targets despite an optimistic old match and verify an immediate fresh snapshot.");
            DelayedFake neverCommitted=new DelayedFake(false,50);neverCommitted.NeverCommit=true;
            Check(NightLightControl.Apply(true,100,false,neverCommitted.OpenMode,neverCommitted.OpenStrength,neverCommitted.Tick,delegate{return neverCommitted.Ticks<4;}).Status=="skipped"&&neverCommitted.ModeSets==0,
                "An uncommitted strength update must time out without enabling Night Light.");
            Console.WriteLine("PASS: "+checks+" Night Light broker checks; substituted sessions only, no Windows settings changed.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    sealed class Fake
    {
        internal TrayPreferenceSnapshot Boolean;
        internal UInt32PreferenceSnapshot Number;
        internal int ModeSets,StrengthSets,ModeOpens,StrengthOpens;
        internal bool IgnoreStrength,IgnoreMode,ThrowAfterStrength,ChangeStrengthOnEnable,DisableOnStrengthSet,ModeDisposed,StrengthDisposed;
        int waits;
        internal List<string> Calls=new List<string>();
        internal Fake(bool mode,uint strength){Boolean=Mode(mode);Number=Amount(strength);}
        internal ITrayPreferenceSession OpenMode(){ModeOpens++;return new BooleanSession(this);}
        internal IUInt32PreferenceSession OpenStrength(){StrengthOpens++;return new NumberSession(this);}
        internal StepResult Apply(bool mode,int strength){waits=0;return NightLightControl.Apply(mode,strength,false,OpenMode,OpenStrength,delegate{waits++;},delegate{return waits<4;});}
        sealed class BooleanSession:ITrayPreferenceSession
        {
            readonly Fake source;internal BooleanSession(Fake value){source=value;}
            public TrayPreferenceSnapshot Read(){return source.Boolean;}
            public void Set(bool value){source.ModeSets++;source.Calls.Add("mode:"+value);if(!source.IgnoreMode)source.Boolean=Mode(value);if(value&&source.ChangeStrengthOnEnable)source.Number=Amount(48);}
            public void Dispose(){source.ModeDisposed=true;}
        }
        sealed class NumberSession:IUInt32PreferenceSession
        {
            readonly Fake source;internal NumberSession(Fake value){source=value;}
            public UInt32PreferenceSnapshot Read(){return source.Number;}
            public void Set(uint value){source.StrengthSets++;source.Calls.Add("strength:"+value);if(!source.IgnoreStrength)source.Number=Amount(value);if(source.DisableOnStrengthSet)source.Boolean=Mode(false);if(source.ThrowAfterStrength)throw new Exception("Partial failure");}
            public void Dispose(){source.StrengthDisposed=true;}
        }
    }
    sealed class DelayedFake
    {
        internal bool CommittedMode,NeverCommit,EnabledBeforeStrengthCommit,ObservedWithWriterOpen;
        internal uint CommittedStrength;
        internal uint? OptimisticInitialStrength;
        internal int Ticks,ModeSets,StrengthSets,PostSetReads,OptimisticInitialReads;
        int activeWriters,modeDue,strengthDue;
        bool? pendingMode;uint? pendingStrength;
        internal DelayedFake(bool mode,uint strength){CommittedMode=mode;CommittedStrength=strength;}
        internal void Tick(){Ticks++;if(!NeverCommit){if(pendingMode.HasValue&&Ticks>=modeDue){CommittedMode=pendingMode.Value;pendingMode=null;}if(pendingStrength.HasValue&&Ticks>=strengthDue){CommittedStrength=pendingStrength.Value;pendingStrength=null;}}}
        internal ITrayPreferenceSession OpenMode(){if(activeWriters!=0)ObservedWithWriterOpen=true;return new DelayedMode(this);}
        internal IUInt32PreferenceSession OpenStrength(){if(activeWriters!=0)ObservedWithWriterOpen=true;return new DelayedStrength(this);}
        sealed class DelayedMode:ITrayPreferenceSession
        {
            readonly DelayedFake source;bool value,writer;
            internal DelayedMode(DelayedFake parent){source=parent;value=parent.CommittedMode;}
            public TrayPreferenceSnapshot Read(){if(writer)source.PostSetReads++;return Mode(value);}
            public void Set(bool next){if(!writer){writer=true;source.activeWriters++;}source.ModeSets++;value=next;source.pendingMode=next;source.modeDue=source.Ticks+2;if(next&&source.CommittedStrength!=100)source.EnabledBeforeStrengthCommit=true;}
            public void Dispose(){if(writer){writer=false;source.activeWriters--;}}
        }
        sealed class DelayedStrength:IUInt32PreferenceSession
        {
            readonly DelayedFake source;uint value;bool writer;
            internal DelayedStrength(DelayedFake parent){source=parent;value=parent.CommittedStrength;if(parent.StrengthSets==0&&parent.OptimisticInitialStrength.HasValue){value=parent.OptimisticInitialStrength.Value;parent.OptimisticInitialReads++;}}
            public UInt32PreferenceSnapshot Read(){if(writer)source.PostSetReads++;return Amount(value);}
            public void Set(uint next){if(!writer){writer=true;source.activeWriters++;}source.StrengthSets++;value=next;source.pendingStrength=next;source.strengthDue=source.Ticks+2;}
            public void Dispose(){if(writer){writer=false;source.activeWriters--;}}
        }
    }
}
