using Mono.Cecil;
using Mono.Cecil.Cil;
using var module=ModuleDefinition.ReadModule(args[0]);using var adapter=ModuleDefinition.ReadModule(args[1]);
var bridge=adapter.Types.Single(t=>t.Name=="HotFeed");var svc=module.Types.Single(t=>t.FullName=="AiHot.NewsService");
void Forward(MethodDefinition target,string name,int argCount){target.Body=new MethodBody(target);var il=target.Body.GetILProcessor();for(int n=0;n<argCount;n++)il.Emit(OpCodes.Ldarg,n);il.Emit(OpCodes.Call,module.ImportReference(bridge.Methods.Single(m=>m.Name==name)));il.Emit(OpCodes.Ret);}
Forward(svc.Methods.Single(m=>m.Name=="Refresh"),"Refresh",2);
Forward(svc.Methods.Single(m=>m.Name=="VisibleItems"),"Visible",2);
Forward(svc.Methods.Single(m=>m.Name=="LatestHundred"),"KeepOrder",2);
Forward(module.Types.Single(t=>t.Name=="NewsItem").Methods.Single(m=>m.Name=="get_Meta"),"Meta",1);
void Text(MethodDefinition m,string value){m.Body=new MethodBody(m);m.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr,value));m.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));}
Text(svc.Methods.Single(m=>m.Name=="Query"),"https://aihot.news/api/v1/items?mode=all&window=24h&by=published&limit=100");
Text(module.Types.Single(t=>t.Name=="MainWindow").Methods.Single(m=>m.Name=="get_RangeLabel"),"今日全部 AI 动态 · 最新在前");
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts){foreach(var t in ts){yield return t;foreach(var n in All(t.NestedTypes))yield return n;}}
foreach(var op in All(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Ldstr)){
 var value=(string)op.Operand;
 if(value.Contains("今天暂时没有精选资讯")||value.Contains("这个范围暂时没有精选资讯"))value="今天暂无 AI 动态";
 value=value.Replace("精选 ","热点 ").Replace("AIHOT 精选","AIHOT 热点").Replace("有新精选后自动更新","榜单更新后自动刷新").Replace(" · 最多 100 条","");
 op.Operand=value;
}
foreach(var op in All(module.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Ldstr)) { op.Operand=((string)op.Operand).Replace("热点 ","资讯 ").Replace(" · 精选", "").Replace("  /  精选", "").Replace("今日精选 TOP", "今日全部 AI 动态").Replace("热榜暂时没有热点事件", "今天暂无 AI 动态"); }
module.Write(args[2]);Console.WriteLine("PASS: today-all feed patched; Beijing date filtering and chronological ordering");
var deps=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(args[0],"deps.json")))!;
var target=deps["targets"]![deps["runtimeTarget"]!["name"]!.GetValue<string>()]!;
target["AIHOT.Desktop/1.2.0"]!["dependencies"]!["AIHOT.HotFeed"]="1.0.0";
target["AIHOT.HotFeed/1.0.0"]=System.Text.Json.Nodes.JsonNode.Parse("{\"runtime\":{\"AIHOT.HotFeed.dll\":{}}}");
deps["libraries"]!["AIHOT.HotFeed/1.0.0"]=System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"project\",\"serviceable\":false,\"sha512\":\"\"}");
File.WriteAllText(Path.ChangeExtension(args[2],"deps.json"),deps.ToJsonString(new(){WriteIndented=true}));
