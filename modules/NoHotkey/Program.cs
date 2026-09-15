using Mono.Cecil;
using Mono.Cecil.Cil;
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types) { foreach(var t in types) { yield return t; foreach(var n in All(t.NestedTypes)) yield return n; } }
using var module=ModuleDefinition.ReadModule(args[0]);
var native=module.Types.Single(t=>t.FullName=="AiHot.Native");
var registration=native.Methods.Single(m=>m.Name=="RegisterHotKey");
var disabled=new MethodDefinition("NoGlobalHotkey",MethodAttributes.Assembly|MethodAttributes.Static,module.TypeSystem.Boolean);
foreach(var p in registration.Parameters) disabled.Parameters.Add(new ParameterDefinition(p.Name,p.Attributes,p.ParameterType));
disabled.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
disabled.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));native.Methods.Add(disabled);
int calls=0,hints=0;
foreach(var type in All(module.Types)) foreach(var method in type.Methods.Where(m=>m.HasBody)) foreach(var i in method.Body.Instructions) {
 if(i.Operand is MethodReference reference&&reference.Name=="RegisterHotKey") {i.Operand=disabled;calls++;}
 if(i.OpCode==OpCodes.Ldstr&&i.Operand is string s&&s.Contains("Ctrl+Alt+A")) { i.Operand=s.Replace(" | Ctrl+Alt+A 显示/隐藏", "").Replace("Ctrl+Alt+A", ""); hints++; }
}
if(calls!=1) throw new Exception($"Expected one hotkey registration, found {calls}");
module.Write(args[1]);
using var check=ModuleDefinition.ReadModule(args[1]);
if(All(check.Types).SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference r&&r.Name=="RegisterHotKey"))throw new Exception("Registration still present");
Console.WriteLine($"PASS: {calls} global hotkey registration disabled; {hints} hints removed");
