using System;
using System.Diagnostics;
using System.Threading;
using WindowsPreferences;

public sealed class NightLightSnapshot
{
    public readonly bool Enabled;
    public readonly int Strength;
    public NightLightSnapshot(bool enabled,int strength)
    {
        if(strength<0||strength>100)throw new ArgumentOutOfRangeException("strength");
        Enabled=enabled;Strength=strength;
    }
}

public static class NightLightControl
{
    static ITrayPreferenceSession OpenMode(){return NativeTrayPreferences.Open(TrayPreferenceKind.NightLightEnabled);}
    public static bool Available(){return Available(OpenMode,NativeNightLightStrength.Open);}
    internal static bool Available(Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> strength)
    {
        try{
            using(ITrayPreferenceSession state=mode())using(IUInt32PreferenceSession level=strength())
                return Writable(state.Read())&&Writable(level.Read());
        }catch{return false;}
    }
    public static NightLightSnapshot Capture(){return Capture(OpenMode,NativeNightLightStrength.Open);}
    internal static NightLightSnapshot Capture(Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> strength)
    {
        using(ITrayPreferenceSession state=mode())using(IUInt32PreferenceSession level=strength()){
            TrayPreferenceSnapshot active=state.Read();UInt32PreferenceSnapshot amount=level.Read();
            if(!Writable(active)||!Writable(amount))throw new InvalidOperationException("Ночной свет недоступен.");
            return new NightLightSnapshot(active.Value,(int)amount.Value);
        }
    }
    public static void Restore(NightLightSnapshot snapshot)
    {
        StepResult result=Restore(snapshot,OpenMode,NativeNightLightStrength.Open);
        if(result.Status!="ok")throw new InvalidOperationException(result.Detail);
    }
    internal static StepResult Restore(NightLightSnapshot snapshot,Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> strength)
    {
        if(snapshot==null)throw new ArgumentNullException("snapshot");
        return Apply(snapshot.Enabled,snapshot.Strength,true,mode,strength);
    }
    internal static StepResult Restore(NightLightSnapshot snapshot,Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> strength,Action pause,Func<bool> canWait)
    {
        if(snapshot==null)throw new ArgumentNullException("snapshot");
        return Apply(snapshot.Enabled,snapshot.Strength,true,mode,strength,pause,canWait);
    }
    public static StepResult Apply(bool enabled,int strength){return Apply(enabled,strength,false,OpenMode,NativeNightLightStrength.Open);}
    internal static StepResult Apply(bool enabled,int strength,bool restoring,Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> amount)
    {
        Stopwatch deadline=Stopwatch.StartNew();
        return Apply(enabled,strength,restoring,mode,amount,delegate{Thread.Sleep(25);},delegate{return deadline.ElapsedMilliseconds<3000;});
    }
    internal static StepResult Apply(bool enabled,int strength,bool restoring,Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> amount,Action pause,Func<bool> canWait)
    {
        const string name="NightLight";
        if(strength<0||strength>100)return new StepResult(name,"skipped","Некорректная интенсивность ночного света.");
        try{
            TrayPreferenceSnapshot initial;
            using(ITrayPreferenceSession state=mode())initial=state.Read();
            if(!Writable(initial))return new StepResult(name,"skipped","Ночной свет недоступен.");
            // Restore issues explicit targets even if an initial model still
            // exposes the old target while an earlier request is committing.
            if(!enabled&&(restoring||initial.Value)){
                WriteMode(false,mode);
                if(!Verify(false,null,mode,amount,pause,canWait))return new StepResult(name,"skipped","Выключение ночного света не подтверждено.");
            }
            if(enabled||restoring){
                UInt32PreferenceSnapshot current;
                using(IUInt32PreferenceSession level=amount())current=level.Read();
                if(!Writable(current))return new StepResult(name,"skipped","Интенсивность ночного света недоступна.");
                if(restoring||current.Value!=(uint)strength)WriteStrength((uint)strength,amount);
                // Do not enable mode on an optimistic same-item readback.
                if(!Verify(null,strength,mode,amount,pause,canWait))return new StepResult(name,"skipped","Интенсивность ночного света не подтверждена.");
            }
            TrayPreferenceSnapshot active;
            using(ITrayPreferenceSession state=mode())active=state.Read();
            if(!Writable(active))return new StepResult(name,"skipped","Состояние ночного света недоступно.");
            if(active.Value!=enabled||(restoring&&enabled))WriteMode(enabled,mode);
            if(!Verify(enabled,enabled||restoring?(int?)strength:null,mode,amount,pause,canWait))
                return new StepResult(name,"skipped","Итоговые параметры ночного света не подтверждены.");
            return new StepResult(name,"ok",enabled?"Ночной свет включён, интенсивность "+strength+"%.":"Ночной свет выключен.");
        }catch{return new StepResult(name,"skipped","Управление ночным светом недоступно.");}
    }
    static void WriteMode(bool value,Func<ITrayPreferenceSession> open)
    {
        using(ITrayPreferenceSession state=open()){
            if(!Writable(state.Read()))throw new InvalidOperationException("Ночной свет недоступен.");
            state.Set(value);
        }
    }
    static void WriteStrength(uint value,Func<IUInt32PreferenceSession> open)
    {
        using(IUInt32PreferenceSession level=open()){
            if(!Writable(level.Read()))throw new InvalidOperationException("Интенсивность ночного света недоступна.");
            level.Set(value);
        }
    }
    static bool Verify(bool? enabled,int? strength,Func<ITrayPreferenceSession> mode,Func<IUInt32PreferenceSession> amount,Action pause,Func<bool> canWait)
    {
        // Each observation uses new models after mutation sessions are disposed.
        // This bounded check belongs only to the operation, never a background timer.
        while(true){
            bool matches=true;
            if(enabled.HasValue){
                using(ITrayPreferenceSession state=mode()){
                    TrayPreferenceSnapshot current=state.Read();
                    if(current==null||!current.IsApplicable||!current.IsEnabled||current.IsSetByGroupPolicy)return false;
                    matches=!current.IsUpdating&&current.Value==enabled.Value;
                }
            }
            if(strength.HasValue){
                using(IUInt32PreferenceSession level=amount()){
                    UInt32PreferenceSnapshot current=level.Read();
                    if(current==null||!current.IsApplicable||!current.IsEnabled||current.IsSetByGroupPolicy||current.Value>100)return false;
                    matches=matches&&!current.IsUpdating&&current.Value==(uint)strength.Value;
                }
            }
            if(matches)return true;
            if(!canWait())return false;
            pause();
        }
    }
    static bool Writable(TrayPreferenceSnapshot state)
    {
        return state!=null&&state.IsApplicable&&state.IsEnabled&&!state.IsSetByGroupPolicy&&!state.IsUpdating;
    }
    static bool Writable(UInt32PreferenceSnapshot state)
    {
        return state!=null&&state.IsApplicable&&state.IsEnabled&&!state.IsSetByGroupPolicy&&!state.IsUpdating&&state.Value<=100;
    }
}
