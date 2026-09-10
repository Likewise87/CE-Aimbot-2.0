using System;
using CombatExtended;
using Verse;

namespace TSA.CE_Aimbot
{
    /// <summary>
    /// Applies CE aim/fire modes via CompFireModes.
    /// Enums match CombatExtended: AimMode { AimedShot, SuppressFire, Snapshot },
    /// FireMode { AutoFire, BurstFire, SingleFire }.
    /// </summary>
    internal static class CE_Reflection
    {
        internal static AimMode ToCeAim(AimOption aim) => aim switch
        {
            AimOption.Aimed => AimMode.AimedShot,
            AimOption.Suppress => AimMode.SuppressFire,
            _ => AimMode.Snapshot
        };

        internal static FireMode ToCeFire(FireOption fire) => fire switch
        {
            FireOption.Burst => FireMode.BurstFire,
            FireOption.FullAuto => FireMode.AutoFire,
            _ => FireMode.SingleFire
        };

        /// <summary>Read current modes. Returns false if CompFireModes is missing.</summary>
        internal static bool TryGetCurrent(ThingWithComps weapon, out AimMode aim, out FireMode fire)
        {
            aim = AimMode.Snapshot;
            fire = FireMode.SingleFire;
            try
            {
                var comp = weapon?.TryGetComp<CompFireModes>();
                if (comp == null) return false;
                aim = comp.CurrentAimMode;
                fire = comp.CurrentFireMode;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Apply desired modes only when they differ (avoids stance dip).</summary>
        internal static void Apply(ThingWithComps weapon, AimOption aimOpt, FireOption fireOpt)
        {
            try
            {
                var comp = weapon?.TryGetComp<CompFireModes>();
                if (comp == null) return;

                AimMode aim = ToCeAim(aimOpt);
                FireMode fire = ToCeFire(fireOpt);

                if (comp.CurrentAimMode != aim)
                    comp.CurrentAimMode = aim;
                if (comp.CurrentFireMode != fire)
                    comp.CurrentFireMode = fire;
            }
            catch (Exception e)
            {
                Log.Warning($"[TSA.CE_Aimbot] Failed to apply fire/aim: {e}");
            }
        }
    }
}
