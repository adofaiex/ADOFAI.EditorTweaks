using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Reflection;
using UnityEngine;
using System;

namespace ADOFAI.EditorTweaks.src.Features.ChartRendering
{
    public static class ChartRenderTimeScalePatch
    {
        public static void Init()
        {
            int counter = 0;
            HarmonyMethod hm = new(typeof(ChartRenderTimeScalePatch).GetMethod(nameof(Transpiler), AccessTools.all));
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (aAll != null && aAll.Length == assemblies.Length)
            {
                bool eq = true;
                for (int i = 0; i < aAll.Length && eq; i++)
                {
                    eq &= aAll[i] == assemblies[i];
                }
                if (eq)
                {
                    Main.Log("Goto fast patch");
                    // fast path
                    foreach (MethodInfo method in aCached)
                    {
                        harmony.Patch(method, transpiler: hm);
                        counter++;
                    }
                    Main.Log("Patch unscaledTime, Count: " + counter);
                    return;
                }
            }

            Main.Log("Goto slow patch");
            aAll = assemblies;
            aCached.Clear();
            foreach (Assembly assembly in assemblies)
            {
                // black list
                string name = assembly.GetName().Name;
                if (
                    name.StartsWith("UniTask") || 
                    name.StartsWith("SkyHook.Unity") || 
                    name.StartsWith("Rewired") || 
                    name.StartsWith("Newtonsoft.Json") || 
                    name.StartsWith("R3") || 
                    name.StartsWith("netstandard") || 
                    name.StartsWith("mscorlib") || 
                    name.StartsWith("UnityEngine") || 
                    name.StartsWith("System") || 
                    name.StartsWith("Mono") || 
                    name.StartsWith("Microsoft") || 
                    name.StartsWith("I18N")
                ) { continue; }
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.FullName.StartsWith("UnityEngine") || type.FullName.StartsWith("System"))
                    {
                        continue;
                    }
                    foreach (MethodInfo method in type.GetMethods(AccessTools.all))
                    {
                        gbState = true;
                        if ((method.Attributes & (MethodAttributes.PinvokeImpl | MethodAttributes.Abstract)) != 0 || (method.GetMethodImplementationFlags() & MethodImplAttributes.InternalCall) != 0)
                        {
                            continue;
                        }
                        try
                        {
                            // 想不到吧 这玩意回莫名报错 所以必须套上try
                            MethodInfo temp = harmony.Patch(method, transpiler: hm);
                            if (gbState)
                            {
                                harmony.Unpatch(method, HarmonyPatchType.Transpiler, harmony.Id); // maybe faster?
                                // harmony.Unpatch(method, hm);
                            }
                            else
                            {
                                counter++;
                                aCached.Add(method);
                            }
                        }
                        catch
                        { /* skip */ }
                    }
                }
            }
            Main.Log("Patch unscaledTime, Count: " + counter);
            Main.Log("Cached MethodInfo, Count: " + aCached.Count);
        }
        public static void Uninit()
        {
            harmony.UnpatchAll(harmony.Id);
        }

        private static Harmony harmony = new Harmony("ADOFAI::EditorTweaks::src::Features::ChartRendering::ChartRenderTimeScale");
        private static bool gbState;

        private static List<MethodInfo> aCached = new();
        private static Assembly[]? aAll;

        private static readonly MethodInfo unscaledDeltaTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledDeltaTime)).GetGetMethod();
        private static readonly MethodInfo deltaTimeMethod = typeof(Time).GetProperty(nameof(Time.deltaTime)).GetGetMethod();
        private static readonly MethodInfo unscaledTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledTime)).GetGetMethod();
        private static readonly MethodInfo timeMethod = typeof(Time).GetProperty(nameof(Time.time)).GetGetMethod();
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            foreach (CodeInstruction ci in instructions)
            {
                if (ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == unscaledDeltaTimeMethod)
                {
                    ci.operand = deltaTimeMethod;
                    gbState = false;
                }
                if (ci.opcode == OpCodes.Call && (ci.operand as MethodInfo) == unscaledTimeMethod)
                {
                    ci.operand = timeMethod;
                    gbState = false;
                }
                yield return ci;
            }
            yield break;
        }
    }
}
