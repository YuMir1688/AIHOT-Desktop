using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AiHot;
namespace YuMir.Cards;
public static class ReaderSections {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 static object Get(object o,string name)=>o.GetType().GetField(name,Flags)!.GetValue(o)!;
 sealed class State {
  public Grid Pages=new(); public Dictionary<int,UIElement> Panels=new();
  public DateTime Stamp,Day; public int Request,Selected=-1;
 }
 static readonly ConditionalWeakTable<Border,State> States=new();
 public static void Switch(object closure) {
  int section=(int)Get(closure,"selectedSection");
  var shared=closure.GetType().GetFields(Flags).Select(f=>f.GetValue(closure)).First(o=>o!=null&&o.GetType().GetField("reportHost",Flags)!=null)!;
  var host=(Border)Get(shared,"reportHost"); var columns=(Grid)Get(shared,"columns");
  var buttons=(List<Button>)Get(shared,"sectionButtons"); var state=States.GetOrCreateValue(host);
  var stamp=File.GetLastWriteTimeUtc(Path.Combine(Storage.Root,"history.json")); var day=NewsService.BeijingDate(DateTimeOffset.UtcNow);
  if(state.Selected==section&&state.Stamp==stamp&&state.Day==day)return;
  for(int i=0;i<buttons.Count;i++)buttons[i].Background=ReportSharing.Brush(i==section?"#23493F":"#1A2633");
  int request=++state.Request;
  if(section==0){state.Selected=0;columns.Visibility=Visibility.Visible;host.Visibility=Visibility.Collapsed;return;}
  host.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{
   if(request!=state.Request)return;
   if(state.Stamp!=stamp||state.Day!=day){state.Panels.Clear();state.Pages.Children.Clear();state.Stamp=stamp;state.Day=day;}
   if(!state.Panels.TryGetValue(section,out var panel)){
    var type=typeof(NewsItem).Assembly.GetType("AiHot.ReportPanel")!;
    panel=(UIElement)Activator.CreateInstance(type,Flags,null,new object[]{section},null)!;
    state.Panels.Add(section,panel);state.Pages.Children.Add(panel);
   }
   foreach(var pair in state.Panels)pair.Value.Visibility=pair.Key==section?Visibility.Visible:Visibility.Hidden;
   host.Child=state.Pages;columns.Visibility=Visibility.Collapsed;host.Visibility=Visibility.Visible;state.Selected=section;
  }));
 }
}
