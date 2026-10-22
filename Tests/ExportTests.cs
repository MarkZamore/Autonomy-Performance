using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

public static class ExportTests {
 static int checks;
 static string builder;
 public static int Main(string[] args) {
  builder=Path.GetFullPath(args[0]);
  AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) { return new AssemblyName(e.Name).Name=="AutonomyPerformance"?Assembly.LoadFrom(builder):null; };
  try { Run(Path.GetFullPath(args[1])); Console.WriteLine("PASS: "+checks+" assertions; no exported executable was run."); return 0; }
  catch(Exception e) {Console.Error.WriteLine(e); return 1;}
 }
 static void Assert(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
 static void Rejected(Action action,string label){bool rejected=false;try{action();}catch{rejected=true;}Assert(rejected,label);}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool FreeLibrary(IntPtr h);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindResource(IntPtr h,IntPtr name,IntPtr type);
 [DllImport("kernel32.dll")]static extern uint SizeofResource(IntPtr h,IntPtr res);
 [DllImport("kernel32.dll")]static extern IntPtr LoadResource(IntPtr h,IntPtr res);
 [DllImport("kernel32.dll")]static extern IntPtr LockResource(IntPtr h);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr BeginUpdateResource(string file,bool deleteExisting);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool UpdateResource(IntPtr h,IntPtr type,IntPtr name,ushort language,byte[] data,uint count);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,EntryPoint="UpdateResourceW",SetLastError=true)]static extern bool UpdateNamedResource(IntPtr h,IntPtr type,string name,ushort language,byte[] data,uint count);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool EndUpdateResource(IntPtr h,bool discard);
 delegate bool ResourceNameCallback(IntPtr h,IntPtr type,IntPtr name,IntPtr parameter);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool EnumResourceNames(IntPtr h,IntPtr type,ResourceNameCallback callback,IntPtr parameter);
 static byte[] Resource(IntPtr h,int type,int name){IntPtr res=FindResource(h,new IntPtr(name),new IntPtr(type));Assert(res!=IntPtr.Zero,"Missing resource "+type+"/"+name);byte[] b=new byte[SizeofResource(h,res)];Marshal.Copy(LockResource(LoadResource(h,res)),b,0,b.Length);return b;}
 static int SectionHeader(byte[] pe,string name){int header=BitConverter.ToInt32(pe,60),count=BitConverter.ToUInt16(pe,header+6),table=header+24+BitConverter.ToUInt16(pe,header+20);for(int i=0;i<count;i++){int at=table+40*i;if(Encoding.ASCII.GetString(pe,at,8).TrimEnd('\0')==name)return at;}throw new Exception("Missing PE section "+name);}
 static byte[] SectionBytes(byte[] pe,string name){int at=SectionHeader(pe,name);return pe.Skip(BitConverter.ToInt32(pe,at+20)).Take(BitConverter.ToInt32(pe,at+16)).ToArray();}
 static void Put(byte[] pe,int at,int value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,pe,at,4);}
 static byte[] WithRelocationSection(byte[] template){
  int pe=BitConverter.ToInt32(template,60),optional=pe+24,table=optional+BitConverter.ToUInt16(template,pe+20),header=table+80;
  Assert(BitConverter.ToUInt16(template,pe+6)==2&&header+40<=BitConverter.ToInt32(template,optional+60),"Fixture has room for relocation section");
  int resource=SectionHeader(template,".rsrc"),alignment=BitConverter.ToInt32(template,optional+32),rva=BitConverter.ToInt32(template,resource+12)+BitConverter.ToInt32(template,resource+8);rva=(rva+alignment-1)&~(alignment-1);
  byte[] result=new byte[template.Length+512];Buffer.BlockCopy(template,0,result,0,template.Length);result[pe+6]=3;
  Buffer.BlockCopy(Encoding.ASCII.GetBytes(".reloc"),0,result,header,6);Put(result,header+8,12);Put(result,header+12,rva);Put(result,header+16,512);Put(result,header+20,template.Length);Put(result,header+36,0x42000040);
  Put(result,optional+112+5*8,rva);Put(result,optional+112+5*8+4,12);Put(result,optional+56,rva+alignment);Put(result,optional+8,BitConverter.ToInt32(template,optional+8)+512);
  Put(result,template.Length,BitConverter.ToInt32(template,SectionHeader(template,".text")+12));Put(result,template.Length+4,12);return result;
 }
 static byte[] CustomFrameIcon(byte[] original,int count){
  int at=6+(BitConverter.ToUInt16(original,4)-1)*16,size=BitConverter.ToInt32(original,at+8),source=BitConverter.ToInt32(original,at+12),offset=6+16*count;byte[] icon=new byte[offset+count*size];
  icon[2]=1;icon[4]=(byte)count;for(int i=0;i<count;i++){Buffer.BlockCopy(original,at,icon,6+i*16,8);Put(icon,6+i*16+8,size);Put(icon,6+i*16+12,offset);Buffer.BlockCopy(original,source,icon,offset,size);offset+=size;}return icon;
 }
 static void VerifyPe(string path,byte[] icon,bool expectedVersion=true,bool managedImage=true){
  byte[] pe=File.ReadAllBytes(path);int header=BitConverter.ToInt32(pe,60);Assert(BitConverter.ToUInt16(pe,header+24+68)==2,"GUI subsystem");Assert(BitConverter.ToUInt16(pe,header+4)==0x8664,"x64 machine");
  byte[] template=EmbeddedAssets.Read("RunnerTemplate");Assert(SectionBytes(pe,".text").SequenceEqual(SectionBytes(template,".text")),"Executable code and CLR data remain byte-identical");
  int optional=header+24;Assert(BitConverter.ToUInt32(pe,optional+112+14*8)==BitConverter.ToUInt32(template,BitConverter.ToInt32(template,60)+24+112+14*8),"CLR header RVA unchanged");
  IntPtr h=LoadLibraryEx(path,IntPtr.Zero,0x22);Assert(h!=IntPtr.Zero,"PE load");
  try {
   bool hasVersion=false;ResourceNameCallback callback=delegate(IntPtr module,IntPtr type,IntPtr name,IntPtr parameter){hasVersion=true;return true;};
   EnumResourceNames(h,new IntPtr(16),callback,IntPtr.Zero);GC.KeepAlive(callback);
   Assert(hasVersion==expectedVersion,expectedVersion?"Export preserves VERSIONINFO resources":"Explicit legacy cleanup removes VERSIONINFO resources");
   string release=FileVersionInfo.GetVersionInfo(builder).FileVersion;int number;
   Assert(Int32.TryParse(release,out number)&&number>0&&release==number.ToString(System.Globalization.CultureInfo.InvariantCulture),"Builder release is a single number");
   FileVersionInfo info=FileVersionInfo.GetVersionInfo(path);
   Assert(expectedVersion?info.FileVersion==release&&info.ProductVersion==release:String.IsNullOrEmpty(info.FileVersion)&&String.IsNullOrEmpty(info.ProductVersion),expectedVersion?"Export file/product versions match builder release":"Explicit legacy cleanup preserves its version-removal behavior");
   if(managedImage)Assert(AssemblyName.GetAssemblyName(path).Version==new Version(1,0,number,0),"Managed assembly version remains technical 1.0.N.0");
   string manifest=Encoding.UTF8.GetString(Resource(h,24,1));Assert(manifest.Contains("asInvoker")&&!manifest.Contains("requireAdministrator"),"asInvoker manifest");
   byte[] group=Resource(h,14,1);int count=BitConverter.ToUInt16(icon,4);Assert(BitConverter.ToUInt16(group,4)==count,"Icon frame count");
   for(int i=0;i<count;i++){
    int original=6+i*16, entry=6+i*14;Assert(icon.Skip(original).Take(8).SequenceEqual(group.Skip(entry).Take(8)),"Icon metadata");
    int size=(int)BitConverter.ToUInt32(icon,original+8),offset=(int)BitConverter.ToUInt32(icon,original+12),id=BitConverter.ToUInt16(group,entry+12);
    Assert(BitConverter.ToUInt32(group,entry+8)==size,"Group size");Assert(Resource(h,3,id).SequenceEqual(icon.Skip(offset).Take(size)),"RT_ICON bytes");
   }
  }finally{FreeLibrary(h);}
 }
 [MethodImpl(MethodImplOptions.NoInlining)] static void Run(string output){
  Directory.CreateDirectory(output);string exe=Path.Combine(output,"Roundtrip.exe");byte[] defaultIcon=EmbeddedAssets.Read("Icon0");
  int cases=0;
  for(int e=0;e<=2;e++)for(int p=0;p<=3;p++)for(int f=0;f<=3;f++)for(int b=0;b<=1;b++)for(int d=0;d<=2;d++){
   ModeConfiguration c=new ModeConfiguration{EnergySaver=e,PowerMode=p,FanProfile=f,Brightness=b==1?100:-1,DarkTheme=d};
   if(!c.HasActions)continue;
   ExecutableExporter.Save(exe,c,defaultIcon);ModeConfiguration r=PeConfiguration.Read(exe);
   Assert(r.Version==2&&r.EnergySaver==e&&r.PowerMode==p&&r.FanProfile==f&&r.Brightness==(b==1?100:-1)&&r.DarkTheme==d,"Configuration roundtrip");cases++;
  }
  Assert(cases==287,"All non-empty independent action combinations");
  for(int playback=0;playback<3;playback++)for(int audio=0;audio<3;audio++){
   ModeConfiguration c=new ModeConfiguration{WallpaperPlayback=playback,WallpaperAudio=audio};
   Assert(c.HasActions==(playback!=0||audio!=0),"Wallpaper audio and playback actions are independent");if(!c.HasActions)continue;
   ExecutableExporter.Save(exe,c,defaultIcon);ModeConfiguration r=PeConfiguration.Read(exe);
   Assert(r.WallpaperPlayback==playback&&r.WallpaperAudio==audio,"Wallpaper audio/playback configuration roundtrip");
  }
  foreach(int brightness in new[]{-1,0,1,50,99,100}){
   ModeConfiguration c=new ModeConfiguration{EnergySaver=1,Brightness=brightness};ExecutableExporter.Save(exe,c,defaultIcon);
   Assert(PeConfiguration.Read(exe).Brightness==brightness,"Brightness percentage and unchanged sentinel roundtrip");
  }
  ModeConfiguration extra=new ModeConfiguration{DarkTheme=2,SleepTimeout=2,ScreenTimeout=2,Hdr=2,PresenceSleep=2,PresenceDim=2,ContentBrightness=2,AdaptiveBrightness=2,ScreenSaver=2,UsbPower=2,DisplayId="TEST\\DISPLAY\\EXPORT",RefreshRate=60};
  ExecutableExporter.Save(exe,extra,defaultIcon);ModeConfiguration additional=PeConfiguration.Read(exe);
  Assert(additional.DarkTheme==2&&additional.SleepTimeout==2&&additional.ScreenTimeout==2&&additional.Hdr==2&&additional.PresenceSleep==2&&additional.PresenceDim==2&&additional.ContentBrightness==2&&additional.AdaptiveBrightness==2&&additional.ScreenSaver==2&&additional.UsbPower==2&&additional.DisplayId==extra.DisplayId&&additional.RefreshRate==60,"All additional actions and display identity roundtrip without execution");
  ModeConfiguration valid=new ModeConfiguration{EnergySaver=1};
  for(int i=0;i<5;i++){byte[] ico=EmbeddedAssets.Read("Icon"+i);IconFile.Validate(ico);ExecutableExporter.Save(exe,valid,ico);VerifyPe(exe,ico);}
  byte[] configuration=PortableResources.ReadConfiguration(File.ReadAllBytes(exe));Assert(configuration.Length>0,"Managed configuration resource read");
  foreach(int frames in new int[]{1,24}){
   byte[] customIcon=CustomFrameIcon(defaultIcon,frames);IconFile.Validate(customIcon);string customOutput=Path.Combine(output,"Custom-"+frames+".exe");ExecutableExporter.Save(customOutput,valid,customIcon);VerifyPe(customOutput,customIcon);
  }
  byte[] relocationTemplate=WithRelocationSection(EmbeddedAssets.Read("RunnerTemplate")),largeIcon=CustomFrameIcon(defaultIcon,24);
  // This synthetic relocation fixture is a data-only PE, not a loadable managed image.
  // Its CLR bytes are checked against the runner by VerifyPe; regular exports also validate AssemblyName.
  byte[] relocated=PortableResources.CreateModeExecutable(relocationTemplate,configuration,largeIcon);string relocatedPath=Path.Combine(output,"Relocated.exe");File.WriteAllBytes(relocatedPath,relocated);VerifyPe(relocatedPath,largeIcon,true,false);
  int oldRelocation=SectionHeader(relocationTemplate,".reloc"),newRelocation=SectionHeader(relocated,".reloc"),relocatedOptional=BitConverter.ToInt32(relocated,60)+24;
  Assert(BitConverter.ToInt32(relocated,newRelocation+12)>BitConverter.ToInt32(relocationTemplate,oldRelocation+12),"Expanded resources move relocation RVA");
  Assert(BitConverter.ToInt32(relocated,relocatedOptional+112+5*8)==BitConverter.ToInt32(relocated,newRelocation+12),"Relocation directory follows relocated section");
  Assert(SectionBytes(relocated,".reloc").SequenceEqual(SectionBytes(relocationTemplate,".reloc")),"Relocation blocks preserved byte-for-byte");
  Assert(AssemblyName.GetAssemblyName(exe).Name=="RunnerTemplate","Exported managed executable metadata remains readable");
  byte[] broken=EmbeddedAssets.Read("RunnerTemplate");broken[0]=0;Rejected(delegate{PortableResources.CreateModeExecutable(broken,configuration,defaultIcon);},"Corrupt PE template rejected before writing");
  byte[] signed=EmbeddedAssets.Read("RunnerTemplate");int signedOptional=BitConverter.ToInt32(signed,60)+24;Put(signed,signedOptional+112+4*8,512);Put(signed,signedOptional+112+4*8+4,8);Rejected(delegate{PortableResources.CreateModeExecutable(signed,configuration,defaultIcon);},"Signed or non-controlled PE template rejected");
  Assert(!typeof(ExecutableExporter).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Any(m=>m.Name.Contains("UpdateResource")),"Exporter has no Windows resource-update calls");
  ExecutableExporter.Save(exe,valid,EmbeddedAssets.Read("Icon4"));
  string versionless=Path.Combine(output,"LegacyVersionless.exe");File.Copy(exe,versionless,true);
  Assert(ExecutableExporter.RemoveVersionMetadata(versionless),"Explicit legacy metadata-removal helper remains compatible");
  Assert(!ExecutableExporter.RemoveVersionMetadata(versionless),"Repeated explicit legacy cleanup is a safe no-op");
  byte[] template=EmbeddedAssets.Read("RunnerTemplate");string legacy=Path.Combine(output,"Versioned.exe");File.WriteAllBytes(legacy,template);
  IntPtr templateModule=LoadLibraryEx(legacy,IntPtr.Zero,0x22);Assert(templateModule!=IntPtr.Zero,"Template PE load");byte[] version;
  try{version=Resource(templateModule,16,1);}finally{FreeLibrary(templateModule);}
  File.Copy(exe,legacy,true);IntPtr update=BeginUpdateResource(legacy,false);Assert(update!=IntPtr.Zero,"Version fixture update");
  try{
   Assert(UpdateResource(update,new IntPtr(16),new IntPtr(1),0,version,(uint)version.Length),"Neutral version fixture");
   Assert(UpdateResource(update,new IntPtr(16),new IntPtr(1),1033,version,(uint)version.Length),"English version fixture");
   Assert(UpdateNamedResource(update,new IntPtr(16),"SECOND_VERSION",1049,version,(uint)version.Length),"Named Russian version fixture");
   IntPtr committing=update;update=IntPtr.Zero;Assert(EndUpdateResource(committing,false),"Version fixture commit");
  }finally{if(update!=IntPtr.Zero)EndUpdateResource(update,true);}
  Assert(!String.IsNullOrEmpty(FileVersionInfo.GetVersionInfo(legacy).FileVersion),"Fixture has Explorer version");
  ModeConfiguration legacyBefore=PeConfiguration.Read(legacy);Assert(ExecutableExporter.RemoveVersionMetadata(legacy),"Existing script metadata removed");
  ModeConfiguration legacyAfter=PeConfiguration.Read(legacy);Assert(legacyBefore.EnergySaver==legacyAfter.EnergySaver&&legacyAfter.EnergySaver==1&&legacyAfter.HasActions,"Existing script configuration preserved");VerifyPe(legacy,EmbeddedAssets.Read("Icon4"),false);
  byte[] cleaned=File.ReadAllBytes(legacy);Assert(!ExecutableExporter.RemoveVersionMetadata(legacy)&&cleaned.SequenceEqual(File.ReadAllBytes(legacy)),"Repeated removal preserves every byte");
  Rejected(delegate{ExecutableExporter.RemoveVersionMetadata(builder);},"Constructor metadata modification rejected");
  IconSelection selection=new IconSelection();Assert(selection.GetBytes().SequenceEqual(defaultIcon),"Default program icon");
  selection.SelectBuiltIn(4);selection.Saved();Assert(selection.BuiltInIndex==4,"Built-in selection persists");
  string custom=Path.Combine(output,"Custom.ico");File.WriteAllBytes(custom,EmbeddedAssets.Read("Icon2"));selection.SelectCustom(custom);Assert(selection.GetBytes().SequenceEqual(File.ReadAllBytes(custom)),"Custom selected");File.Delete(custom);Assert(selection.GetBytes().SequenceEqual(defaultIcon)&&selection.CustomPath==null,"Deleted custom icon fallback");
  File.WriteAllBytes(custom,EmbeddedAssets.Read("Icon1"));selection.SelectCustom(custom);File.WriteAllBytes(custom,new byte[]{0,1,2});Assert(selection.GetBytes().SequenceEqual(defaultIcon)&&selection.CustomPath==null,"Invalid changed custom fallback");
  Rejected(delegate{selection.SelectCustom(custom);},"Invalid ICO rejected");File.WriteAllBytes(custom,EmbeddedAssets.Read("Icon1"));selection.SelectCustom(custom);selection.Saved();Assert(selection.CustomPath==null&&selection.BuiltInIndex==0,"Successful custom save reset");File.Delete(custom);
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration(),defaultIcon);},"Empty actions rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{EnergySaver=3},defaultIcon);},"Invalid config rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{Brightness=-2},defaultIcon);},"Brightness below unchanged sentinel rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{Brightness=101},defaultIcon);},"Brightness above 100 percent rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{Hdr=1},defaultIcon);},"HDR action without target display rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{DarkTheme=3},defaultIcon);},"Invalid additional toggle rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,new ModeConfiguration{WallpaperAudio=3},defaultIcon);},"Invalid wallpaper audio toggle rejected");
  Rejected(delegate{ExecutableExporter.Save(exe,valid,new byte[]{1,2,3});},"Invalid export icon rejected");
  Rejected(delegate{ExecutableExporter.Save(builder,valid,defaultIcon);},"Constructor overwrite rejected");
  string locked=Path.Combine(output,"Locked.exe");File.WriteAllText(locked,"must remain");
  using(FileStream block=new FileStream(locked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){Rejected(delegate{ExecutableExporter.Save(locked,valid,defaultIcon);},"Locked destination rejected");}
  Assert(File.ReadAllText(locked)=="must remain","Failed overwrite preserves original");File.Delete(locked);
  string folder=Path.Combine(output,"Directory.exe");Directory.CreateDirectory(folder);Rejected(delegate{ExecutableExporter.Save(folder,valid,defaultIcon);},"Directory destination rejected");Directory.Delete(folder);
  Assert(Directory.GetFiles(output,".ap-*.tmp").Length==0,"No temporary exports after success/failure");
  Assert(ModeConfiguration.PowerGuid(2)==Guid.Empty,"Balanced GUID");
  Console.WriteLine("Validated "+cases+" configuration combinations, five full multi-frame ICO resources, release version preservation, explicit legacy cleanup, overwrite and failure cleanup.");
 }
}
