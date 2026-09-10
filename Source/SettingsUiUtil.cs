using System;
using UnityEngine;
using Verse;

namespace TSA.CE_Aimbot
{
    public static class SettingsUiUtil
    {
        public static readonly Color SectionHeaderColor = new Color(0.55f, 0.85f, 1f);

        private static readonly Color SlateFill = new Color(0.16f, 0.18f, 0.22f, 0.92f);
        private static readonly Color SlateHover = new Color(0.22f, 0.26f, 0.32f, 0.96f);
        private static readonly Color SlatePress = new Color(0.12f, 0.14f, 0.17f, 0.96f);
        private static readonly Color SlateOutline = new Color(0.55f, 0.62f, 0.72f, 0.42f);
        private static readonly Color SlateOutlineHover = new Color(0.78f, 0.84f, 0.92f, 0.72f);

        private const float SlateIconSize = 26f;
        private const float SlateIconPad = 8f;
        private const float SlateIconTextGap = 6f;

        /// <summary>Non-interactive slate value box (same chrome as slate buttons).</summary>
        public static void DrawSlateValueBox(Rect rect, string text, Color? textColor = null)
        {
            Color prevColor = GUI.color;
            TextAnchor prevAnchor = Text.Anchor;
            GameFont prevFont = Text.Font;

            Widgets.DrawBoxSolid(rect, SlateFill);
            GUI.color = SlateOutline;
            Widgets.DrawBox(rect, 1);
            GUI.color = textColor ?? new Color(0.78f, 0.82f, 0.88f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text ?? "");

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            GUI.color = prevColor;
        }

        public static bool DrawCollapsibleHeader(Listing_Standard l, string label, ref bool expanded, Color? color = null, string? tip = null)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;

            l.Gap(8f);
            l.GapLine(6f);
            l.Gap(4f);
            Rect r = l.GetRect(26f);
            Widgets.DrawHighlightIfMouseover(r);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(r, tip);
            if (Widgets.ButtonInvisible(r)) expanded = !expanded;

            Color c = color ?? Color.white;
            string arrow = expanded ? "▼" : "▶";
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(r, $"<b><color=#{ColorUtility.ToHtmlStringRGBA(c)}>{arrow}  {label}</color></b>");
            l.Gap(4f);

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            return expanded;
        }

        /// <summary>Red Reset (left), optional secondary (e.g. Add rule), Expand all / Collapse all (right).</summary>
        public static void DrawMenuTopBar(
            Listing_Standard l,
            string resetLabel,
            Action onReset,
            Action? expandAll,
            Action? collapseAll,
            string? secondaryLeftLabel = null,
            Action? onSecondaryLeft = null)
        {
            if (onReset == null) return;

            l.Gap(4f);
            Rect row = l.GetRect(30f);
            float sideBtnW = 110f;
            bool hasSecondary = !secondaryLeftLabel.NullOrEmpty() && onSecondaryLeft != null;

            // Expand / Collapse always own the right edge; left controls must not invade that zone.
            Rect collapseRect = new Rect(row.xMax - sideBtnW, row.y, sideBtnW, row.height);
            Rect expandRect = new Rect(collapseRect.x - 8f - sideBtnW, row.y, sideBtnW, row.height);
            float leftMax = expandRect.x - 8f;
            float leftBudget = Mathf.Max(80f, leftMax - row.x);

            float resetW;
            float secondaryW = 0f;
            if (hasSecondary)
            {
                secondaryW = 100f;
                resetW = 180f;
                float needed = resetW + 8f + secondaryW;
                if (needed > leftBudget)
                {
                    secondaryW = Mathf.Clamp(leftBudget * 0.35f, 70f, 100f);
                    resetW = Mathf.Max(90f, leftBudget - 8f - secondaryW);
                }
            }
            else
            {
                resetW = Mathf.Min(250f, leftBudget);
                if (resetW < 120f) resetW = Mathf.Max(90f, leftBudget * 0.55f);
            }

            Rect resetRect = new Rect(row.x, row.y, resetW, row.height);
            Rect secondaryRect = hasSecondary
                ? new Rect(resetRect.xMax + 8f, row.y, secondaryW, row.height)
                : default;

            // Hard clamp: never draw left buttons over Expand/Collapse
            if (hasSecondary && secondaryRect.xMax > leftMax)
            {
                secondaryW = Mathf.Max(0f, leftMax - secondaryRect.x);
                secondaryRect = new Rect(secondaryRect.x, secondaryRect.y, secondaryW, secondaryRect.height);
            }
            if (resetRect.xMax > leftMax)
                resetRect = new Rect(resetRect.x, resetRect.y, Mathf.Max(0f, leftMax - resetRect.x), resetRect.height);

            DrawRedButton(resetRect, resetLabel, onReset);
            if (hasSecondary && secondaryRect.width >= 70f && Widgets.ButtonText(secondaryRect, secondaryLeftLabel))
                onSecondaryLeft!();
            if (expandAll != null && Widgets.ButtonText(expandRect, "Expand all"))
                expandAll();
            if (collapseAll != null && Widgets.ButtonText(collapseRect, "Collapse all"))
                collapseAll();

            l.Gap(6f);
        }

        public static void DrawRedButton(Rect rect, string label, Action onClick)
        {
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.5f, 0.5f);
            if (Widgets.ButtonText(rect, label)) onClick();
            GUI.color = prev;
        }

        public static bool DrawSlateButtonWithIcon(Rect rect, string label, Texture2D? icon, Action? onClick = null)
        {
            Color prevColor = GUI.color;
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;

            bool mouseOver = Mouse.IsOver(rect);
            bool pressed = mouseOver && Input.GetMouseButton(0) && GUI.enabled;
            Color bg = !GUI.enabled
                ? new Color(SlateFill.r, SlateFill.g, SlateFill.b, 0.45f)
                : pressed ? SlatePress : mouseOver ? SlateHover : SlateFill;
            Widgets.DrawBoxSolid(rect, bg);
            GUI.color = mouseOver && GUI.enabled ? SlateOutlineHover : SlateOutline;
            Widgets.DrawBox(rect, 1);
            GUI.color = GUI.enabled ? Color.white : new Color(1f, 1f, 1f, 0.4f);

            float drawIcon = Mathf.Min(SlateIconSize, rect.height - 8f);
            float textLeft = rect.x + SlateIconPad;
            string text = label ?? "";

            if (icon != null && drawIcon > 4f)
            {
                Rect iconRect = new Rect(
                    textLeft,
                    rect.y + (rect.height - drawIcon) * 0.5f,
                    drawIcon,
                    drawIcon);
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
                textLeft = iconRect.xMax + SlateIconTextGap;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float labelW = Mathf.Max(0f, rect.xMax - textLeft - SlateIconPad);
            Widgets.Label(new Rect(textLeft, rect.y, labelW, rect.height), text.Truncate(labelW));

            bool clicked = GUI.enabled && Widgets.ButtonInvisible(rect);
            if (clicked && onClick != null) onClick();

            GUI.color = prevColor;
            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
            return clicked;
        }

        public static void DrawLabeledCheckbox(Listing_Standard l, string label, ref bool value, string? tip = null, Action? onChanged = null)
        {
            Rect row = l.GetRect(24f);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip);
            Widgets.Label(row, label);
            Rect checkRect = new Rect(row.xMax - 45f, row.y, 24f, 24f);
            bool prev = value;
            Widgets.Checkbox(checkRect.position, ref value);
            if (value != prev) onChanged?.Invoke();
        }

        /// <summary>Two side-by-side labeled int sliders. Min is capped by current max; max is floored by current min.</summary>
        public static void DrawDualIntSliders(
            Listing_Standard l,
            string leftLabel,
            ref int leftVal,
            string rightLabel,
            ref int rightVal,
            int min,
            int max,
            string? tip = null,
            float margin = 16f,
            float leftInset = 0f,
            float rightInset = 0f)
        {
            GameFont prevFont = Text.Font;
            TextAnchor prevAnchor = Text.Anchor;

            l.Gap(2f);
            Rect row = l.GetRect(24f);
            float inset = leftInset + rightInset;
            if (inset > 0f)
                row = new Rect(row.x + leftInset, row.y, Mathf.Max(40f, row.width - inset), row.height);
            if (!tip.NullOrEmpty()) TooltipHandler.TipRegion(row, tip);

            // Repair inverted saved/edited pairs before drawing.
            if (leftVal > rightVal)
            {
                int swap = leftVal;
                leftVal = rightVal;
                rightVal = swap;
            }
            leftVal = Mathf.Clamp(leftVal, min, max);
            rightVal = Mathf.Clamp(rightVal, min, max);

            float half = (row.width - margin) * 0.5f;
            Rect left = new Rect(row.x, row.y, half, row.height);
            Rect right = new Rect(row.x + half + margin, row.y, half, row.height);

            // Min may only move within [globalMin, currentMax]; max within [currentMin, globalMax].
            leftVal = DrawLabeledIntSliderInRect(left, leftLabel, leftVal, min, rightVal);
            rightVal = DrawLabeledIntSliderInRect(right, rightLabel, rightVal, leftVal, max);

            Text.Font = prevFont;
            Text.Anchor = prevAnchor;
        }

        private static int DrawLabeledIntSliderInRect(Rect rect, string label, int val, int min, int max)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            string text = $"{label}: {val.ToString().Colorize(Color.cyan)}";
            // Size label to content so the slider sits close beside it
            float labelW = Mathf.Min(Text.CalcSize($"{label}: {val}").x + 6f, rect.width * 0.65f);
            Widgets.Label(new Rect(rect.x, rect.y, labelW, rect.height), text);
            Text.Anchor = TextAnchor.UpperLeft;

            const float gap = 6f;
            float sliderH = 22f;
            Rect sliderRect = new Rect(
                rect.x + labelW + gap,
                rect.y + (rect.height - sliderH) * 0.5f,
                Mathf.Max(40f, rect.width - labelW - gap),
                sliderH);
            float f = Widgets.HorizontalSlider(sliderRect, val, min, max, middleAlignment: true, null, null, null, 1f);
            return Mathf.Clamp(Mathf.RoundToInt(f), min, max);
        }
    }
}
