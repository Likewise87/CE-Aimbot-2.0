using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using CombatExtended; // VerbPropertiesCE

namespace TSA.CE_Aimbot
{
    internal static class RuleEngine
    {
        private static List<CompiledRule> compiled = new List<CompiledRule>();

        private sealed class CompiledRule
        {
            public bool Enabled;
            public string PatternLC;                 // lowercased (empty string = Default)
            public TargetKind Kind;
            public MatchMode Mode;
            public IntRange SkillRange;
            public List<RangeEntry> Entries;         // descending MinRange (compiled order)

            public bool IsDefault => string.IsNullOrEmpty(PatternLC);

            public bool Matches(Pawn pawn, Thing weapon)
            {
                if (!Enabled || weapon == null || pawn == null) return false;

                int skillLevel = pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0;

                // Max 20 means "20 or higher" for modded skill caps.
                bool passesMax = SkillRange.max >= 20 || skillLevel <= SkillRange.max;
                if (skillLevel < SkillRange.min || !passesMax) return false;

                var def = weapon.def;
                if (def == null) return false;

                // Default (empty pattern) is only used as Evaluate fallback; callers skip IsDefault customs.
                if (IsDefault) return true;

                return WeaponDefMatches(def, Kind, Mode, PatternLC);
            }

            public (AimOption aim, FireOption fire, int entryIndex)? SelectForDistance(float dist, VerbPropertiesCE? vp)
            {
                if (Entries == null || Entries.Count == 0) return null;

                // FIRST entry where dist >= MinRange (band To / inner boundary); otherwise LAST (closest band, To=0).
                // Entries are compile-sorted descending by MinRange.
                int chosenIdx = -1;
                for (int e = 0; e < Entries.Count; e++)
                {
                    if (dist >= Entries[e].MinRange)
                    {
                        chosenIdx = e;
                        break;
                    }
                }
                if (chosenIdx < 0) chosenIdx = Entries.Count - 1;

                var chosen = Entries[chosenIdx];
                int burstCount = vp?.burstShotCount ?? 1;
                var fire = ClampFire(chosen.Fire, burstCount);
                return (chosen.Aim, fire, chosenIdx);
            }

            private static FireOption ClampFire(FireOption f, int burstCount)
            {
                bool allowBurst = burstCount > 1;
                bool allowAuto = burstCount >= 3;

                if (f == FireOption.FullAuto && !allowAuto)
                    return allowBurst ? FireOption.Burst : FireOption.Single;

                if (f == FireOption.Burst && !allowBurst)
                    return FireOption.Single;

                return f;
            }
        }

        public static void RebuildCompiled(List<Rule> rules)
        {
            compiled.Clear();
            if (rules == null || rules.Count == 0) return;

            foreach (var r in rules)
            {
                if (r == null) continue;
                compiled.Add(new CompiledRule
                {
                    Enabled = r.Enabled,
                    Kind = r.Kind,
                    Mode = r.Mode,
                    SkillRange = r.SkillRange,
                    PatternLC = (r.Pattern ?? string.Empty).ToLowerInvariant(),
                    Entries = r.Entries?.ToList() ?? new List<RangeEntry>()
                });
            }
        }

        /// <summary>
        /// Same Kind/Mode/Pattern match used at shot time (skill ignored). Empty pattern matches nothing
        /// for preview (custom empty patterns are skipped at runtime; Default is not previewed here).
        /// </summary>
        public static bool WeaponDefMatches(ThingDef def, TargetKind kind, MatchMode mode, string patternOrLc)
        {
            if (def == null) return false;
            string patternLc = (patternOrLc ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrEmpty(patternLc)) return false;

            if (kind == TargetKind.DefName)
            {
                var hay = (def.defName ?? string.Empty).ToLowerInvariant();
                if (mode == MatchMode.Exact) return hay == patternLc;
                return hay.Contains(patternLc);
            }
            if (kind == TargetKind.Label)
            {
                var lbl = (def.label ?? string.Empty).ToLowerInvariant();
                if (mode == MatchMode.Exact) return lbl == patternLc;
                return lbl.Contains(patternLc);
            }

            var tags = def.weaponTags ?? new List<string>();
            if (mode == MatchMode.Exact)
                return tags.Any(t => string.Equals(t ?? "", patternLc, StringComparison.OrdinalIgnoreCase));

            return tags.Any(t => (t ?? "").ToLowerInvariant().Contains(patternLc));
        }

        public static bool LooksLikeRangedWeaponDef(ThingDef def)
        {
            if (def == null) return false;
            try
            {
                if (def.IsRangedWeapon) return true;
            }
            catch { /* forks */ }

            if (def.Verbs == null) return false;
            foreach (var vp in def.Verbs)
            {
                if (vp != null && !vp.IsMeleeAttack && vp.range > 1f) return true;
            }
            return false;
        }

        /// <summary>All ranged weapon defs from DefDatabase that match this rule's Kind/Mode/Pattern.</summary>
        public static List<ThingDef> CollectMatchingWeaponDefs(Rule rule)
        {
            var result = new List<ThingDef>();
            if (rule == null || string.IsNullOrWhiteSpace(rule.Pattern)) return result;

            string patternLc = rule.Pattern.ToLowerInvariant();
            var all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (!LooksLikeRangedWeaponDef(def)) continue;
                if (!WeaponDefMatches(def, rule.Kind, rule.Mode, patternLc)) continue;
                result.Add(def);
            }

            result.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        /// <summary>
        /// Shared match used by apply + debug + cache. Returns aim/fire (capability-clamped)
        /// and the compiled rule/entry indices. Entry index is into the compiled (sorted) list.
        /// </summary>
        public static bool TryGetMatch(
            Pawn pawn,
            ThingWithComps weapon,
            float dist,
            out int ruleIndex,
            out int entryIndex,
            out AimOption aim,
            out FireOption fire)
        {
            ruleIndex = -1;
            entryIndex = -1;
            aim = AimOption.Snapshot;
            fire = FireOption.Single;

            if (weapon == null || compiled.Count == 0 || pawn == null) return false;

            var eq = weapon.TryGetComp<CompEquippable>();
            var vp = eq?.PrimaryVerb?.verbProps as VerbPropertiesCE;

            // 1) Custom rules (index 1..N); empty-pattern customs are skipped (IsDefault).
            for (int i = 1; i < compiled.Count; i++)
            {
                var cr = compiled[i];
                if (!cr.Enabled) continue;
                if (!cr.Matches(pawn, weapon) || cr.IsDefault) continue;

                var sel = cr.SelectForDistance(dist, vp);
                if (sel == null) continue;

                ruleIndex = i;
                entryIndex = sel.Value.entryIndex;
                aim = sel.Value.aim;
                fire = sel.Value.fire;
                return true;
            }

            // 2) Default fallback (index 0)
            var def = compiled[0];
            if (def != null && def.Enabled && def.Matches(pawn, weapon))
            {
                var sel = def.SelectForDistance(dist, vp);
                if (sel != null)
                {
                    ruleIndex = 0;
                    entryIndex = sel.Value.entryIndex;
                    aim = sel.Value.aim;
                    fire = sel.Value.fire;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Return aim/fire by evaluating CUSTOM rules first (index 1..N), then falling back to Default (index 0).
        /// </summary>
        public static (AimOption aim, FireOption fire)? Evaluate(Pawn pawn, ThingWithComps weapon, float dist)
        {
            if (!TryGetMatch(pawn, weapon, dist, out _, out _, out var aim, out var fire))
                return null;
            return (aim, fire);
        }
    }
}
