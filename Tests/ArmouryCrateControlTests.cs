using System;
using System.IO;
using System.Text;

class ArmouryCrateControlTests
{
    static int checks;
    static void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    static ArmouryCrateSnapshot Ready(){return new ArmouryCrateSnapshot{Installed=true,ReceiverAvailable=true,StateKnown=true,PowerKnown=true,PowerOnline=true,SupportedModes=31,EnabledModes=31,SelectedMode=8,ManualConfigured=true,ActivePowerScheme="scheme"};}
    static StepResult Skipped(int mode,ArmouryCrateSnapshot state)
    {
        int sends=0,waits=0;StepResult result=ArmouryCrateControl.Apply(mode,delegate{return state;},delegate(int target){sends++;return true;},delegate(int delay){waits++;});
        Check(result.Name=="ArmouryMode"&&result.Status=="skipped","Unavailable action must skip: "+mode);
        Check(sends==0&&waits==0,"Skipped action must not send or wait");return result;
    }
    static byte[] Map(params object[] pairs)
    {
        using(MemoryStream stream=new MemoryStream())using(BinaryWriter writer=new BinaryWriter(stream)){
            for(int index=0;index<pairs.Length;index+=2){byte[] key=Encoding.UTF8.GetBytes((string)pairs[index]);object value=pairs[index+1];byte[] bytes=value is byte[]?(byte[])value:Encoding.Unicode.GetBytes((string)value);writer.Write((byte)key.Length);writer.Write(key);writer.Write((byte)(value is byte[]?0x20:0x10));writer.Write(bytes.Length);writer.Write(bytes);}
            return stream.ToArray();
        }
    }
    static void Main()
    {
        for(int mode=1;mode<=5;mode++){
            Check(ArmouryCrateControl.ModeBit(mode)==1<<(mode-1),"All five mode mappings must match ASUS bitmask");
            ArmouryCrateSnapshot state=Ready();state.SelectedMode=mode==4?2:8;int sent=0,waits=0;
            StepResult result=ArmouryCrateControl.Apply(mode,delegate{return state;},delegate(int target){sent=target;state.SelectedMode=target;return true;},delegate(int delay){Check(delay==50,"Polling interval is bounded");waits++;});
            Check(result.Status=="ok"&&sent==1<<(mode-1)&&waits==10,"Each mode must use the verified transport and settled readback");
        }
        foreach(int mode in new[]{-1,0,6,Int32.MaxValue}){Check(ArmouryCrateControl.ModeBit(mode)==0,"Invalid/unchanged mode has no command bit");Skipped(mode,Ready());}
        ArmouryCrateSnapshot absent=Ready();absent.Installed=false;Skipped(2,absent);
        ArmouryCrateSnapshot unavailable=Ready();unavailable.ReceiverAvailable=false;Skipped(2,unavailable);
        ArmouryCrateSnapshot unknown=Ready();unknown.StateKnown=false;Skipped(2,unknown);
        ArmouryCrateSnapshot locked=Ready();locked.Locked=true;Skipped(2,locked);
        ArmouryCrateSnapshot unsupported=Ready();unsupported.SupportedModes=15;Skipped(5,unsupported);
        ArmouryCrateSnapshot disabled=Ready();disabled.EnabledModes=15;Skipped(5,disabled);
        ArmouryCrateSnapshot battery=Ready();battery.PowerOnline=false;Skipped(4,battery);
        ArmouryCrateSnapshot powerUnknown=Ready();powerUnknown.PowerKnown=false;Skipped(2,powerUnknown);
        ArmouryCrateSnapshot manualMissing=Ready();manualMissing.ManualConfigured=false;manualMissing.SelectedMode=16;Skipped(5,manualMissing);
        int sends=0,settlingWaits=0;ArmouryCrateSnapshot selected=Ready();StepResult same=ArmouryCrateControl.Apply(4,delegate{return selected;},delegate(int target){sends++;return true;},delegate(int delay){settlingWaits++;});Check(same.Status=="ok"&&sends==1&&settlingWaits==10,"An explicitly selected cached mode must still send and await settlement");
        sends=0;
        int reads=0;ArmouryCrateSnapshot changing=Ready();StepResult changed=ArmouryCrateControl.Apply(2,delegate{reads++;if(reads==2)changing.EnabledModes=8;return changing;},delegate(int target){sends++;return true;},delegate(int delay){});Check(changed.Status=="skipped"&&sends==0,"Availability must be rechecked before sending");
        reads=0;ArmouryCrateSnapshot transition=Ready();StepResult transitioned=ArmouryCrateControl.Apply(2,delegate{reads++;if(reads==2)transition.PowerOnline=false;return transition;},delegate(int target){sends++;return true;},delegate(int delay){});Check(transitioned.Status=="skipped"&&sends==0,"A power-source transition must not emit a command");
        int polls=0;ArmouryCrateSnapshot neverChanges=Ready();StepResult timeout=ArmouryCrateControl.Apply(2,delegate{return neverChanges;},delegate(int target){polls++;return true;},delegate(int delay){polls++;});Check(timeout.Status=="error"&&polls==50,"Unconfirmed switch must terminate with an error");
        StepResult failed=ArmouryCrateControl.Apply(2,delegate{return Ready();},delegate(int target){return false;},delegate(int delay){throw new Exception("Must not wait after delivery fails");});Check(failed.Status=="error","Transport failure cannot report success");
        StepResult exception=ArmouryCrateControl.Apply(2,delegate{throw new InvalidOperationException();},delegate(int target){return true;},delegate(int delay){});Check(exception.Status=="error","A backend exception must not escape to later actions");
        string identifier="{11111111-2222-3333-4444-555555555555}";byte[] profile=Map("Name","Manual mode 1"),defaults=Map("ProfileList",identifier,identifier,profile),configured=Map("ProfileList",identifier,identifier,profile,"Apply",identifier);
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(defaults),"ASUS default named profile without Apply is unconfigured");
        Check(ArmouryCrateControl.HasConfiguredManualProfile(configured),"A saved applied profile is configured");
        byte[] hardwareValues={8,67,80,85,76,105,109,105,116,2,80,0,0,0,8,70,97,110,67,117,114,118,101,64,4,0,0,0,20,40,60,80};
        byte[] configuredValues=new byte[hardwareValues.Length+profile.Length];Array.Copy(hardwareValues,configuredValues,hardwareValues.Length);Array.Copy(profile,0,configuredValues,hardwareValues.Length,profile.Length);
        Check(ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,identifier,configuredValues,"Apply",identifier)),"Applied profile must tolerate opaque numeric limits and fan-curve data owned by ASUS");
        Check(ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,identifier,hardwareValues,"Apply",identifier)),"Applied profile must not depend on profile naming or decoding ASUS hardware settings");
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,"Apply",identifier)),"A dangling Apply profile must not pass");
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,identifier,profile,"Apply","unknown")),"Unknown Apply value must skip");
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,identifier,new byte[0],"Apply",identifier)),"Empty profile must skip");
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(null)&&!ArmouryCrateControl.HasConfiguredManualProfile(new byte[524289]),"Missing and oversized profiles must skip");
        byte[] truncated=new byte[configured.Length-1];Array.Copy(configured,truncated,truncated.Length);Check(!ArmouryCrateControl.HasConfiguredManualProfile(truncated),"Truncated profile cannot be treated as configured");
        byte[] corrupt=(byte[])configured.Clone();corrupt[12]=0x7f;Check(!ArmouryCrateControl.HasConfiguredManualProfile(corrupt),"Unknown ASUS value encoding must skip");
        Check(!ArmouryCrateControl.HasConfiguredManualProfile(Map("ProfileList",identifier,identifier,profile,"Apply",identifier,"Apply",identifier)),"Ambiguous duplicate keys must skip");
        Console.WriteLine("Armoury Crate: "+checks+" synthetic checks PASS; no real mode requests sent.");
    }
}
