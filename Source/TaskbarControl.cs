using System;
using WindowsPreferences;

public static class TaskbarControl
{
    public static bool Available(bool battery)
    {
        return Available(battery,NativeTrayPreferences.Open,!battery||SensorCapabilities.HasBattery());
    }
    internal static bool Available(bool battery,Func<TrayPreferenceKind,ITrayPreferenceSession> open,bool hasBattery)
    {
        if(battery&&!hasBattery)return false;
        try{using(ITrayPreferenceSession session=open(Kind(battery)))return Writable(session.Read());}catch{return false;}
    }
    public static StepResult SetPreference(bool battery,bool enabled)
    {
        return SetPreference(battery,enabled,NativeTrayPreferences.Open,!battery||SensorCapabilities.HasBattery());
    }
    internal static StepResult SetPreference(bool battery,bool enabled,Func<TrayPreferenceKind,ITrayPreferenceSession> open,bool hasBattery)
    {
        string name=battery?"IsBatteryPercentageEnabled":"ShowSecondsInSystemClock";
        if(battery&&!hasBattery)return new StepResult(name,"skipped","Батарея не обнаружена.");
        try{
            using(ITrayPreferenceSession session=open(Kind(battery))){
                TrayPreferenceSnapshot before=session.Read();
                if(!Writable(before))return new StepResult(name,"skipped","Изменение параметра панели задач недоступно.");
                if(before.Value==enabled)return new StepResult(name,"ok","Параметр панели задач уже выбран.");
                session.Set(enabled);
                TrayPreferenceSnapshot after=session.Read();
                if(!Writable(after)||after.Value!=enabled)return new StepResult(name,"skipped","Изменение параметра панели задач не подтверждено.");
                return new StepResult(name,"ok","Параметр панели задач изменён и проверен.");
            }
        }catch{return new StepResult(name,"skipped","Настройка панели задач недоступна.");}
    }
    static TrayPreferenceKind Kind(bool battery){return battery?TrayPreferenceKind.BatteryPercentage:TrayPreferenceKind.ClockSeconds;}
    static bool Writable(TrayPreferenceSnapshot state)
    {
        return state!=null&&state.IsApplicable&&state.IsEnabled&&!state.IsSetByGroupPolicy&&!state.IsUpdating;
    }
}
