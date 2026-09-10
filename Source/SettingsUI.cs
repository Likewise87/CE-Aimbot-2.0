using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.CE_Aimbot
{
    public class Mod_Aimbot : Mod
    {
        public static AimbotSettings Settings;

        private Vector2 scroll;
        private bool scrollToBottom;

        private bool generalExpanded = true;
        private readonly HashSet<Rule> collapsedRules = new HashSet<Rule>();

        private bool hasErrors;
        private string errorText = "";
        private bool settingsDirty;

        private readonly Dictionary<RangeEntry, string> rangeEdit = new Dictionary<RangeEntry, string>();

        private const float EntryRowH = 28f;
        private const float DeleteIconSize = 24f;
        private const float ApplyBarHeight = 36f;
        /// <summary>Left edge of Aim/Fire buttons on entry rows.</summary>
        private const float FieldStart = 168f;
        /// <summary>From slate / To field / fixed-0 slate share this size.</summary>
        private const float RangeCellW = 48f;
        private const float RangeCellH = 24f;
        private const float ToWordW = 22f;
        private const float BandInnerGap = 8f;
        private const float ModeBtnW = 140f;
        /// <summary>Equal gap between range cluster, aim, fire, delete, and add.</summary>
        private const float EntryGap = 12f;
        private const float SplitMinSpan = 0.01f;
        private const float SoleSplitHi = 10f;
        private const float MaxRangeValue = 1000f;
        /// <summary>Enabled checkbox sits 45px from the right; 24px wide → right edge pad 21.</summary>
        private const float EnabledToggleRightPad = 21f;

        /// <summary>Left edge of the editable To cell (and of rule name / DefName / min skill).</summary>
        private static float ToFieldStart => FieldStart - EntryGap - RangeCellW;

        public Mod_Aimbot(ModContentPack content) : base(content)
        {
            Settings = GetSettings<AimbotSettings>();
            if (AimbotSettings.NormalizeAllRules(Settings.Rules) || AimbotSettings.NormalizedEntriesOnLastLoad)
            {
                settingsDirty = true;
                AimbotSettings.NormalizedEntriesOnLastLoad = false;
            }

            if (AimbotSettings.AreRulesValid(Settings.Rules))
            {
                RuleEngine.RebuildCompiled(Settings.Rules);
            }
            else
            {
                Log.Warning("[TSA.CE_Aimbot] Settings have broken rules at startup. Live aimbot was not updated until you Apply valid rules.");
                settingsDirty = true;
            }
        }

        public override string SettingsCategory() => "CE Aimbot 2.0";

        public override void WriteSettings()
        {
            // Closing the options window: apply if valid; otherwise keep prior live compile and warn.
            if (settingsDirty)
            {
                if (AimbotSettings.TryCollectRuleErrors(Settings.Rules, out _))
                {
                    Messages.Message(
                        "CE Aimbot: you have broken rules. Live aimbot was not updated. Fix the issues and click Apply changes.",
                        MessageTypeDefOf.RejectInput,
                        false);
                }
                else
                {
                    CommitAppliedRules(persist: false);
                }
            }
            base.WriteSettings();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            hasErrors = false;
            errorText = "";

            if (Settings.Rules.Count == 0 || Settings.Rules[0] == null)
            {
                Settings.BuildDefaultRules();
                CommitAppliedRules(persist: false);
            }

            ValidateRules(Settings.Rules);

            // Sticky apply bar at bottom (outside scroll).
            Rect scrollRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - ApplyBarHeight - 4f);
            Rect applyBarRect = new Rect(inRect.x, inRect.yMax - ApplyBarHeight, inRect.width, ApplyBarHeight);

            float contentH = CalculateContentHeight(Settings.Rules);
            var view = new Rect(0, 0, scrollRect.width - 16f, Mathf.Max(scrollRect.height, contentH + 80f));

            if (scrollToBottom)
            {
                scroll.y = Mathf.Max(0f, view.height - scrollRect.height + 40f);
                scrollToBottom = false;
            }

            Widgets.BeginScrollView(scrollRect, ref scroll, view);

            var listing = new Listing_Standard();
            listing.Begin(view);

            SettingsUiUtil.DrawMenuTopBar(
                listing,
                "Reset to defaults",
                () =>
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "Are you sure? This will delete all custom rules and restore the default rule.",
                        () =>
                        {
                            Settings.BuildDefaultRules();
                            rangeEdit.Clear();
                            collapsedRules.Clear();
                            generalExpanded = true;
                            CommitAppliedRules(persist: true);
                            scroll.y = 0f;
                        },
                        true));
                },
                ExpandAll,
                CollapseAll,
                "Add rule",
                () =>
                {
                    Settings.Rules.Add(new Rule("New Rule", TargetKind.DefName, MatchMode.Contains, "Example",
                        AimbotSettings.NewRuleDefaultEntries()));
                    MarkDirty();
                    scrollToBottom = true;
                });

            if (SettingsUiUtil.DrawCollapsibleHeader(listing, "General", ref generalExpanded, SettingsUiUtil.SectionHeaderColor,
                "Gizmo default, Player.log diagnostics, and how rules are evaluated."))
            {
                bool gizmoDefault = Settings.GizmoEnabledByDefault;
                SettingsUiUtil.DrawLabeledCheckbox(
                    listing,
                    "CE Aimbot enabled by default on pawns",
                    ref gizmoDefault,
                    "When on, new pawns start with the Aimbot gizmo enabled. Clearing overrides resets living pawns to this default.");
                if (gizmoDefault != Settings.GizmoEnabledByDefault)
                {
                    Settings.GizmoEnabledByDefault = gizmoDefault;
                    PawnAutoAimStore.Instance?.ClearOverrides();
                    // Immediate: not part of rule compile; still mark dirty so WriteSettings persists.
                    MarkDirty();
                }

                bool dbg = Settings.DebugLogging;
                SettingsUiUtil.DrawLabeledCheckbox(
                    listing,
                    "Log rule matches to Player.log",
                    ref dbg,
                    "Developer aid only. Each shot writes the matched rule, range entry, and CE aim/fire before and after apply. Leave off in normal play.");
                if (dbg != Settings.DebugLogging)
                {
                    Settings.DebugLogging = dbg;
                    MarkDirty();
                }

                listing.Gap(4f);
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(listing.GetRect(48f),
                    "Configure which firing and aiming mode is applied to which weapon at what range level. Optionally, you may also break this down by pawn skill level. Custom rules top to bottom; Default is fallback only. Click Apply changes to push rule edits to live aimbot.");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            DrawDefaultBlock(listing, Settings.Rules[0]);

            for (int i = 1; i < Settings.Rules.Count; i++)
            {
                DrawCustomRule(listing, Settings.Rules[i], i, Settings.Rules);
            }

            if (hasErrors)
            {
                listing.Gap(10f);
                var errRect = listing.GetRect(Mathf.Max(40f, 18f + errorText.Split('\n').Length * 16f));
                GUI.color = Color.red;
                Text.Font = GameFont.Tiny;
                Widgets.Label(errRect, "Issues detected:\n" + errorText.Trim());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }

            listing.End();
            Widgets.EndScrollView();

            DrawApplyBar(applyBarRect);
        }

        private void DrawApplyBar(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(4f);

            const float btnW = 150f;
            Rect btnRect = new Rect(inner.xMax - btnW, inner.y, btnW, inner.height);
            Rect statusRect = new Rect(inner.x + 4f, inner.y, btnRect.x - inner.x - 10f, inner.height);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (hasErrors)
            {
                GUI.color = new Color(1f, 0.35f, 0.35f);
                Widgets.Label(statusRect, "You have broken rules");
            }
            else if (settingsDirty)
            {
                GUI.color = new Color(1f, 0.85f, 0.2f);
                Widgets.Label(statusRect, "You have unsaved changes");
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(statusRect, "All changes applied");
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;

            bool canApply = settingsDirty && !hasErrors;
            TooltipHandler.TipRegion(btnRect, hasErrors
                ? "Fix broken rules before applying."
                : settingsDirty
                    ? "Push the current rules to live aimbot and save."
                    : "No pending rule changes.");

            GUI.enabled = canApply;
            if (Widgets.ButtonText(btnRect, "Apply changes"))
            {
                if (CommitAppliedRules(persist: true))
                    Messages.Message("CE Aimbot rules applied.", MessageTypeDefOf.TaskCompletion, false);
            }
            GUI.enabled = true;
        }

        private void ExpandAll()
        {
            generalExpanded = true;
            collapsedRules.Clear();
        }

        private void CollapseAll()
        {
            generalExpanded = false;
            collapsedRules.Clear();
            foreach (var r in Settings.Rules)
            {
                if (r != null) collapsedRules.Add(r);
            }
        }

        private bool IsExpanded(Rule rule) => rule != null && !collapsedRules.Contains(rule);

        private void SetExpanded(Rule rule, bool expanded)
        {
            if (rule == null) return;
            if (expanded) collapsedRules.Remove(rule);
            else collapsedRules.Add(rule);
        }

        private float CalculateContentHeight(List<Rule> rules)
        {
            float h = 4f + 30f + 6f; // top bar

            // General (same chrome as DrawCollapsibleHeader)
            h += SectionHeaderHeight(generalExpanded);
            if (generalExpanded)
                h += 24f + 24f + 4f + 48f;

            if (rules == null || rules.Count == 0) return h + 200f;

            // Default
            h += SectionHeaderHeight(IsExpanded(rules[0]));
            if (IsExpanded(rules[0]))
                h += DefaultBodyHeight(rules[0]);

            for (int i = 1; i < rules.Count; i++)
            {
                var r = rules[i];
                h += SectionHeaderHeight(IsExpanded(r));
                if (IsExpanded(r))
                    h += CustomBodyHeight(r);
            }

            if (hasErrors) h += 10f + Mathf.Max(40f, 18f + errorText.Split('\n').Length * 16f);
            return h;
        }

        private static float SectionHeaderHeight(bool expanded) =>
            8f + 6f + 4f + 26f + 4f; // gap, line above headline, gap, header, gap

        private float DefaultBodyHeight(Rule rule)
        {
            return EntryTableHeight(rule);
        }

        private float CustomBodyHeight(Rule rule)
        {
            // Must match DrawCustomRule: Gap4 + name28 + Gap10 + match28 + Gap10 + skill(2+24) + Gap15
            float h = 4f + 28f + 10f + 28f + 10f + 2f + 24f + 15f;
            h += EntryTableHeight(rule);
            return h;
        }

        private float EntryTableHeight(Rule rule)
        {
            float h = 0f;
            int entries = rule?.Entries?.Count ?? 0;
            h += entries * EntryRowH;
            if (rule != null && rule.Enabled && entries == 0) h += 50f;
            if (entries == 0) h += 28f; // fallback add control when there is no first row
            return h;
        }

        private void DrawDefaultBlock(Listing_Standard listing, Rule rule)
        {
            bool expanded = IsExpanded(rule);
            if (!SettingsUiUtil.DrawCollapsibleHeader(listing, "Default rule", ref expanded, SettingsUiUtil.SectionHeaderColor,
                "Fallback when no custom rule matches the target."))
            {
                SetExpanded(rule, expanded);
                return;
            }
            SetExpanded(rule, expanded);
            DrawEntryTable(listing, rule);
        }

        private void DrawCustomRule(Listing_Standard listing, Rule rule, int index, List<Rule> all)
        {
            if (rule == null) return;

            string headerName = string.IsNullOrWhiteSpace(rule.Name) ? $"Rule #{index}" : rule.Name;
            bool expanded = IsExpanded(rule);
            if (!SettingsUiUtil.DrawCollapsibleHeader(listing, headerName, ref expanded, SettingsUiUtil.SectionHeaderColor))
            {
                SetExpanded(rule, expanded);
                return;
            }
            SetExpanded(rule, expanded);

            listing.Gap(4f);

            // Name + management row
            var titleRect = listing.GetRect(28f);
            float managementButtonsStart = titleRect.xMax - 200f;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(titleRect.x, titleRect.y, ToFieldStart - 5f, titleRect.height), "<b>Rule name</b>");
            Text.Anchor = TextAnchor.UpperLeft;

            float nameInputStart = titleRect.x + ToFieldStart;
            float nameFieldRight = managementButtonsStart - 10f;
            var nameEditRect = new Rect(nameInputStart, titleRect.y, nameFieldRight - nameInputStart, titleRect.height);
            string name = string.IsNullOrWhiteSpace(rule.Name) ? $"Rule #{index}" : rule.Name;
            string newName = Widgets.TextField(nameEditRect, name);
            if (newName != name) { rule.Name = newName; MarkDirty(); }

            float buttonX = managementButtonsStart;
            float iconY = titleRect.y + (titleRect.height - DeleteIconSize) * 0.5f;

            bool canMoveUp = index > 1;
            bool canMoveDown = index < all.Count - 1;

            var upRect = new Rect(buttonX, iconY, DeleteIconSize, DeleteIconSize);
            Color prevUp = GUI.color;
            if (!canMoveUp) GUI.color = new Color(1f, 1f, 1f, 0.35f);
            if (Widgets.ButtonImage(upRect, TexButton.ReorderUp) && canMoveUp)
            {
                (all[index - 1], all[index]) = (all[index], all[index - 1]);
                MarkDirty();
            }
            GUI.color = prevUp;
            TooltipHandler.TipRegion(upRect, canMoveUp ? "Move rule up" : "Already at top");
            buttonX += DeleteIconSize + 6f;

            var downRect = new Rect(buttonX, iconY, DeleteIconSize, DeleteIconSize);
            Color prevDown = GUI.color;
            if (!canMoveDown) GUI.color = new Color(1f, 1f, 1f, 0.35f);
            if (Widgets.ButtonImage(downRect, TexButton.ReorderDown) && canMoveDown)
            {
                (all[index + 1], all[index]) = (all[index], all[index + 1]);
                MarkDirty();
            }
            GUI.color = prevDown;
            TooltipHandler.TipRegion(downRect, canMoveDown ? "Move rule down" : "Already at bottom");
            buttonX += DeleteIconSize + 6f;

            var copyRect = new Rect(buttonX, iconY, DeleteIconSize, DeleteIconSize);
            if (Widgets.ButtonImage(copyRect, TexButton.Copy))
            {
                var copy = new Rule
                {
                    Enabled = rule.Enabled,
                    Name = rule.Name + " (Copy)",
                    Kind = rule.Kind,
                    Mode = rule.Mode,
                    Pattern = rule.Pattern,
                    SkillRange = new IntRange(rule.SkillRange.min, rule.SkillRange.max),
                    Entries = rule.Entries.Select(e => new RangeEntry(e.MinRange, e.Aim, e.Fire)).ToList()
                };
                all.Insert(index + 1, copy);
                MarkDirty();
            }
            TooltipHandler.TipRegion(copyRect, "Copy rule");
            buttonX += DeleteIconSize + 6f;

            var ruleDelRect = new Rect(buttonX, iconY, DeleteIconSize, DeleteIconSize);
            if (Widgets.ButtonImage(ruleDelRect, TexButton.Delete))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation($"Delete {name}?", () =>
                {
                    if (rule.Entries != null)
                    {
                        foreach (var ent in rule.Entries)
                            rangeEdit.Remove(ent);
                    }
                    collapsedRules.Remove(rule);
                    all.RemoveAt(index);
                    MarkDirty();
                }, true));
            }
            TooltipHandler.TipRegion(ruleDelRect, "Delete rule");

            var checkRect = new Rect(titleRect.xMax - 45f, titleRect.y + 2f, 24f, 24f);
            bool en = rule.Enabled;
            Widgets.Checkbox(checkRect.position, ref en);
            if (en != rule.Enabled) { rule.Enabled = en; MarkDirty(); }
            TooltipHandler.TipRegion(checkRect, rule.Enabled ? "Rule enabled" : "Rule disabled");

            listing.Gap(10f);

            // Match row: kind button lines up with the To value / rule name field
            var row1 = listing.GetRect(28f);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect matchLabelRect = new Rect(row1.x, row1.y, ToFieldStart - 5f, row1.height);
            Widgets.Label(matchLabelRect, "Apply to");
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(matchLabelRect,
                "Which weapons this rule covers. Pick DefName, Tag, or Label, then Exact or Contains, and enter the text to match.");

            var kindRect = new Rect(row1.x + ToFieldStart, row1.y, 80f, row1.height);
            if (Widgets.ButtonText(kindRect, rule.Kind.ToString()))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("DefName", () => { rule.Kind = TargetKind.DefName; MarkDirty(); }),
                    new FloatMenuOption("Tag", () => { rule.Kind = TargetKind.Tag; MarkDirty(); }),
                    new FloatMenuOption("Label", () => { rule.Kind = TargetKind.Label; MarkDirty(); })
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            var modeRect = new Rect(kindRect.xMax + 5f, row1.y, 80f, row1.height);
            if (Widgets.ButtonText(modeRect, rule.Mode.ToString()))
            {
                var opts = new List<FloatMenuOption>
                {
                    new FloatMenuOption("Exact", () => { rule.Mode = MatchMode.Exact; MarkDirty(); }),
                    new FloatMenuOption("Contains", () => { rule.Mode = MatchMode.Contains; MarkDirty(); })
                };
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            // Pattern ends aligned with the rule name field above; View_Details sits in the icon strip.
            var patternRect = new Rect(modeRect.xMax + 5f, row1.y, Mathf.Max(40f, nameFieldRight - (modeRect.xMax + 5f)), row1.height);
            string prevPattern = rule.Pattern ?? string.Empty;
            string newPattern = Widgets.TextField(patternRect, prevPattern);
            if (newPattern != prevPattern)
            {
                rule.Pattern = newPattern;
                MarkDirty();
            }

            float matchIconY = row1.y + (row1.height - DeleteIconSize) * 0.5f;
            var showMatchRect = new Rect(managementButtonsStart, matchIconY, DeleteIconSize, DeleteIconSize);
            Texture2D viewIcon = AimbotTextures.ViewDetails ?? TexButton.Info;
            if (Widgets.ButtonImage(showMatchRect, viewIcon))
            {
                string display = string.IsNullOrWhiteSpace(rule.Name) ? $"Rule #{index}" : rule.Name;
                Find.WindowStack.Add(new Window_MatchingWeapons(rule, display));
            }
            TooltipHandler.TipRegion(showMatchRect,
                "Show matching weapons: lists ranged weapons from the def database that match this rule's Kind, Mode, and Pattern.");

            listing.Gap(10f);

            // Skill: min / max side by side
            int skillMin = rule.SkillRange.min;
            int skillMax = rule.SkillRange.max;
            SettingsUiUtil.DrawDualIntSliders(
                listing,
                "Min skill level",
                ref skillMin,
                "Max skill level",
                ref skillMax,
                0,
                20,
                "Shooting skill must fall in this range. Max 20 means 20 or higher.",
                leftInset: ToFieldStart,
                rightInset: EnabledToggleRightPad);
            if (skillMin != rule.SkillRange.min || skillMax != rule.SkillRange.max)
            {
                rule.SkillRange = new IntRange(skillMin, skillMax);
                MarkDirty();
            }

            Text.Font = GameFont.Small;
            listing.Gap(15f);
            DrawEntryTable(listing, rule);
        }

        private void DrawEntryTable(Listing_Standard listing, Rule rule)
        {
            if (rule.Entries == null) rule.Entries = new List<RangeEntry>();
            if (AimbotSettings.NormalizeRuleEntries(rule))
                MarkDirty();

            for (int e = 0; e < rule.Entries.Count; e++)
            {
                var entry = rule.Entries[e];
                var r = listing.GetRect(EntryRowH);
                bool isLast = e == rule.Entries.Count - 1;

                Text.Font = GameFont.Small;

                // Aim lines up with DefName (FieldStart). Band: [From slate] to [To field/slate].
                float aimX = r.x + FieldStart;
                float bandW = RangeCellW + BandInnerGap + ToWordW + BandInnerGap + RangeCellW;
                float bandX = Mathf.Max(r.x, aimX - EntryGap - bandW);
                float cellY = r.y + (r.height - RangeCellH) * 0.5f;
                var fromRect = new Rect(bandX, cellY, RangeCellW, RangeCellH);
                var toWordRect = new Rect(fromRect.xMax + BandInnerGap, r.y, ToWordW, r.height);
                var toRect = new Rect(toWordRect.xMax + BandInnerGap, cellY, RangeCellW, RangeCellH);

                string fromText = e == 0
                    ? "∞"
                    : rule.Entries[e - 1].MinRange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                SettingsUiUtil.DrawSlateValueBox(fromRect, fromText);

                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = Color.gray;
                Widgets.Label(toWordRect, "to");
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                bool orderBad = IsRangeOrderViolation(rule, e);
                bool parseBad = false;

                if (isLast)
                {
                    // Last / sole To is always 0 (not editable).
                    entry.MinRange = 0f;
                    SettingsUiUtil.DrawSlateValueBox(toRect, "0");
                }
                else
                {
                    if (!rangeEdit.TryGetValue(entry, out var buf))
                        buf = entry.MinRange.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

                    parseBad = !float.TryParse(buf, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _)
                        && !float.TryParse(buf, out _);
                    if (orderBad || parseBad) GUI.color = new Color(1f, 0.45f, 0.45f);
                    GUIStyle fieldStyle = Text.CurTextFieldStyle;
                    TextAnchor prevAlign = TextAnchor.MiddleLeft;
                    if (fieldStyle != null)
                    {
                        prevAlign = fieldStyle.alignment;
                        fieldStyle.alignment = TextAnchor.MiddleCenter;
                    }
                    string newBuf = fieldStyle != null
                        ? GUI.TextField(toRect, buf ?? "", fieldStyle)
                        : Widgets.TextField(toRect, buf);
                    if (fieldStyle != null)
                        fieldStyle.alignment = prevAlign;
                    GUI.color = Color.white;

                    if (newBuf != buf)
                    {
                        rangeEdit[entry] = newBuf;
                        if (float.TryParse(newBuf, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var f)
                            || float.TryParse(newBuf, out f))
                        {
                            entry.MinRange = Mathf.Clamp(f, 0f, MaxRangeValue);
                            MarkDirty();
                        }
                    }
                }

                string tt = BandTooltip(rule, e, parseBad, orderBad);
                TooltipHandler.TipRegion(new Rect(bandX, r.y, bandW, r.height), tt);

                var aimRect = new Rect(aimX, r.y, ModeBtnW, r.height);
                if (SettingsUiUtil.DrawSlateButtonWithIcon(aimRect, entry.Aim.ToString(), IconFor(entry.Aim)))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (AimOption ao in Enum.GetValues(typeof(AimOption)))
                    {
                        Texture2D? icon = IconFor(ao);
                        if (icon != null)
                            opts.Add(new FloatMenuOption(ao.ToString(), () => { entry.Aim = ao; MarkDirty(); }, icon, Color.white));
                        else
                            opts.Add(new FloatMenuOption(ao.ToString(), () => { entry.Aim = ao; MarkDirty(); }));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }

                var fireRect = new Rect(aimRect.xMax + EntryGap, r.y, ModeBtnW, r.height);
                if (SettingsUiUtil.DrawSlateButtonWithIcon(fireRect, entry.Fire.ToString(), IconFor(entry.Fire)))
                {
                    var opts = new List<FloatMenuOption>();
                    foreach (FireOption fo in Enum.GetValues(typeof(FireOption)))
                    {
                        Texture2D? icon = IconFor(fo);
                        if (icon != null)
                            opts.Add(new FloatMenuOption(fo.ToString(), () => { entry.Fire = fo; MarkDirty(); }, icon, Color.white));
                        else
                            opts.Add(new FloatMenuOption(fo.ToString(), () => { entry.Fire = fo; MarkDirty(); }));
                    }
                    Find.WindowStack.Add(new FloatMenu(opts));
                }

                var delRect = new Rect(
                    fireRect.xMax + EntryGap,
                    r.y + (r.height - DeleteIconSize) * 0.5f,
                    DeleteIconSize,
                    DeleteIconSize);
                if (Widgets.ButtonImage(delRect, TexButton.Delete))
                {
                    DeleteEntryAt(rule, e);
                    e--;
                    continue;
                }
                TooltipHandler.TipRegion(delRect, "Delete entry");

                // Plus on every row: split this band toward closer range.
                var addRect = new Rect(delRect.xMax + EntryGap, delRect.y, DeleteIconSize, DeleteIconSize);
                bool canSplit = TryGetSplitBounds(rule, e, out float hi, out float lo)
                    && TryComputeSplitMid(hi, lo, out _);
                Color prevPlus = GUI.color;
                if (!canSplit) GUI.color = new Color(1f, 1f, 1f, 0.35f);
                if (Widgets.ButtonImage(addRect, TexButton.Plus) && canSplit)
                {
                    SplitBandAt(rule, e);
                }
                GUI.color = prevPlus;
                TooltipHandler.TipRegion(addRect, canSplit
                    ? "Split this band toward closer range"
                    : "Band too tight to split");
            }

            if (rule.Enabled && rule.Entries.Count == 0) DrawEmptyRuleWarning(listing, rule);

            if (rule.Entries.Count == 0)
            {
                Text.Font = GameFont.Small;
                var emptyAdd = listing.GetRect(28f);
                var addRect = new Rect(emptyAdd.x, emptyAdd.y + (emptyAdd.height - DeleteIconSize) * 0.5f, DeleteIconSize, DeleteIconSize);
                if (Widgets.ButtonImage(addRect, TexButton.Plus))
                {
                    rule.Entries.AddRange(AimbotSettings.EmptyRuleStarterEntries());
                    MarkDirty();
                }
                TooltipHandler.TipRegion(addRect, "Add default bands (10 to 0)");
            }
        }

        private static string BandTooltip(Rule rule, int e, bool parseBad, bool orderBad)
        {
            if (parseBad)
                return "Enter a valid number from 0 to 1000 (e.g. 30 or 12.5).";
            if (orderBad)
                return "To values must be strictly descending down to 0.";

            float to = rule.Entries[e].MinRange;
            string toStr = to.ToString("0.##");
            if (rule.Entries.Count == 1)
                return "∞ to 0: applies to all distances.";
            if (e == 0)
                return $"{toStr} and farther.";
            if (e == rule.Entries.Count - 1)
            {
                float from = rule.Entries[e - 1].MinRange;
                return $"Closer than {from.ToString("0.##")}.";
            }
            float fromMid = rule.Entries[e - 1].MinRange;
            return $"{toStr} up to {fromMid.ToString("0.##")}.";
        }

        private static bool TryGetSplitBounds(Rule rule, int index, out float hi, out float lo)
        {
            hi = 0f;
            lo = 0f;
            if (rule?.Entries == null || index < 0 || index >= rule.Entries.Count) return false;
            int n = rule.Entries.Count;
            bool isLast = index == n - 1;

            if (n == 1)
            {
                // Sole ∞→0: seed hi=10, lo=0 → become [10,0].
                hi = SoleSplitHi;
                lo = 0f;
                return true;
            }

            if (isLast)
            {
                // Split last band (From→0): hi = previous To, lo = 0.
                hi = rule.Entries[n - 2].MinRange;
                lo = 0f;
                return true;
            }

            hi = rule.Entries[index].MinRange;
            lo = rule.Entries[index + 1].MinRange;
            return true;
        }

        private static bool TryComputeSplitMid(float hi, float lo, out float mid)
        {
            mid = 0f;
            if (hi - lo <= SplitMinSpan) return false;
            mid = (float)System.Math.Round((hi + lo) * 0.5, 2);
            if (mid > lo && mid < hi) return true;
            mid = hi - 2f;
            if (mid > lo) return true;
            mid = lo + (hi - lo) * 0.5f;
            return mid > lo && mid < hi;
        }

        private void SplitBandAt(Rule rule, int index)
        {
            if (!TryGetSplitBounds(rule, index, out float hi, out float lo)) return;
            if (!TryComputeSplitMid(hi, lo, out float mid)) return;

            int n = rule.Entries.Count;
            if (n == 1)
            {
                // Sole [0] → [10, 0] (∞→10, 10→0).
                var keep = rule.Entries[0];
                rule.Entries.Clear();
                rule.Entries.Add(new RangeEntry(SoleSplitHi, keep.Aim, keep.Fire));
                rule.Entries.Add(new RangeEntry(0f, AimOption.Snapshot, FireOption.Burst));
                MarkDirty();
                return;
            }

            bool isLast = index == n - 1;
            int insertAt = isLast ? n - 1 : index + 1;
            var template = rule.Entries[index];
            rule.Entries.Insert(insertAt, new RangeEntry(mid, template.Aim, template.Fire));
            AimbotSettings.NormalizeRuleEntries(rule);
            MarkDirty();
        }

        private void DeleteEntryAt(Rule rule, int index)
        {
            if (rule?.Entries == null || index < 0 || index >= rule.Entries.Count) return;
            var entry = rule.Entries[index];
            rangeEdit.Remove(entry);
            rule.Entries.RemoveAt(index);
            if (rule.Entries.Count > 0)
                AimbotSettings.NormalizeRuleEntries(rule);
            MarkDirty();
        }

        private void DrawEmptyRuleWarning(Listing_Standard listing, Rule rule)
        {
            var warnRect = listing.GetRect(24f);
            GUI.color = Color.red;
            Text.Font = GameFont.Tiny;
            Widgets.Label(warnRect, "Missing entries! Rule will not trigger.");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(listing.GetRect(26f).LeftPartPixels(150f), "Add default entry"))
            {
                if (rule.Entries == null) rule.Entries = new List<RangeEntry>();
                rule.Entries.AddRange(AimbotSettings.EmptyRuleStarterEntries());
                MarkDirty();
            }
        }

        private void ValidateRules(List<Rule> rules)
        {
            if (AimbotSettings.TryCollectRuleErrors(rules, out string errors))
            {
                hasErrors = true;
                errorText = errors;
            }
        }

        private static bool IsRangeOrderViolation(Rule rule, int index)
        {
            if (rule?.Entries == null || index < 0 || index >= rule.Entries.Count) return false;
            if (index > 0 && rule.Entries[index - 1].MinRange <= rule.Entries[index].MinRange) return true;
            if (index + 1 < rule.Entries.Count && rule.Entries[index].MinRange <= rule.Entries[index + 1].MinRange) return true;
            return false;
        }

        private void MarkDirty() => settingsDirty = true;

        /// <summary>Rebuild live RuleEngine from current settings when valid. Returns false if blocked by errors.</summary>
        private bool CommitAppliedRules(bool persist)
        {
            AimbotSettings.NormalizeAllRules(Settings.Rules);
            if (AimbotSettings.TryCollectRuleErrors(Settings.Rules, out _))
            {
                hasErrors = true;
                return false;
            }

            RuleEngine.RebuildCompiled(Settings.Rules);
            settingsDirty = false;
            if (persist)
                base.WriteSettings();
            return true;
        }

        private static Texture2D? IconFor(AimOption aim) => aim switch
        {
            AimOption.Aimed => AimbotTextures.Aimed,
            AimOption.Snapshot => AimbotTextures.Snapshot,
            AimOption.Suppress => AimbotTextures.Suppress,
            _ => null
        };

        private static Texture2D? IconFor(FireOption fire) => fire switch
        {
            FireOption.Single => AimbotTextures.Single,
            FireOption.Burst => AimbotTextures.Burst,
            FireOption.FullAuto => AimbotTextures.FullAuto,
            _ => null
        };
    }
}
