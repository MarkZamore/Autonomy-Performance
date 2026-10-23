using System;using System.Collections.Generic;
class EnergySaverPreferenceTests {
 static int checks;static void Check(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static void Main(){foreach(bool desired in new[]{false,true})foreach(uint initial in new[]{0u,1u,2u}) {
 uint state=initial;int forces=0,observes=0;
 var result=WindowsControl.SetEnergySaver(desired,value=>new StepResult("EnergySaver","ok",""),()=>state,(value,target)=>{forces++;state=value;return new List<StepResult>{new StepResult("EnergySaver","ok","")};},value=>{observes++;return true;});
 Check(result[0].Status=="ok"&&state==(desired?1u:2u),"A matching Boolean must not retain an automatic or opposite override");
 Check(forces==(initial==(desired?1u:2u)?0:1),"Only a mismatched forced context needs the native override");
 Check(observes==(forces==0?1:0),"Effective state must be verified by the selected adapter");
 }
 foreach(string status in new[]{"skipped","error"}){int writes=0;var result=WindowsControl.SetEnergySaver(false,v=>new StepResult("EnergySaver",status,""),()=>0u,(v,t)=>{writes++;return null;},v=>true);Check(result[0].Status==status&&writes==0,"Failed or policy-blocked broker cannot be bypassed");}
 var ineffective=WindowsControl.SetEnergySaver(false,v=>new StepResult("EnergySaver","ok",""),()=>2u,(v,t)=>null,v=>false);Check(ineffective[0].Status=="error","A persisted override without effective convergence is not success");
 int fallback=0;var unsupported=WindowsControl.SetEnergySaver(true,v=>null,()=>0u,(v,t)=>{fallback++;return new List<StepResult>{new StepResult("EnergySaver","ok","")};},v=>false);Check(unsupported[0].Status=="ok"&&fallback==1,"An uncertified build retains its original scoped adapter");
 Console.WriteLine("Energy saver: "+checks+" fake-adapter checks PASS; no Windows state changed.");
 }
}
