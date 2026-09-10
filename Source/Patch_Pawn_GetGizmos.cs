using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using CombatExtended; // VerbPropertiesCE

namespace TSA.CE_Aimbot
{
    /// <summary>
    /// Show the aimbot toggle gizmo on any drafted player pawn that currently has a primary
    /// which looks ranged. The detection is intentionally permissive so players reliably see the toggle.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result)
                yield return g;

            if (__instance == null) yield break;
            if (!__instance.Drafted) yield break;
            if (__instance.Faction != Faction.OfPlayer) yield break;

            var primary = __instance.equipment?.Primary;
            if (primary == null) yield break;

            if (!LooksRanged(primary))
                yield break;

            var giz = Gizmo_PawnToggle.Build(__instance);
            if (giz != null) yield return giz;
        }

        /// <summary>
        /// Broad "is this ranged?" detection:
        /// - CE verb: VerbPropertiesCE -> true
        /// - Vanilla flag: def.IsRangedWeapon -> true (if available)
        /// - Equipped primary verb range > 1 -> true
        /// - Any def verb with range > 1 and not melee -> true
        /// If none of the above, assume not ranged.
        /// </summary>
        private static bool LooksRanged(ThingWithComps weapon)
        {
            try
            {
                var eq = weapon.TryGetComp<CompEquippable>();
                var v = eq?.PrimaryVerb;

                // CE ranged verb?
                if (v?.verbProps is VerbPropertiesCE) return true;

                // Vanilla ranged hint via primary verb
                if (v?.verbProps != null && !v.verbProps.IsMeleeAttack && v.verbProps.range > 1f) return true;

                // ThingDef flag (vanilla)
                try
                {
                    if (weapon.def != null && weapon.def.IsRangedWeapon) return true;
                }
                catch { /* some forks might not expose IsRangedWeapon cleanly */ }

                // Any def verb that looks ranged
                if (weapon.def?.Verbs != null)
                {
                    foreach (var vp in weapon.def.Verbs)
                    {
                        if (vp != null && !vp.IsMeleeAttack && vp.range > 1f) return true;
                    }
                }
            }
            catch
            {
                // On any odd failure, lean toward not showing (silent)
            }
            return false;
        }
    }
}
