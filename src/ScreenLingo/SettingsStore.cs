using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ScreenLingo.Core;
namespace ScreenLingo;
public sealed class SettingsStore
{
 public string DirectoryPath { get; }
 public string FilePath=>Path.Combine(DirectoryPath,"settings.json");
 public string? LoadWarning { get; private set; }
 public SettingsStore(string? directory=null) { DirectoryPath=directory??Path.Combine(AppContext.BaseDirectory,"data");Directory.CreateDirectory(DirectoryPath); }
 public AppSettings Load()
 {
  if(!File.Exists(FilePath)) return new();
  try
  {
   var settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))??new();settings.Profiles??=[];settings.Profiles.RemoveAll(p=>p is null);
   if(settings.Profiles.Count==0)settings.Profiles.Add(new());settings.IdleSeconds=Math.Clamp(settings.IdleSeconds,30,600);settings.ReadingFontSize=Math.Clamp(settings.ReadingFontSize,12,40);
   settings.ReaderWidth=double.IsFinite(settings.ReaderWidth)?Math.Clamp(settings.ReaderWidth,0,4000):0;
   settings.ReaderHeight=double.IsFinite(settings.ReaderHeight)?Math.Clamp(settings.ReaderHeight,0,4000):0;return settings;
  }
  catch(Exception ex) when(ex is JsonException or IOException) { LoadWarning="配置文件无法读取，原文件已保留。请检查或重新保存配置。";return new(); }
 }
 public void Save(AppSettings settings)
 {
  var temporary=FilePath+".tmp";
  File.WriteAllText(temporary,JsonSerializer.Serialize(settings,new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
  File.Move(temporary,FilePath,true);
 }
}
public static class KeyVault
{
 [StructLayout(LayoutKind.Sequential)]struct Blob {public int Length;public IntPtr Data;}
 [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool CryptProtectData(ref Blob input,string description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("crypt32.dll",SetLastError=true)]static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
 [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr ptr);
 public static string Protect(string value)=>string.IsNullOrEmpty(value)?"":Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value),true));
 public static string Unprotect(string value)=>string.IsNullOrEmpty(value)?"":Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value),false));
 static byte[] Transform(byte[] bytes,bool encrypt)
 {
  var input=new Blob{Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;
  try
  {
   Marshal.Copy(bytes,0,input.Data,bytes.Length);
   bool ok=encrypt?CryptProtectData(ref input,"ScreenLingo API",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
   if(!ok)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法读取密钥，请在当前 Windows 用户下重新填写。");
   var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
  }
  finally{Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);Array.Clear(bytes);}
 }
}
