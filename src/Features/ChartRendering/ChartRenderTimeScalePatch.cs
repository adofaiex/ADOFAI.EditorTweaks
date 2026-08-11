using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using System.Xml.Serialization;
using UnityEngine;

namespace ADOFAI.EditorTweaks.src.Features.ChartRendering
{
    public static class ChartRenderTimeScalePatch
    {
        private sealed class PatchPackage
        {
            internal PatchPackage(string typ)
            {
                type = typ;
                methods = new();
            }
            internal readonly string type;
            internal readonly List<string> methods;
        }
        public static void Init()
        {
            DateTime start = DateTime.Now;
            int counter = 0;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Main.Log("try to fast patch");
            if (aAll != null && aAll.Length == assemblies.Length)
            {
                Main.Log("fast patch flag: a");
                bool eq = true;
                for (int i = 0; i < aAll.Length && eq; i++)
                {
                    eq &= aAll[i] == assemblies[i];
                }
                if (eq)
                {
                    Main.Log("succ, goto fast patch");
                    // fast path
                    foreach (MethodInfo method in aCached)
                    {
                        harmony.Patch(method, transpiler: patchMethod);
                        counter++;
                    }
                    Main.Log("Patch unscaledTime, Count: " + counter);
                    return;
                }
            }

            Main.Log("fail, goto slow patch");
            aAll = assemblies;
            aCached.Clear();
            foreach (Assembly assembly in assemblies)
            {
                // black list
                string name = assembly.GetName().Name;
                if (
                    // .net
                    name.StartsWith("System") ||
                    name.StartsWith("Mono") ||
                    name.StartsWith("I18N") ||
                    name.StartsWith("Microsoft") ||
                    name == "netstandard" ||
                    name == "mscorlib" ||
                    // unity
                    name.StartsWith("UnityEngine") ||
                    name.StartsWith("Unity") ||
                    name.StartsWith("UniTask") ||
                    //name.StartsWith("DOTween") ||
                    // umm
                    name == "UnityModManager" ||
                    name == "0Harmony" ||
                    name == "dnlib" ||
                    name.StartsWith("Harmony") ||
                    // 7bug
                    name == "Facepunch.Steamworks.Win64" ||
                    //name == "RDTools" ||
                    name == "SkyHook.Unity" ||
                    name == "Newtonsoft.Json" ||
                    name == "XTUtilities" ||
                    name == "SharpSevenZip" ||
                    name == "Interop.SpeechLib" ||
                    // other
                    name.StartsWith("ModsTagLib") || 
                    name.StartsWith("Cover") ||
                    name == "ADOToolsLib" || 
                    name == "ADOFAI.EditorTweaks"
                ) { continue; }

                int total = 0;
                int patched = 0;
                Main.Log("Patch Assembly Name: " + name);
                Main.Log("Patch Assembly Location: " + assembly.Location);

                List<PatchPackage> packages = new();
                if (name == "Assembly-CSharp") Task_ACS(packages, assembly); else Task(packages, assembly);

                if (packages.Count == 0)
                {
                    Main.Log("  Patch Skip... (0)");
                    continue; 
                }

                foreach (PatchPackage pp in packages)
                {
                    Type type = assembly.GetType(pp.type);
                    total += pp.methods.Count;
                    foreach (string method in pp.methods)
                    {
                        MethodInfo[] methods = type.GetMethods(AccessTools.all);
                        foreach (MethodInfo mi in methods)
                        {
                            // // fuck it
                            // if (
                            //     mi.HasMethodBody() || 
                            //     (mi.Attributes & (MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)) != 0 || 
                            //     (mi.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0 ||
                            //     (mi.GetMethodImplementationFlags() & MethodImplAttributes.Native) != 0 ||
                            //     (mi.GetMethodImplementationFlags() & MethodImplAttributes.OPTIL) != 0 ||
                            //     (mi.GetMethodImplementationFlags() & MethodImplAttributes.Runtime) != 0 ||
                            //     (mi.GetMethodImplementationFlags() & MethodImplAttributes.ManagedMask) != 0
                            // )
                            // { continue; }
                            if (mi.Name == method)
                            {
                                try
                                {
                                    harmony.Patch(mi, transpiler: patchMethod);
                                    aCached.Add(mi);
                                    Main.Log("  Patched: " + method);
                                    counter++;
                                    patched++;
                                }
                                catch
                                { 
                                }
                            }
                        }
                    }
                }
                Main.Log("  Patched Count: " + patched + " / " + total);
            }

            DateTime end = DateTime.Now;
            Main.Log("Patch unscaledTime, Count: " + counter);
            Main.Log("Cached MethodInfo, Count: " + aCached.Count);
            Main.Log("Patch Successful, Time: " + ((end - start).Ticks) / 10000 + " ms");
        }
        public static void Uninit()
        {
            harmony.UnpatchAll(harmony.Id);
        }

        private static void Task(List<PatchPackage> result, Assembly assembly)
        {
            if (assembly.Location is null || !System.IO.File.Exists(assembly.Location))
            {
                return;
            }
            Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location);

            foreach (Mono.Cecil.TypeDefinition type in assemblyDefinition.MainModule.Types)
            {
                PatchPackage package = new(type.FullName);
                foreach (Mono.Cecil.MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody || method.Name == "Equals" || method.Name == "Finalize" || method.Name == "GetHashCode" || method.Name == "ToString" || method.Name == "CompareTo")
                    { continue; }
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call)
                        {
                            string typ = (instruction.Operand! as Mono.Cecil.MethodReference).DeclaringType.FullName;
                            string mtd = (instruction.Operand! as Mono.Cecil.MethodReference).Name;
                            if (typ == "UnityEngine.Time" && (mtd == "get_unscaledDeltaTime" || mtd == "get_unscaledTime" || mtd == "get_unscaledTimeAsDouble"))
                            {
                                package.methods.Add(method.Name);
                            }
                        }
                    }
                }
                if (package.methods.Count > 0)
                {
                    result.Add(package);
                }
            }
            return;
        }
        private static void Task_ACS(List<PatchPackage> result, Assembly assembly)
        {
            if (assembly.Location is null || !System.IO.File.Exists(assembly.Location))
            {
                return;
            }
            Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location);

            foreach (Mono.Cecil.TypeDefinition type in assemblyDefinition.MainModule.Types)
            {
                Mono.Cecil.TypeReference baseType = type.BaseType;
                bool isMonoBehaviour = false;

                while (baseType != null && !isMonoBehaviour)
                {
                    if (baseType.FullName == "UnityEngine.MonoBehaviour")
                    {
                        isMonoBehaviour = true;
                        break;
                    }

                    Mono.Cecil.TypeDefinition? baseTypeDef = baseType.Resolve();
                    if (baseTypeDef == null)
                    {
                        break;
                    }

                    baseType = baseTypeDef.BaseType;
                }
                PatchPackage package = new(type.FullName);
                foreach (Mono.Cecil.MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody || method.Name == "Equals" || method.Name == "Finalize" || method.Name == "GetHashCode" || method.Name == "ToString" || method.Name == "CompareTo")
                    { continue; }
                    if (isMonoBehaviour)
                    {
                        if (
                            method.Name == nameof(MonoBehaviour.IsInvoking) ||
                            method.Name == nameof(MonoBehaviour.CancelInvoke) ||
                            method.Name == nameof(MonoBehaviour.Invoke) ||
                            method.Name == nameof(MonoBehaviour.InvokeRepeating) ||
                            method.Name == nameof(MonoBehaviour.StartCoroutine) ||
                            method.Name == nameof(MonoBehaviour.StartCoroutine_Auto) ||
                            method.Name == nameof(MonoBehaviour.StopCoroutine) ||
                            method.Name == nameof(MonoBehaviour.StopAllCoroutines) ||
                            method.Name == "get_useGUILayout" ||
                            method.Name == "set_useGUILayout" ||
                            method.Name == "get_didStart" ||
                            method.Name == "get_didAwake" ||
                            method.Name == "get_enabled" ||
                            method.Name == "set_enabled" ||
                            method.Name == "get_isActiveAndEnabled" ||
                            method.Name == "get_transform" ||
                            method.Name == "get_transformHandle" ||
                            method.Name == "get_gameObject" ||
                            method.Name == nameof(MonoBehaviour.GetComponent) ||
                            method.Name == "MonoBehaviour.GetComponentFastPath" ||
                            method.Name == nameof(MonoBehaviour.TryGetComponent) ||
                            method.Name == nameof(MonoBehaviour.GetComponentInChildren) ||
                            method.Name == nameof(MonoBehaviour.GetComponentsInChildren) ||
                            method.Name == nameof(MonoBehaviour.GetComponentInParent) ||
                            method.Name == nameof(MonoBehaviour.GetComponentsInParent) ||
                            method.Name == nameof(MonoBehaviour.GetComponents) ||
                            method.Name == "get_tag" ||
                            method.Name == "set_tag" ||
                            method.Name == "get_name" ||
                            method.Name == "set_name" ||
                            method.Name == "get_hideFlags" ||
                            method.Name == "set_hideFlags" ||
                            method.Name == nameof(MonoBehaviour.GetComponentIndex) ||
                            method.Name == nameof(MonoBehaviour.CompareTag) ||
                            method.Name == nameof(MonoBehaviour.SendMessage) ||
                            method.Name == nameof(MonoBehaviour.BroadcastMessage) ||
                            method.Name == nameof(MonoBehaviour.GetInstanceID) ||
                            method.Name == nameof(MonoBehaviour.GetEntityId) ||
                            method.Name == nameof(MonoBehaviour.SendMessageUpwards)
                        ) { continue; }
                    }
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call)
                        {
                            string typ = (instruction.Operand! as Mono.Cecil.MethodReference).DeclaringType.FullName;
                            string mtd = (instruction.Operand! as Mono.Cecil.MethodReference).Name;
                            if (typ == "UnityEngine.Time" && (mtd == "get_unscaledDeltaTime" || mtd == "get_unscaledTime" || mtd == "get_unscaledTimeAsDouble"))
                            {
                                package.methods.Add(method.Name);
                            }
                        }
                    }
                }
                if (package.methods.Count > 0)
                {
                    result.Add(package);
                }
            }
            return;
        }

        private static Harmony harmony = new Harmony("ADOFAI::EditorTweaks::src::Features::ChartRendering::ChartRenderTimeScale");

        private static List<MethodInfo> aCached = new();
        private static Assembly[]? aAll;

        private static readonly HarmonyMethod patchMethod = new(typeof(ChartRenderTimeScalePatch).GetMethod(nameof(Transpiler), AccessTools.all));
        private static readonly MethodInfo unscaledDeltaTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledDeltaTime)).GetGetMethod();
        private static readonly MethodInfo deltaTimeMethod = typeof(Time).GetProperty(nameof(Time.deltaTime)).GetGetMethod();
        private static readonly MethodInfo unscaledTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledTime)).GetGetMethod();
        private static readonly MethodInfo timeMethod = typeof(Time).GetProperty(nameof(Time.time)).GetGetMethod();
        private static readonly MethodInfo unscaledTimeAsDoubleMethod = typeof(Time).GetProperty(nameof(Time.unscaledTimeAsDouble)).GetGetMethod();
        private static readonly MethodInfo timeAsDoubleMethod = typeof(Time).GetProperty(nameof(Time.timeAsDouble)).GetGetMethod();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == unscaledDeltaTimeMethod)
                {
                    ci.operand = deltaTimeMethod;
                }
                else if (ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == unscaledTimeMethod)
                {
                    ci.operand = timeMethod;
                }
                else if (ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == unscaledTimeAsDoubleMethod)
                {
                    ci.operand = timeAsDoubleMethod;
                }
                yield return ci;
            }
            yield break;
        }
    }
}
