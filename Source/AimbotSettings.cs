using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace TSA.CE_Aimbot
{
    public enum TargetKind : byte { DefName, Tag, Label }
    public enum MatchMode : byte { Exact, Contains }

    public enum AimOption : byte { Aimed, Snapshot, Suppress }
    public enum FireOption : byte { Single, Burst, FullAuto }

    public sealed class RangeEntry : IExposable
    {
        public float MinRange;
        public AimOption Aim;
        public FireOption Fire;

        public RangeEntry() { }
        public RangeEntry(float minRange, AimOption aim, FireOption fire)
        {
            MinRange = minRange; Aim = aim; Fire = fire;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref MinRange, "minRange", 0f);
            Scribe_Values.Look(ref Aim, "aim", AimOption.Snapshot);
            Scribe_Values.Look(ref Fire, "fire", FireOption.Single);
        }
    }

    public sealed class Rule : IExposable
    {
        public bool Enabled = true;
        public string Name = "Rule";
        public TargetKind Kind = TargetKind.DefName;
        public MatchMode Mode = MatchMode.Contains;
        public string Pattern = string.Empty;
        public IntRange SkillRange = new IntRange(0, 20); // New skill range field

        public List<RangeEntry> Entries = new List<RangeEntry>();

        public Rule() { }

        public Rule(string name, TargetKind kind, MatchMode mode, string pattern, IEnumerable<RangeEntry> entries)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Rule" : name;
            Kind = kind; Mode = mode; Pattern = pattern ?? string.Empty;
            Entries = entries?.ToList() ?? new List<RangeEntry>();
            SkillRange = new IntRange(0, 20);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref Name, "name", "Rule");
            Scribe_Values.Look(ref Kind, "kind", TargetKind.DefName);
            Scribe_Values.Look(ref Mode, "mode", MatchMode.Contains);
            Scribe_Values.Look(ref Pattern, "pattern", string.Empty);
            Scribe_Values.Look(ref SkillRange, "skillRange", new IntRange(0, 20));
            Scribe_Collections.Look(ref Entries, "entries", LookMode.Deep);
            if (Entries == null) Entries = new List<RangeEntry>();
        }
    }

    public sealed class AimbotSettings : ModSettings
    {
        // Index 0 = Default (pinned)
        public List<Rule> Rules = new List<Rule>();

        // Debug: Log.Message each shot's matched rule/entry and CE modes (Player.log spam).
        public bool DebugLogging = false;

        // Per-pawn gizmo default: ON when true, OFF when false
        public bool GizmoEnabledByDefault = true;

        /// <summary>Set when ExposeData forced a legacy last To to 0; Mod marks settings dirty.</summary>
        public static bool NormalizedEntriesOnLastLoad;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Rules, "rules", LookMode.Deep);
            Scribe_Values.Look(ref DebugLogging, "debugLogging", false);
            Scribe_Values.Look(ref GizmoEnabledByDefault, "gizmoEnabledByDefault", true);
            // Legacy key applyToNonPlayerPawns intentionally ignored (feature never shipped).

            if (Rules == null || Rules.Count == 0)
            {
                BuildDefaultRules();
            }
            else
            {
                if (Rules[0] == null) Rules[0] = BuildDefaultRule();
                Rules[0].Enabled = true; // default always enabled
                if (string.IsNullOrWhiteSpace(Rules[0].Name))
                    Rules[0].Name = "Default";
            }

            // Keep compiled cache in sync after load only when rules are valid.
            // Broken descending bands must not silently go live (player fixes via Apply in settings).
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                NormalizedEntriesOnLastLoad = NormalizeAllRules(Rules);
                if (AreRulesValid(Rules))
                    RuleEngine.RebuildCompiled(Rules);
                else
                    Log.Warning("[TSA.CE_Aimbot] Loaded settings have broken rules (empty entries or non-descending ranges). Live aimbot was not updated. Open mod settings, fix the issues, then click Apply changes.");
            }
        }

        /// <summary>
        /// UI bands are From→To with last To always 0. MinRange stores each row's To (inner boundary).
        /// Forces last entry MinRange = 0 (sole becomes [0]). Returns true if any value changed.
        /// </summary>
        public static bool NormalizeRuleEntries(Rule rule)
        {
            if (rule?.Entries == null || rule.Entries.Count == 0) return false;
            var last = rule.Entries[rule.Entries.Count - 1];
            if (last == null) return false;
            if (System.Math.Abs(last.MinRange) <= 0.0001f) return false;
            last.MinRange = 0f;
            return true;
        }

        public static bool NormalizeAllRules(List<Rule> rules)
        {
            if (rules == null) return false;
            bool changed = false;
            for (int i = 0; i < rules.Count; i++)
            {
                if (NormalizeRuleEntries(rules[i]))
                    changed = true;
            }
            return changed;
        }

        /// <summary>True when every enabled rule has entries and strictly descending MinRange thresholds.</summary>
        public static bool AreRulesValid(List<Rule> rules)
        {
            return !TryCollectRuleErrors(rules, out _);
        }

        public static bool TryCollectRuleErrors(List<Rule> rules, out string errorText)
        {
            var sb = new System.Text.StringBuilder();
            if (rules == null || rules.Count == 0)
            {
                errorText = "• No rules.\n";
                return true;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                if (r == null || (i > 0 && !r.Enabled)) continue;
                string name = string.IsNullOrWhiteSpace(r.Name) ? $"Rule #{i}" : r.Name;
                if (i > 0 && string.IsNullOrWhiteSpace(r.Pattern))
                    sb.Append("• ").Append(name).Append(": empty pattern (custom rules need a match pattern; Default is the only empty-pattern fallback).\n");
                if (r.Entries == null || r.Entries.Count == 0)
                {
                    sb.Append("• ").Append(name).Append(": No entries.\n");
                    continue;
                }
                var last = r.Entries[r.Entries.Count - 1];
                if (last != null && System.Math.Abs(last.MinRange) > 0.0001f)
                {
                    sb.Append("• ").Append(name)
                        .Append(": last band must end at 0 (closest), currently ")
                        .Append(last.MinRange.ToString("0.##")).Append(".\n");
                }
                for (int e = 0; e + 1 < r.Entries.Count; e++)
                {
                    if (r.Entries[e].MinRange <= r.Entries[e + 1].MinRange)
                    {
                        sb.Append("• ").Append(name)
                            .Append(": To values must descend (row ").Append(e + 1).Append(" To is ")
                            .Append(r.Entries[e].MinRange.ToString("0.##")).Append(", row ").Append(e + 2)
                            .Append(" To is ").Append(r.Entries[e + 1].MinRange.ToString("0.##")).Append(").\n");
                    }
                }
            }

            errorText = sb.ToString();
            return sb.Length > 0;
        }

        public void BuildDefaultRules()
        {
            Rules = new List<Rule> { BuildDefaultRule() };
        }

        private Rule BuildDefaultRule()
        {
            // Default rule applies to all ranged weapons if nothing else matches.
            var r = new Rule("Default", TargetKind.DefName, MatchMode.Contains, string.Empty, null)
            {
                Enabled = true,
                SkillRange = new IntRange(0, 20)
            };

            // Bands: ∞→30, 30→25, 25→20, 20→16, 16→0 (last To always 0).
            r.Entries.Add(new RangeEntry(30f, AimOption.Aimed, FireOption.Single));
            r.Entries.Add(new RangeEntry(25f, AimOption.Aimed, FireOption.Burst));
            r.Entries.Add(new RangeEntry(20f, AimOption.Snapshot, FireOption.Burst));
            r.Entries.Add(new RangeEntry(16f, AimOption.Snapshot, FireOption.FullAuto));
            r.Entries.Add(new RangeEntry(0f, AimOption.Suppress, FireOption.FullAuto));

            return r;
        }

        // Factory for new user-added rules (last To = 0).
        public static List<RangeEntry> NewRuleDefaultEntries() => new List<RangeEntry>
        {
            new RangeEntry(30f, AimOption.Aimed,    FireOption.Single),
            new RangeEntry(25f, AimOption.Aimed,    FireOption.Burst),
            new RangeEntry(20f, AimOption.Snapshot, FireOption.Burst),
            new RangeEntry(16f, AimOption.Snapshot, FireOption.FullAuto),
            new RangeEntry(0f, AimOption.Suppress, FireOption.FullAuto),
        };

        /// <summary>Empty-rule starter: ∞→10, 10→0.</summary>
        public static List<RangeEntry> EmptyRuleStarterEntries() => new List<RangeEntry>
        {
            new RangeEntry(10f, AimOption.Snapshot, FireOption.Burst),
            new RangeEntry(0f, AimOption.Snapshot, FireOption.Burst),
        };
    }
}