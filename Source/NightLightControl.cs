using System;using System.Collections.Generic;using System.IO;using Microsoft.Win32;
// Bounded CompactBinary v1 edits. Preserve untouched fields/schedules and reject
// unfamiliar envelopes rather than writing hard-coded registry byte offsets.
public static class NightLightControl{
 const string Root=@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.";
 static string Key(bool settings){string name=settings?"settings":"bluelightreductionstate";return Root+name+@"\windows.data.bluelightreduction."+name;}
 sealed class Field{public int Id,Type;public byte[] Raw,Value;}
 sealed class Record{public byte[] Original,Payload;public ulong Stamp;public List<Field> Fields;}
 static byte Next(byte[] b,ref int p){if(p>=b.Length)throw new InvalidDataException();return b[p++];}
 static ulong Var(byte[] b,ref int p){ulong n=0;for(int i=0;i<10;i++){byte v=Next(b,ref p);if(i==9&&v>1)throw new InvalidDataException();n|=(ulong)(v&127)<<(i*7);if(v<128)return n;}throw new InvalidDataException();}
 static byte[] Var(ulong n){List<byte>b=new List<byte>();do{byte v=(byte)(n&127);n>>=7;b.Add((byte)(v|(n>0?128:0)));}while(n>0);return b.ToArray();}
 static byte[] Slice(byte[] b,int start,int count){byte[] r=new byte[count];Array.Copy(b,start,r,0,count);return r;}
 static void Skip(byte[] b,ref int p,int type,int depth){if(depth>16)throw new InvalidDataException();switch(type){case 2:case 3:case 14:Next(b,ref p);break;case 4:case 5:case 6:case 15:case 16:case 17:Var(b,ref p);break;case 7:p=checked(p+4);break;case 8:p=checked(p+8);break;case 9:case 18:int n=checked((int)Var(b,ref p));p=checked(p+n*(type==18?2:1));break;case 10:Fields(b,ref p,depth+1);break;default:throw new InvalidDataException();}if(p>b.Length)throw new InvalidDataException();}
 static List<Field> Fields(byte[] b,ref int p,int depth){List<Field> fields=new List<Field>();int last=-1;while(true){int start=p;byte h=Next(b,ref p);if(h==0)return fields;int type=h&31,id=h>>5;if(type<2)throw new InvalidDataException();if(id==6)id=Next(b,ref p);else if(id==7){id=Next(b,ref p);id|=Next(b,ref p)<<8;}if(id<=last)throw new InvalidDataException();last=id;int value=p;Skip(b,ref p,type,depth);fields.Add(new Field{Id=id,Type=type,Raw=Slice(b,start,p-start),Value=Slice(b,value,p-value)});if(fields.Count>128)throw new InvalidDataException();}}
 static void Expect(byte[] b,ref int p,params byte[] bytes){foreach(byte x in bytes)if(Next(b,ref p)!=x)throw new InvalidDataException();}
 static Record Decode(byte[] bytes){if(bytes==null||bytes.Length>4096)throw new InvalidDataException();int p=0;Expect(bytes,ref p,67,66,1,0,10,2,1,0,42,6);ulong stamp=Var(bytes,ref p);Expect(bytes,ref p,42,43,14);int length=checked((int)Var(bytes,ref p));if(length<5||length>bytes.Length-p-3)throw new InvalidDataException();byte[] payload=Slice(bytes,p,length);p+=length;Expect(bytes,ref p,0,0,0);if(p!=bytes.Length)throw new InvalidDataException();int q=0;Expect(payload,ref q,67,66,1,0);List<Field> fields=Fields(payload,ref q,0);if(q!=payload.Length)throw new InvalidDataException();return new Record{Original=bytes,Payload=payload,Stamp=stamp,Fields=fields};}
 static Record Read(bool settings){using(RegistryKey key=Registry.CurrentUser.OpenSubKey(Key(settings),false)){if(key==null)throw new InvalidDataException();return Decode(key.GetValue("Data") as byte[]);}}
 static Field Find(Record r,int id,int type){Field f=r.Fields.Find(delegate(Field x){return x.Id==id;});if(f!=null&&f.Type!=type)throw new InvalidDataException();return f;}
 static void Set(Record r,int id,int type,byte[] value){Field old=Find(r,id,type);if(old!=null)r.Fields.Remove(old);if(value==null)return;List<byte> raw=new List<byte>();if(id<6)raw.Add((byte)(id*32+type));else{raw.Add((byte)(192+type));raw.Add((byte)id);}raw.AddRange(value);r.Fields.Add(new Field{Id=id,Type=type,Value=value,Raw=raw.ToArray()});r.Fields.Sort(delegate(Field a,Field b){return a.Id.CompareTo(b.Id);});}
 static byte[] Encode(Record r,ulong stamp){List<byte> payload=new List<byte>{67,66,1,0};foreach(Field f in r.Fields)payload.AddRange(f.Raw);payload.Add(0);List<byte> bytes=new List<byte>{67,66,1,0,10,2,1,0,42,6};bytes.AddRange(Var(stamp));bytes.AddRange(new byte[]{42,43,14});bytes.AddRange(Var((ulong)payload.Count));bytes.AddRange(payload);bytes.AddRange(new byte[]{0,0,0});return bytes.ToArray();}
 static void Write(bool settings,Record desired){Record current=Read(settings);if(!Equal(current.Original,desired.Original))throw new IOException("Concurrent night-light change.");ulong now=(ulong)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;byte[] bytes=Encode(desired,Math.Max(now,checked(current.Stamp+2)));using(RegistryKey key=Registry.CurrentUser.OpenSubKey(Key(settings),true)){if(key==null)throw new InvalidDataException();key.SetValue("Data",bytes,RegistryValueKind.Binary);}}
 static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
 public static bool Available(){try{Record state=Read(false);Field usable=Find(state,30,2);Read(true);return usable==null||usable.Value[0]!=0;}catch{return false;}}
 public static byte[][] Capture(){return new[]{Read(false).Original,Read(true).Original};}
 public static void Restore(byte[][] snapshot){for(int i=0;i<2;i++){Record desired=Decode(snapshot[i]),current=Read(i==1);desired.Original=current.Original;desired.Stamp=current.Stamp;Write(i==1,desired);}}
 public static StepResult Apply(bool enabled,int strength){const string step="NightLight";try{
  if(strength<0||strength>100||!Available())return new StepResult(step,"skipped","Ночной свет недоступен.");
  Record state=Read(false);bool active=Find(state,0,16)!=null;
  if(enabled){Record settings=Read(true);int temperature=6500-(int)Math.Round(5300.0*strength/100);Set(settings,40,15,Var((ulong)temperature*2));
   bool preview=Find(settings,70,2)!=null&&Find(settings,70,2).Value[0]!=0;
   if(active&&!preview)Set(settings,70,2,new byte[]{1});
   Write(true,settings);
   if(active&&!preview)try{System.Threading.Thread.Sleep(80);}finally{Record fresh=Read(true);Set(fresh,70,2,null);Write(true,fresh);}
  }
  // State is global in Windows; schedules are never changed by a mode.
  if(active!=enabled){state=Read(false);Set(state,0,16,enabled?Var(0):null);Set(state,10,16,Var(2));Set(state,20,6,Var((ulong)DateTime.UtcNow.ToFileTimeUtc()));Write(false,state);}
  if((Find(Read(false),0,16)!=null)!=enabled)return new StepResult(step,"skipped","Состояние ночного света не подтверждено.");
  return new StepResult(step,"ok",enabled?"Ночной свет включён, интенсивность "+strength+"%.":"Ночной свет выключен.");
 }catch{return new StepResult(step,"skipped","Управление ночным светом недоступно.");}}
}
