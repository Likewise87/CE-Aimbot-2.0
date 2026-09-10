using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace TSA.CE_Aimbot
{
    /// <summary>
    /// Applies settings-driven aim/fire at the start of a CE shot/burst.
    /// - POSTFIX so our choice wins.
    /// - Skips re-applying if same rule entry & same target to avoid stance "dip".
    /// - Optional debug logging: chosen rule/entry and CE modes before/after.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Verb_ShootCE_TryStartCastOn
    {
        private static Type ceVerbShootType;

        private class LastApplied
        {
            public int targetId;
            public IntVec3 targetCell;
            public int ruleIndex;
            public int entryIndex;
            public AimOption aim;
            public FireOption fire;
        }

        private static readonly Dictionary<int, LastApplied> cache = new Dictionary<int, LastApplied>();

        static Patch_Verb_ShootCE_TryStartCastOn()
        {
            ceVerbShootType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => GetTypesSafe(a))
                .FirstOrDefault(t => t.FullName == "CombatExtended.Verb_ShootCE");
        }

        public static bool Prepare() => ceVerbShootType != null;

        public static MethodInfo TargetMethod()
        {
            var methods = ceVerbShootType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "TryStartCastOn").ToList();
            return methods.OrderByDescending(m => m.GetParameters().Length).FirstOrDefault();
        }

        public static void Postfix(object __instance, LocalTargetInfo castTarg, bool __result)
        {
            try
            {
                var verb = __instance as Verb;
                var pawn = verb?.CasterPawn;
                var weapon = verb?.EquipmentSource;
                if (pawn == null || weapon == null) return;
                if (pawn.Faction != Faction.OfPlayer) return;
                if (!pawn.Drafted) return;
                if (!(PawnAutoAimStore.Instance?.IsEnabledFor(pawn) ?? false)) return;
                if (castTarg == null || !castTarg.IsValid) return;

                float dist = pawn.Position.DistanceTo(castTarg.Cell);

                if (!RuleEngine.TryGetMatch(pawn, weapon, dist, out int ruleIndex, out int entryIndex, out var aimOpt, out var fireOpt))
                    return;

                int targId = castTarg.Thing?.thingIDNumber ?? 0;
                IntVec3 targCell = castTarg.Cell;

                var id = pawn.thingIDNumber;
                if (!cache.TryGetValue(id, out var last))
                {
                    last = new LastApplied();
                    cache[id] = last;
                }

                bool sameTarget = (last.targetId == targId && last.targetCell == targCell);
                bool sameEntry = (last.ruleIndex == ruleIndex && last.entryIndex == entryIndex);
                bool sameModes = (last.aim == aimOpt && last.fire == fireOpt);

                bool debug = Mod_Aimbot.Settings?.DebugLogging == true;
                if (debug)
                {
                    CE_Reflection.TryGetCurrent(weapon, out AimMode aimCur, out FireMode fireCur);

                    var def = weapon.def;
                    var defName = def?.defName ?? "(null)";
                    var tags = def?.weaponTags != null ? string.Join(", ", def.weaponTags) : "(no tags)";

                    var rules = Mod_Aimbot.Settings?.Rules;
                    string ruleName = (rules != null && ruleIndex >= 0 && ruleIndex < rules.Count)
                                      ? rules[ruleIndex].Name
                                      : "(no match)";
                    string ruleDesc = (rules != null && ruleIndex >= 0 && ruleIndex < rules.Count)
                                      ? $"{rules[ruleIndex].Kind}/{rules[ruleIndex].Mode} \"{rules[ruleIndex].Pattern}\""
                                      : "";

                    string entryDesc = "(n/a)";
                    if (entryIndex >= 0 && rules != null && ruleIndex >= 0 && ruleIndex < rules.Count
                        && rules[ruleIndex].Entries != null && entryIndex < rules[ruleIndex].Entries.Count)
                    {
                        entryDesc = $"MinRange={rules[ruleIndex].Entries[entryIndex].MinRange:0.##}";
                    }

                    Log.Message(
                        $"[TSA.CE_Aimbot] Shoot Eval → Weapon:{defName} | Tags:[{tags}] | Dist:{dist:0.##} | " +
                        $"Rule[{ruleIndex}]: {ruleName} {ruleDesc} | Entry[{entryIndex}]: {entryDesc} | " +
                        $"Aim want:{CE_Reflection.ToCeAim(aimOpt)} (cur:{aimCur}) | Fire want:{CE_Reflection.ToCeFire(fireOpt)} (cur:{fireCur})"
                    );
                }

                if (!(sameTarget && sameEntry && sameModes))
                {
                    CE_Reflection.Apply(weapon, aimOpt, fireOpt);
                    last.targetId = targId;
                    last.targetCell = targCell;
                    last.ruleIndex = ruleIndex;
                    last.entryIndex = entryIndex;
                    last.aim = aimOpt;
                    last.fire = fireOpt;

                    if (debug)
                    {
                        CE_Reflection.TryGetCurrent(weapon, out AimMode aimAfter, out FireMode fireAfter);
                        Log.Message($"[TSA.CE_Aimbot] Apply → Aim now:{aimAfter} | Fire now:{fireAfter}");
                    }
                }
                else if (debug)
                {
                    Log.Message("[TSA.CE_Aimbot] Skipped apply (same target/entry/modes).");
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[TSA.CE_Aimbot] TryStartCastOn Postfix failed: {e}");
            }
        }

        private static Type[] GetTypesSafe(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch { return Array.Empty<Type>(); }
        }
    }
}
