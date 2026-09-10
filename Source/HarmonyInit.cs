using HarmonyLib;
using Verse;

namespace TSA.CE_Aimbot
{
    [StaticConstructorOnStartup]
    public static class HarmonyInit
    {
        static HarmonyInit()
        {
            var h = new Harmony("TSA.CE.Aimbot");
            h.PatchAll();
            Log.Message("[TSA.CE.Aimbot] Harmony patches applied.");
        }
    }
}
