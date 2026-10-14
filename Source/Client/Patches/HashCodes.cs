using HarmonyLib;
using Multiplayer.Client.Util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Patches
{
    [HarmonyPatch(typeof(WindManager), nameof(WindManager.BaseWindSpeedAt))]
    static class BaseWindSpeetGetHashCodeChange
    {
        static readonly MethodInfo OriginalGetHash = AccessTools.Method(typeof(object), nameof(GetHashCode));

        static readonly MethodInfo ChangedGetHash = AccessTools.Method(typeof(BaseWindSpeetGetHashCodeChange), nameof(GetMapHash));

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var ci in insts)
            {
                if (ci.Calls(OriginalGetHash))
                    yield return new CodeInstruction(OpCodes.Call, ChangedGetHash);
                else
                    yield return ci;
            }
        }

        static int GetMapHash(Map map)
        {
            if (Multiplayer.Client == null)
                return map.GetHashCode();

            return map.Tile;
        }
    }

    [HarmonyPatch(typeof(GlowGrid), MethodType.Constructor, typeof(Map))]
    static class GlowGridCtorPatch
    {
        private static AccessTools.FieldRef<GlowGrid, HashSet<CompGlower>> litGlowers =
            AccessTools.FieldRefAccess<GlowGrid, HashSet<CompGlower>>(nameof(GlowGrid.litGlowers));

        static void Postfix(GlowGrid __instance)
        {
            litGlowers(__instance) = new HashSet<CompGlower>(new CompGlowerEquality());
        }

        class CompGlowerEquality : IEqualityComparer<CompGlower>
        {
            public bool Equals(CompGlower x, CompGlower y) => x == y;
            public int GetHashCode(CompGlower obj) => obj.parent.thingIDNumber;
        }
    }

    [HarmonyPatch]
    static class PatchTargetInfoHashCodes
    {
        static MethodInfo Combine = AccessTools.Method(typeof(Gen), nameof(Gen.HashCombine)).MakeGenericMethod(typeof(Map));

        static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(GlobalTargetInfo), nameof(GlobalTargetInfo.GetHashCode));
            yield return AccessTools.Method(typeof(TargetInfo), nameof(TargetInfo.GetHashCode));
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand as MethodInfo == Combine)
                    inst.operand = AccessTools.Method(typeof(PatchTargetInfoHashCodes), nameof(CombineHashes));

                yield return inst;
            }
        }

        static int CombineHashes(int seed, Map map) => Gen.HashCombineInt(seed, map?.uniqueID ?? -1);
    }

    // todo does this cause issues?
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.GetHashCode))]
    static class TradeableHashCode
    {
        static bool Prefix() => false;

        static void Postfix(Tradeable __instance, ref int __result)
        {
            __result = RuntimeHelpers.GetHashCode(__instance);
        }
    }

    // System.HashCode.Combine is seeded randomly per process. Game code uses Combine with int arguments for rand seeds
    // (Map.NextGenSeed, LayoutWorker.FillAllRooms, Gravship.ConstantRandSeed, Building_Trap.KnowsOfTrap) and hash codes,
    // which have to be the same for all players. The calls are replaced in the calling game methods instead of patching
    // HashCode.Combine itself: once the JIT optimizes other BCL methods it inlines the original Combine into them
    // (e.g. ValueTuple.GetHashCode used by Dictionary), so a patched Combine made the same key hash differently over
    // time and broke dictionaries (lost load IDs on reload). Calls are replaced always, not only in multiplayer, so the
    // hash codes don't change when a game gets hosted.
    [HarmonyPatch]
    static class HashCodeCombineCallers
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var module = typeof(Game).Assembly.ManifestModule;
            var combineSpecs = new HashSet<int>();

            // MethodSpec table entries for HashCode.Combine<int, ...>
            for (int i = 1; ; i++)
            {
                int token = 0x2B000000 | i;
                MethodBase spec;
                try { spec = module.ResolveMethod(token); }
                catch (ArgumentOutOfRangeException) { break; }
                catch (Exception) { continue; } // needs a generic context, can't be HashCode.Combine<int, ...>

                if (spec is MethodInfo { IsGenericMethod: true } m && m.DeclaringType == typeof(HashCode) &&
                    m.Name == nameof(HashCode.Combine) && m.GetGenericArguments().All(t => t == typeof(int)))
                    combineSpecs.Add(token);
            }

            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Game).Assembly))
            foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;

                byte[] il;
                try { il = method.GetMethodBody()?.GetILAsByteArray(); }
                catch (Exception) { continue; }
                if (il == null) continue;

                // call <token>; a match at a non-instruction offset only patches a method without changing it
                for (int i = 0; i + 4 < il.Length; i++)
                    if (il[i] == 0x28 && combineSpecs.Contains(BitConverter.ToInt32(il, i + 1)))
                    {
                        yield return method;
                        break;
                    }
            }
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> insts)
        {
            foreach (var inst in insts)
            {
                if (inst.operand is MethodInfo { IsGenericMethod: true } m && m.DeclaringType == typeof(HashCode) &&
                    m.Name == nameof(HashCode.Combine) && m.GetGenericArguments().All(t => t == typeof(int)))
                    inst.operand = AccessTools.Method(typeof(HashCodeCombineCallers), "Combine" + m.GetGenericArguments().Length);

                yield return inst;
            }
        }

        static int Combine2(int v1, int v2) => Gen.HashCombineInt(v1, v2);
        static int Combine3(int v1, int v2, int v3) => DeterministicHash.HashCombineInt(v1, v2, v3);
        static int Combine4(int v1, int v2, int v3, int v4) => Gen.HashCombineInt(v1, v2, v3, v4);
        static int Combine5(int v1, int v2, int v3, int v4, int v5) =>
            DeterministicHash.HashCombineInt(v1, v2, v3, v4, v5);
        static int Combine6(int v1, int v2, int v3, int v4, int v5, int v6) =>
            DeterministicHash.HashCombineInt(v1, v2, v3, v4, v5, v6);
        static int Combine7(int v1, int v2, int v3, int v4, int v5, int v6, int v7) =>
            DeterministicHash.HashCombineInt(v1, v2, v3, v4, v5, v6, v7);
        static int Combine8(int v1, int v2, int v3, int v4, int v5, int v6, int v7, int v8) =>
            DeterministicHash.HashCombineInt(v1, v2, v3, v4, v5, v6, v7, v8);
    }
}
