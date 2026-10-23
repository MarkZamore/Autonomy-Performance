using System;
using WindowsPreferences;
static class BrokerPreferenceTests
{
 static int checks;
 static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Main(){
  foreach(WindowsPreferenceId id in Enum.GetValues(typeof(WindowsPreferenceId)))Check(WindowsPreferenceCatalog.Get(id).SettingId.StartsWith("SystemSettings_"),"Closed catalog");
  StepResult result;
  var f=new Fake(false);
  Check(BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>true,"test",()=>f,false,out result)&&result.Status=="ok"&&f.Sets==1,"Boolean applies");
  BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>true,"test",()=>f,false,out result);Check(f.Sets==1,"Repeated matching value must not write");
  foreach(int flag in new[]{0,1,2,3}){
   f=new Fake(false){Applicable=flag!=0,Enabled=flag!=1,Policy=flag==2,Updating=flag==3};
   Check(BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>true,"test",()=>f,false,out result)&&result.Status=="skipped"&&f.Sets==0,"Unavailable/policy/updating cannot mutate");
  }
  f=new Fake(false){Enabled=false};Check(!BrokerWindowsControl.Apply(WindowsPreferenceId.EnergySaver,s=>true,"test",()=>f,true,out result)&&f.Sets==0,"Explicit inactive context may use scoped native adapter");
  f.Policy=true;Check(BrokerWindowsControl.Apply(WindowsPreferenceId.EnergySaver,s=>true,"test",()=>f,true,out result)&&f.Sets==0,"Policy never enables fallback");
  f=new Fake(false){Ignore=true};BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>true,"test",()=>f,false,out result);Check(result.Status=="skipped","An ignored write is not success");
  f=new Fake(false);BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>1,"test",()=>f,false,out result);Check(result.Status=="skipped"&&f.Sets==0,"Unexpected value type cannot reach setter");
  f=new Fake("dark"){Choices=new[]{"light","dark","custom"}};BrokerWindowsControl.Apply(WindowsPreferenceId.DarkColorMode,s=>s.Choices[0],"test",()=>f,false,out result);Check(result.Status=="ok"&&Equals(f.Value,"light"),"Localized choices are values, not registry indices");
  Check(BrokerWindowsControl.Apply(WindowsPreferenceId.Widgets,s=>true,"test",()=>{throw new Exception();},true,out result)&&result.Status=="skipped","Failed broker must not fall back to a blind write");
  bool rejected=false;try{WindowsPreferenceCatalog.Get((WindowsPreferenceId)9999);}catch(ArgumentOutOfRangeException){rejected=true;}Check(rejected,"Unknown IDs rejected");
  Console.WriteLine("Broker preferences: "+checks+" PASS");
 }
 sealed class Fake:IWindowsPreferenceSession {
  internal object Value;internal bool Applicable=true,Enabled=true,Policy,Updating,Ignore;internal int Sets;internal string[] Choices=new string[0];
  internal Fake(object value){Value=value;}
  public WindowsPreferenceSnapshot Read(){return new WindowsPreferenceSnapshot(Value,Applicable,Enabled,Policy,Updating,Choices);}
  public void Set(object value){Sets++;if(!Ignore)Value=value;}
  public void Dispose(){}
 }
}
