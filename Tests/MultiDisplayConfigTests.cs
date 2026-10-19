using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;

// Configuration and read-only endpoint tests; never changes display settings.
static class MultiDisplayConfigTests
{
    static int checks;
    static void Assert(bool value,string message) { checks++;if(!value)throw new InvalidOperationException(message); }
    static void Invalid(ModeConfiguration value,string message)
    {
        bool rejected=false;try { value.Validate(); } catch(InvalidDataException) { rejected=true; }
        Assert(rejected,message);
    }
    static ModeConfiguration One(DisplayModeConfiguration display)
    {
        return new ModeConfiguration { Displays=new[]{display} };
    }
    static DisplayModeConfiguration Target(string id)
    {
        return new DisplayModeConfiguration { DisplayId=id };
    }
    static string Normalize(string method,string value)
    {
        return (string)typeof(DisplayBrightnessControl).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{value});
    }
    [STAThread] static int Main()
    {
        try {
            ModeConfiguration empty=new ModeConfiguration();empty.Validate();
            Assert(!empty.HasActions&&empty.GetDisplayActions().Length==0,"An empty profile must remain empty.");
            empty.Displays=new DisplayModeConfiguration[0];empty.Validate();
            Assert(!empty.HasActions,"An empty display array must not enable Save.");
            ModeConfiguration noActions=One(Target("screen1"));noActions.Validate();
            Assert(!noActions.HasActions&&noActions.GetDisplayActions().Length==0,"An unchanged monitor must not count as an action.");
            foreach(string field in new[]{"DisplayWidth","RefreshRate","DisplayScale","Hdr","DynamicRefresh","ContentBrightness","Brightness"}) {
                DisplayModeConfiguration display=Target("screen1");
                if(field=="DisplayWidth") { display.DisplayWidth=1920;display.DisplayHeight=1080; }
                else typeof(DisplayModeConfiguration).GetField(field).SetValue(display,field=="RefreshRate"?(object)60U:field=="Brightness"?0:field=="DisplayScale"?150:1);
                ModeConfiguration value=One(display);value.Validate();
                Assert(value.HasActions&&value.GetDisplayActions().Length==1,field+" alone must enable Save.");
            }
            ModeConfiguration two=new ModeConfiguration { Displays=new[]{new DisplayModeConfiguration { DisplayId="screen1",RefreshRate=60,DisplayWidth=1920,DisplayHeight=1080,DisplayScale=150,Hdr=1,Brightness=37 },new DisplayModeConfiguration { DisplayId="screen2",RefreshRate=144,DynamicRefresh=2,ContentBrightness=3,Brightness=80 } } };
            two.Validate();
            DisplayModeConfiguration[] actions=two.GetDisplayActions();
            Assert(actions.Length==2&&actions[0].DisplayId=="screen1"&&actions[1].DisplayId=="screen2","Monitor order must be preserved.");
            JavaScriptSerializer serializer=new JavaScriptSerializer();
            ModeConfiguration roundTrip=serializer.Deserialize<ModeConfiguration>(serializer.Serialize(two));roundTrip.Validate();
            Assert(roundTrip.Displays.Length==2&&roundTrip.Displays[0].RefreshRate==60&&roundTrip.Displays[1].RefreshRate==144,"Both refresh rates must survive serialization.");
            Assert(roundTrip.Displays[0].DisplayScale==150&&roundTrip.Displays[0].Hdr==1&&roundTrip.Displays[1].DynamicRefresh==2&&roundTrip.Displays[1].ContentBrightness==3,"Independent display actions must survive serialization.");
            Assert(roundTrip.Displays[0].Brightness==37&&roundTrip.Displays[1].Brightness==80&&roundTrip.Brightness==-1,"Per-monitor brightness must not become a global action.");
            ModeConfiguration old=serializer.Deserialize<ModeConfiguration>("{\"Version\":2,\"DisplayId\":\"legacy\",\"RefreshRate\":60}");old.Validate();
            Assert(old.Displays==null&&old.GetDisplayActions().Length==1&&old.GetDisplayActions()[0].DisplayId=="legacy","Old single-display profiles must still work.");
            Assert(old.GetDisplayActions()[0].Brightness==-1,"Global brightness must never be duplicated into a legacy display action.");
            old.Brightness=100;old.Validate();Assert(old.Brightness==100&&old.GetDisplayActions()[0].Brightness==-1,"Legacy global brightness must stay separate.");
            ModeConfiguration mixed=new ModeConfiguration { DisplayId="legacy",RefreshRate=60,Displays=new[]{new DisplayModeConfiguration { DisplayId="new",RefreshRate=144 }} };mixed.Validate();
            Assert(mixed.GetDisplayActions().Length==2&&mixed.GetDisplayActions()[1].DisplayId=="legacy","Distinct old and new monitor actions remain compatible.");
            Invalid(new ModeConfiguration { Displays=new DisplayModeConfiguration[]{null} },"Null monitor entries must be rejected.");
            Invalid(One(new DisplayModeConfiguration { RefreshRate=60 }),"A monitor identity is required.");
            Invalid(One(new DisplayModeConfiguration { DisplayId=new string('x',1025),Hdr=1 }),"Overlong monitor identities must be rejected.");
            Invalid(new ModeConfiguration { Displays=new[]{Target("same"),Target("SAME")} },"Monitor identities must be unique case-insensitively.");
            Invalid(new ModeConfiguration { DisplayId="same",Hdr=1,Displays=new[]{Target("SAME")} },"Old/new duplicate monitor identities must be rejected.");
            Invalid(One(new DisplayModeConfiguration { DisplayId="screen1",RefreshRate=1 }),"Invalid refresh rates must be rejected.");
            Invalid(One(new DisplayModeConfiguration { DisplayId="screen1",DisplayWidth=1920 }),"Incomplete resolutions must be rejected.");
            Invalid(One(new DisplayModeConfiguration { DisplayId="screen1",DisplayWidth=65536,DisplayHeight=1080 }),"Oversized resolutions must be rejected.");
            foreach(string field in new[]{"Hdr","DynamicRefresh","ContentBrightness","DisplayScale","Brightness"}) {
                DisplayModeConfiguration display=Target("screen1");
                typeof(DisplayModeConfiguration).GetField(field).SetValue(display,field=="ContentBrightness"?4:field=="DisplayScale"?501:field=="Brightness"?101:3);
                Invalid(One(display),"Out-of-range "+field+" must be rejected.");
                typeof(DisplayModeConfiguration).GetField(field).SetValue(display,field=="Brightness"?-2:-1);
                Invalid(One(display),"Negative "+field+" must be rejected.");
            }
            List<DisplayModeConfiguration> many=new List<DisplayModeConfiguration>();
            for(int i=0;i<32;i++)many.Add(new DisplayModeConfiguration { DisplayId="screen"+i+new string('x',1000),RefreshRate=60 });
            ModeConfiguration maximum=new ModeConfiguration { Displays=many.ToArray() };maximum.Validate();
            Assert(maximum.GetDisplayActions().Length==32,"32 uniquely identified displays must remain representable.");
            Assert(System.Text.Encoding.UTF8.GetByteCount(serializer.Serialize(maximum))>4096,"Multi-display configuration must exercise the enlarged resource limit.");
            many.Add(Target("screen33"));Invalid(new ModeConfiguration { Displays=many.ToArray() },"More than 32 monitor targets must be rejected.");
            string monitor=@"\\?\DISPLAY#BOE0D7B#4&162da92a&0&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
            string expected=@"DISPLAY\BOE0D7B\4&162da92a&0&UID8388688";
            Assert(Normalize("NormalizeMonitor",monitor)==expected,"Monitor interface normalization must retain the full hardware instance.");
            Assert(Normalize("NormalizeInstance",expected+"_0")==expected,"WMI normalization must remove only its numeric suffix.");
            Assert(Normalize("NormalizeInstance",expected+"_12")==expected,"Numeric WMI suffixes must normalize consistently.");
            Assert(Normalize("NormalizeMonitor",@"DISPLAY1")==null&&Normalize("NormalizeMonitor",null)==null,"Display labels must never be used as brightness identities.");
            Assert(Normalize("NormalizeMonitor",@"\\?\DISPLAY#BOE0D7B#wrong#{not-a-guid}")==null,"Malformed interface classes must be rejected.");
            Assert(Normalize("NormalizeInstance",expected+"_x")==null,"Non-numeric WMI suffixes must be rejected.");
            Assert(!DisplayBrightnessControl.Available("nonexistent-test-monitor"),"A disconnected monitor must not inherit another panel's brightness.");
            foreach(DisplayInfo display in DisplayControl.Enumerate())Console.WriteLine("READ ONLY brightness support: "+display.Label+"="+DisplayBrightnessControl.Available(display.MonitorId));
            Console.WriteLine("PASS: "+checks+" multi-display configuration/identity checks; no display settings changed.");
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
