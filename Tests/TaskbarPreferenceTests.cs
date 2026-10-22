using System;
using WindowsPreferences;

// Production TaskbarControl with substituted sessions. No Windows setter runs.
static class TaskbarPreferenceTests
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    static TrayPreferenceSnapshot Ready(bool value){return new TrayPreferenceSnapshot(value,true,true,false,false);}
    static int Main()
    {
        try{
            foreach(bool battery in new[]{false,true})foreach(bool target in new[]{false,true}){
                FakeSession session=new FakeSession(Ready(!target));TrayPreferenceKind requested=(TrayPreferenceKind)99;
                Func<TrayPreferenceKind,ITrayPreferenceSession> open=delegate(TrayPreferenceKind kind){requested=kind;return session;};
                StepResult result=TaskbarControl.SetPreference(battery,target,open,true);
                Check(result.Status=="ok"&&session.State.Value==target&&session.Sets==1&&session.Reads==2&&session.Disposed,"A changed preference needs effective readback and session disposal.");
                Check(requested==(battery?TrayPreferenceKind.BatteryPercentage:TrayPreferenceKind.ClockSeconds),"Only the selected fixed broker action may be opened.");
                result=TaskbarControl.SetPreference(battery,target,open,true);
                Check(result.Status=="ok"&&session.Sets==1,"An already matching preference must not be applied again.");
            }
            foreach(TrayPreferenceSnapshot blocked in new[]{null,new TrayPreferenceSnapshot(false,false,true,false,false),new TrayPreferenceSnapshot(false,true,false,false,false),new TrayPreferenceSnapshot(false,true,true,true,false),new TrayPreferenceSnapshot(false,true,true,false,true)}){
                FakeSession session=new FakeSession(blocked);
                Func<TrayPreferenceKind,ITrayPreferenceSession> open=delegate{return session;};
                Check(!TaskbarControl.Available(false,open,true)&&session.Sets==0,"Unknown, unavailable, disabled, managed and updating settings must not be offered.");
                Check(TaskbarControl.SetPreference(false,true,open,true).Status=="skipped"&&session.Sets==0&&session.Disposed,"An unavailable native setting must not be changed.");
            }
            int opens=0;
            Func<TrayPreferenceKind,ITrayPreferenceSession> absent=delegate{opens++;throw new Exception("The factory must not run without a battery.");};
            Check(!TaskbarControl.Available(true,absent,false)&&TaskbarControl.SetPreference(true,true,absent,false).Status=="skipped"&&opens==0,"No battery must skip before opening a native session.");
            FakeSession mismatch=new FakeSession(Ready(false));mismatch.IgnoreSet=true;
            Check(TaskbarControl.SetPreference(true,true,delegate{return mismatch;},true).Status=="skipped"&&mismatch.Reads==2&&mismatch.Disposed,"An accepted setter without effective change is not success.");
            FakeSession error=new FakeSession(Ready(false));error.ThrowSet=true;
            Check(TaskbarControl.SetPreference(false,true,delegate{return error;},true).Status=="skipped"&&error.Disposed,"A setter error must be skipped with session cleanup.");
            FakeSession unreadable=new FakeSession(Ready(false));unreadable.ThrowRead=true;
            Check(TaskbarControl.SetPreference(false,true,delegate{return unreadable;},true).Status=="skipped"&&unreadable.Sets==0&&unreadable.Disposed,"An unreadable setting must never be applied.");
            FakeSession available=new FakeSession(Ready(false));
            Check(TaskbarControl.Available(false,delegate{return available;},false)&&available.Sets==0&&available.Disposed,"Seconds availability must be independent of battery hardware and remain read-only.");
            Check(!TaskbarControl.Available(false,delegate{throw new Exception("No broker");},true),"Missing native API must not become available.");
            Console.WriteLine("PASS: "+checks+" taskbar preference checks; substituted sessions only, no Windows settings changed.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    sealed class FakeSession:ITrayPreferenceSession
    {
        internal TrayPreferenceSnapshot State;
        internal int Reads,Sets;
        internal bool Disposed,IgnoreSet,ThrowSet,ThrowRead;
        internal FakeSession(TrayPreferenceSnapshot state){State=state;}
        public TrayPreferenceSnapshot Read(){Reads++;if(ThrowRead)throw new Exception("Unknown state");return State;}
        public void Set(bool value){Sets++;if(ThrowSet)throw new Exception("Rejected setter");if(!IgnoreSet)State=Ready(value);}
        public void Dispose(){Disposed=true;}
    }
}
// Public entrypoints are deliberately unused. An accidental hardware call fails.
public static class SensorCapabilities
{
    public static bool HasBattery(){throw new InvalidOperationException("Tests must use their substituted hardware state.");}
}
