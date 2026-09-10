using RimWorld;
using UnityEngine;
using Verse;

namespace TSA.CE_Aimbot
{
    /// <summary>
    /// Per-pawn toggle gizmo builder. Shown by our GetGizmos patch.
    /// </summary>
    public static class Gizmo_PawnToggle
    {
        public static Command_Toggle Build(Pawn pawn)
        {
            if (pawn == null) return null;

            var store = PawnAutoAimStore.Instance;
            if (store == null) return null;

            bool enabled = store.IsEnabledFor(pawn);
            return new Command_Toggle
            {
                defaultLabel = "CE Aimbot",
                defaultDesc = "Automatically selects aim and fire modes by your settings.",
                icon = enabled ? AimbotTextures.IconOn : AimbotTextures.IconOff,
                isActive = () => PawnAutoAimStore.Instance?.IsEnabledFor(pawn) ?? false,
                toggleAction = () =>
                {
                    var s = PawnAutoAimStore.Instance;
                    if (s == null) return;
                    if (s.IsEnabledFor(pawn)) s.DisableFor(pawn);
                    else s.EnableFor(pawn);
                }
            };
        }
    }
}
