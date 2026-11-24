using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace PCMonitor;
static class Program {
 [STAThread] static void Main(){ ApplicationConfiguration.Initialize(); Application.Run(new MainForm()); }
}
record Metric(string id,string name,string hardware,string type,string unit,double value);
sealed class MainForm:Form {
 readonly Label status=new(){AutoSize=true,MaximumSize=new Size(540,0)};
 readonly Label pinLabel=new(){AutoSize=true,Font=new Font("Segoe UI",26,FontStyle.Bold)};
 readonly CancellationTokenSource stop=new();
 readonly Computer computer=new(){IsCpuEnabled=true,IsGpuEnabled=true,IsMemoryEnabled=true,IsMotherboardEnabled=true,IsControllerEnabled=true};
 readonly ConcurrentDictionary<string,byte> tokens=new();
 readonly object pairLock=new();
 string pin=""; DateTime expires; DateTime nextAttempt=DateTime.MinValue; int failures;
 string snapshot="{\"ready\":false,\"metrics\":[]}";
 HttpListener? http; UdpClient? udp; Task? sampler;
 public MainForm(){
 Text="PC Monitor — 电脑端"; Width=610; Height=350; StartPosition=FormStartPosition.CenterScreen;
 var panel=new FlowLayoutPanel(){Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(20),AutoScroll=true};
 panel.Controls.Add(new Label(){Text="PC Monitor · 局域网硬件监控",AutoSize=true,Font=new Font("Segoe UI",18,FontStyle.Bold)});
 panel.Controls.Add(new Label(){Text="手机连接同一局域网，打开 APP 搜索本电脑。",AutoSize=true});
 panel.Controls.Add(pinLabel); panel.Controls.Add(new Label(){Text="配对码有效期 5 分钟；配对后可自动重连（本次电脑端运行期间）。",AutoSize=true});
 var renew=new Button(){Text="生成新配对码 / 断开全部手机",AutoSize=true}; renew.Click+=(_,_)=>NewPin(true); panel.Controls.Add(renew);
 var fw=new Button(){Text="允许专用局域网防火墙访问",AutoSize=true}; fw.Click+=(_,_)=>Firewall(); panel.Controls.Add(fw); panel.Controls.Add(status); Controls.Add(panel);
 NewPin(false); Shown+=(_,_)=>Start(); FormClosing+=(_,_)=>{stop.Cancel();http?.Close();udp?.Dispose();};
 }
 void NewPin(bool revoke){lock(pairLock){pin=RandomNumberGenerator.GetInt32(0,1000000).ToString("D6");expires=DateTime.UtcNow.AddMinutes(5); failures=0;nextAttempt=DateTime.MinValue;if(revoke)tokens.Clear();pinLabel.Text="配对码："+pin;}}
 void SetStatus(string s){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)(()=>status.Text=s));}catch{}}
 static string Addresses()=>string.Join(" / ",NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Where(a=>a.Address.AddressFamily==AddressFamily.InterNetwork).Select(a=>a.Address.ToString()));
 void Start(){try{http=new HttpListener();http.Prefixes.Add("http://+:8765/");http.Start();udp=new UdpClient(8766);_=Task.Run(Serve);_=Task.Run(Discover);sampler=Task.Run(Sample);SetStatus("地址："+Addresses()+"  | TCP 8765 / UDP 8766\n仅在可信专用局域网使用。若搜索失败可手动输入 IP。");}catch(Exception e){http?.Close();udp?.Dispose();SetStatus("启动失败："+e.Message);}}
 void Firewall(){try{foreach(var p in new[]{("TCP",8765),("UDP",8766)}){var name=$"PCMonitor-{p.Item1}-{p.Item2}";RunNetsh($"advfirewall firewall delete rule name=\"{name}\"");if(RunNetsh($"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={p.Item1} localport={p.Item2} remoteip=LocalSubnet profile=private")!=0)throw new Exception("无法添加 "+name);}MessageBox.Show("已允许专用网络中的本地子网访问。请确认当前网络为专用网络。");}catch(Exception e){MessageBox.Show(e.Message);}}
 static int RunNetsh(string args){using var p=Process.Start(new ProcessStartInfo("netsh",args){UseShellExecute=false,CreateNoWindow=true});p!.WaitForExit();return p.ExitCode;}
 async Task Discover(){while(!stop.IsCancellationRequested)try{var r=await udp!.ReceiveAsync(stop.Token);if(Encoding.UTF8.GetString(r.Buffer)!="PCMONITOR_DISCOVER_V1")continue;var b=JsonSerializer.SerializeToUtf8Bytes(new{service="PCMONITOR_V1",name=Environment.MachineName,port=8765});await udp.SendAsync(b,b.Length,r.RemoteEndPoint);}catch(OperationCanceledException){break;}catch(ObjectDisposedException){break;}catch{await Task.Delay(500);}}
 async Task Serve(){while(!stop.IsCancellationRequested)try{var c=await http!.GetContextAsync();_=Task.Run(()=>Handle(c));}catch{break;}}
 async Task Handle(HttpListenerContext c){try{var r=c.Request;var path=r.Url?.AbsolutePath; if(path=="/pair"&&r.HttpMethod=="POST"){
 string? code=r.Headers["X-Pair-Code"]; string? token=null;int error=403;lock(pairLock){var now=DateTime.UtcNow;if(now<nextAttempt)error=429;else if(now<=expires&&code==pin&&failures<10){token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));tokens.TryAdd(token,0);failures=0;}else{failures++;nextAttempt=now.AddSeconds(2);}}
 if(token==null){await Reply(c,error,"{\"error\":\"pair_failed\"}");return;}await Reply(c,200,JsonSerializer.Serialize(new{token}));return;}
 if(path=="/metrics"&&r.HttpMethod=="GET"){var auth=r.Headers["Authorization"];if(auth==null||!auth.StartsWith("Bearer ")||!tokens.ContainsKey(auth[7..])){await Reply(c,401,"{}");return;}await Reply(c,200,Volatile.Read(ref snapshot));return;}await Reply(c,404,"{}");}catch{}finally{try{c.Response.Close();}catch{}}}
 static async Task Reply(HttpListenerContext c,int status,string json){var b=Encoding.UTF8.GetBytes(json);c.Response.StatusCode=status;c.Response.ContentType="application/json; charset=utf-8";c.Response.Headers["Cache-Control"]="no-store";c.Response.ContentLength64=b.Length;await c.Response.OutputStream.WriteAsync(b);}
 void Sample(){try{computer.Open();while(!stop.IsCancellationRequested){var metrics=new List<Metric>();var errors=new List<string>();foreach(var h in computer.Hardware)Visit(h,metrics,errors);Volatile.Write(ref snapshot,JsonSerializer.Serialize(new{ready=true,time=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),metrics,errors}));if(stop.Token.WaitHandle.WaitOne(1000))break;}}catch(Exception e){Volatile.Write(ref snapshot,JsonSerializer.Serialize(new{ready=false,metrics=Array.Empty<Metric>(),error=e.Message}));}finally{try{computer.Close();}catch{}}}
 static void Visit(IHardware h,List<Metric> list,List<string> errors){try{h.Update();foreach(var s in h.Sensors){if(s.Value is not float v||!float.IsFinite(v))continue;var unit=s.SensorType.ToString() switch{"Temperature"=>"°C","Load"=>"%","Fan"=>"RPM","Voltage"=>"V","Data"=>"GB","SmallData"=>"MB","Power"=>"W","Clock"=>"MHz",_=>null};if(unit!=null)list.Add(new(s.Identifier.ToString(),s.Name,h.Name,s.SensorType.ToString(),unit,v));}}catch(Exception e){errors.Add(h.Name+": "+e.Message);}foreach(var child in h.SubHardware)Visit(child,list,errors);}
}
