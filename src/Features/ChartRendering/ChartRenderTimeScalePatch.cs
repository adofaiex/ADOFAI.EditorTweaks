// 开发: ModsTag
// ↑ 屎山代码的问题找他:) 和酥酥无关

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ADOFAI.EditorTweaks.src.Features.ChartRendering
{
    /// <summary>
    /// 遍历大部分的<see cref="Assembly"/> 并且替换方法，包括:
    /// <code>
    /// - <see cref="Time.unscaledDeltaTime"/>    -> <see cref="Time.deltaTime"/><br/>
    /// - <see cref="Time.unscaledTime"/>         -> <see cref="Time.time"/><br/>
    /// - <see cref="Time.unscaledTimeAsDouble"/> -> <see cref="Time.timeAsDouble"/><br/>
    /// </code>
    /// </summary>
    public static class ChartRenderTimeScalePatch
    {
        /// <summary>
        /// 补丁包体 (伪结构体)
        /// </summary>
        private sealed class PatchPackage
        {
            internal PatchPackage(string typ)
            {
                type = typ;
                // 预先生成4个元素 不大不小刚刚好
                methods = new(4);
            }
            /// <summary>
            /// <see cref="Type.FullName"/>返回的值
            /// </summary>
            internal readonly string type;
            /// <summary>
            /// 一个列表，包含来自<see cref="RuntimeMethodInfo"/>的<see cref="MemberInfo.Name"/>返回的值
            /// </summary>
            internal readonly List<string> methods;
        }
        /// <summary>
        /// 创建新的补丁
        /// </summary>
        public static void Create()
        {
            DateTime start = DateTime.Now;
            // 计数器 用于记录补丁数量
            int counter = 0;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            // 快速通道 直接使用缓存数据
            // 但是为了"快速" 要求极为严苛
            Main.Log("try to fast patch");
            // 1. 检查缓存数据是否存在以及长度是否相等
            if (aAll != null && aAll.Length == assemblies.Length)
            {
                // 就一个调试日志别管
                Main.Log("fast patch flag1");
                // 2. 检查缓存数据是否匹配
                bool eq = true;
                for (int i = 0; i < aAll.Length && eq; i++)
                {
                    eq &= aAll[i].FullName == assemblies[i].FullName;
                }
                if (eq)
                {
                    // 就两个调试日志别管
                    Main.Log("succ, goto fast patch");
                    // 进入快速路径 只补丁缓存内的数据
                    foreach (MethodInfo method in aCached)
                    {
                        harmony.Patch(method, transpiler: patchMethod);
                        counter++;
                    }
                    Main.Log("Patch unscaledTime, Count: " + counter);
                    return;
                }
            }
            // 慢速通道 重新创建缓存数据
            Main.Log("fail, goto slow patch");
            aAll = assemblies;
            aCached.Clear();
            foreach (Assembly assembly in assemblies)
            {
                // 黑名单列表 专门排除不关事的Assembly
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

                // 如果这玩意路径都没了 那还说啥了给你了
                if (string.IsNullOrEmpty(assembly.Location) || !System.IO.File.Exists(assembly.Location))
                {
                    // 如果有必要可以下一个Harmony补丁
                    // 但是我偷懒 直接去他妈的
                    continue;
                }

                // 派发任务 处理Assembly内的所有类型
                // 欸你说这玩意为啥就不能是循环队列呢 真奇怪
                Queue<PatchPackage> packages = new();
                if (name == "Assembly-CSharp") // 由于这玩意太tm大了 所以开个小差 用一个更优的方式处理
                    Task_ACS(packages, assembly);
                else 
                    Task(packages, assembly);

                // 如果这玩意啥都没 那就别占用公共资源了 滚吧
                if (packages.Count == 0)
                {
                    Main.Log("  Patch Skip... (0)");
                    continue; 
                }

                // 非经典遍历所有包体
                while (packages.TryDequeue(out PatchPackage pp))
                {
                    // 还原成Type
                    Type type = assembly.GetType(pp.type);
                    total += pp.methods.Count;
                    foreach (string method in pp.methods)
                    {
                        // 查询Method
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

                            // 排除一下不是在这个Type的
                            // 说真的谁想到type.GetMethods里会参杂着不是这个type的MethodInfo呢
                            if (mi.DeclaringType != type)
                            { continue; }
                            // 正常检查然后补丁
                            // 没存详细参数 所以全补丁了
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
                                catch (Exception ex)
                                { 
                                    // 对于异常直接Log算了
                                    Main.Mod?.Logger?.LogException("  Patch: ", ex);
                                }
                            }
                        }
                    }
                }
                Main.Log("  Patched Count: " + patched + " / " + total);
            }

            DateTime end = DateTime.Now;
            // 都说了调试日志别管 (红温)
            // byd写补丁脸红的和苹果一样
            Main.Log("Patch unscaledTime, Count: " + counter);
            Main.Log("Cached MethodInfo, Count: " + aCached.Count);
            Main.Log("Patch Successful, Time: " + ((end - start).Ticks) / 10000 + " ms");
        }
        public static void Destroy()
        {
            // 全给你删了全给你删了我全给你删了
            harmony.UnpatchAll(harmony.Id);
        }

        /// <summary>
        /// 第二大屎山 这个还没那么离谱
        /// </summary>
        /// <param name="result"><see cref="this"/>, 一个静态伪实例</param>
        /// <param name="assembly">需要搞的Assembly</param>
        private static void Task(Queue<PatchPackage> result, Assembly assembly)
        {
            Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location);

            // 非常正常的遍历所有类型
            foreach (Mono.Cecil.TypeDefinition type in assemblyDefinition.MainModule.Types)
            {
                PatchPackage package = new(type.FullName);
                // 非常正常的遍历所有方法
                foreach (Mono.Cecil.MethodDefinition method in type.Methods)
                {
                    // 非常正常的排除一部分不可能在里面使用的方法
                    if (!method.HasBody || method.Name == "Equals" || method.Name == "Finalize" || method.Name == "GetHashCode" || method.Name == "ToString" || method.Name == "CompareTo")
                    { continue; }
                    // 非常正常的查IL
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                    {
                        // 如果看不懂下面的代码 我这里解释一下
                        // 首先 对于静态方法 调用通通是用OpCodes.Call
                        // 而实例方法不需要知道
                        // 而所有的property的get和set其实都是Method
                        //   只不过前面会加一句"get_"或是"set_"而已
                        // 而我们目标是把所有的unscaled读取出来
                        // 所以就是查询OpCode是不是OpCodes.Call
                        // 然后查询当前类型是不是来自"UnityEngine.Time"
                        // 最后查询方法是不是"get_unscaledDeltaTime"或"get_unscaledTime"或"get_unscaledTimeAsDouble"其中之一
                        // 是就加进去package.methods里 然后退出
                        // 不是就滚
                        if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call)
                        {
                            string typ = (instruction.Operand! as Mono.Cecil.MethodReference).DeclaringType.FullName;
                            string mtd = (instruction.Operand! as Mono.Cecil.MethodReference).Name;
                            if (typ == "UnityEngine.Time" && (mtd == "get_unscaledDeltaTime" || mtd == "get_unscaledTime" || mtd == "get_unscaledTimeAsDouble"))
                            {
                                package.methods.Add(method.Name);
                                break;
                            }
                        }
                    }
                }
                // 非常正常的判断是否有方法需要打补丁
                // 有就加进去队列里
                if (package.methods.Count > 0)
                {
                    result.Enqueue(package);
                }
            }
            return;
        }
        /// <summary>
        /// 第一大屎山 纯硬编码的产物:)
        /// </summary>
        /// <param name="result"><see cref="this"/>, 一个静态伪实例</param>
        /// <param name="assembly">需要搞的Assembly</param>
        private static void Task_ACS(Queue<PatchPackage> result, Assembly assembly)
        {
            Mono.Cecil.AssemblyDefinition assemblyDefinition = Mono.Cecil.AssemblyDefinition.ReadAssembly(assembly.Location);

            // 非常正常的遍历所有类型
            foreach (Mono.Cecil.TypeDefinition type in assemblyDefinition.MainModule.Types)
            {
                Mono.Cecil.TypeReference baseType = type.BaseType;
                // 这里多了个检查类是不是由UnityEngine.MonoBehaviour派生出来的
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
                // 非常正常的遍历所有方法
                foreach (Mono.Cecil.MethodDefinition method in type.Methods)
                {
                    // 非常正常的排除一部分不可能在里面使用的方法
                    if (!method.HasBody || method.Name == "Equals" || method.Name == "Finalize" || method.Name == "GetHashCode" || method.Name == "ToString" || method.Name == "CompareTo")
                    { continue; }
                    // 非常雷霆的硬编码查询:)
                    // 这都是为了优化啊(被打)
                    // 其实Mono.Cecil的IL遍历速度不差了 但是加了这点东西性能高很多 就留下来吧
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
                    // 非常正常的查IL
                    foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                    {
                        // 如果看不懂就看前面Task的
                        if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Call)
                        {
                            string typ = (instruction.Operand! as Mono.Cecil.MethodReference).DeclaringType.FullName;
                            string mtd = (instruction.Operand! as Mono.Cecil.MethodReference).Name;
                            if (typ == "UnityEngine.Time" && (mtd == "get_unscaledDeltaTime" || mtd == "get_unscaledTime" || mtd == "get_unscaledTimeAsDouble"))
                            {
                                package.methods.Add(method.Name);
                                break;
                            }
                        }
                    }
                }
                // 非常正常的判断是否有方法需要打补丁
                // 有就加进去队列里
                if (package.methods.Count > 0)
                {
                    result.Enqueue(package);
                }
            }
            return;
        }

        /// <summary>
        /// dddd
        /// </summary>
        private static Harmony harmony = new Harmony("ADOFAI::EditorTweaks::src::Features::ChartRendering::ChartRenderTimeScale");

        /// <summary>
        /// 当前补丁的缓存
        /// </summary>
        private static List<MethodInfo> aCached = new();
        /// <summary>
        /// 当前缓存的Assembly 用于校验
        /// </summary>
        private static Assembly[]? aAll;

        // 这里纯大粪
        private static readonly HarmonyMethod patchMethod = new(typeof(ChartRenderTimeScalePatch).GetMethod(nameof(Transpiler), AccessTools.all));
        private static readonly MethodInfo unscaledDeltaTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledDeltaTime)).GetGetMethod();
        private static readonly MethodInfo deltaTimeMethod = typeof(Time).GetProperty(nameof(Time.deltaTime)).GetGetMethod();
        private static readonly MethodInfo unscaledTimeMethod = typeof(Time).GetProperty(nameof(Time.unscaledTime)).GetGetMethod();
        private static readonly MethodInfo timeMethod = typeof(Time).GetProperty(nameof(Time.time)).GetGetMethod();
        private static readonly MethodInfo unscaledTimeAsDoubleMethod = typeof(Time).GetProperty(nameof(Time.unscaledTimeAsDouble)).GetGetMethod();
        private static readonly MethodInfo timeAsDoubleMethod = typeof(Time).GetProperty(nameof(Time.timeAsDouble)).GetGetMethod();

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            // 这里也大粪 但是还是得解释一下
            // 简单来说就是和上面的一样 也是遍历IL
            // 但是不一样的是 这里遍历了之后会直接替换了 而不是仅查找
            // 性能最差的也是这里 性能差到什么程度? 大部分开销都来自这里:)
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
