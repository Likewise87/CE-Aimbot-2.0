using UnityEngine;
using Verse;

namespace TSA.CE_Aimbot
{
    /// <summary>All ContentFinder textures loaded on the main thread at startup.</summary>
    [StaticConstructorOnStartup]
    internal static class AimbotTextures
    {
        public static readonly Texture2D IconOn =
            ContentFinder<Texture2D>.Get("UI/Commands/AimbotOn", true);
        public static readonly Texture2D IconOff =
            ContentFinder<Texture2D>.Get("UI/Commands/AimbotOff", true);
        public static readonly Texture2D ViewDetails =
            ContentFinder<Texture2D>.Get("UI/Commands/View_Details", false);

        // CE UI button icons (may be null if CE textures are missing)
        public static readonly Texture2D Aimed =
            ContentFinder<Texture2D>.Get("UI/Buttons/AimedShot", false);
        public static readonly Texture2D Snapshot =
            ContentFinder<Texture2D>.Get("UI/Buttons/Snapshot", false);
        public static readonly Texture2D Suppress =
            ContentFinder<Texture2D>.Get("UI/Buttons/SuppressFire", false);
        public static readonly Texture2D Single =
            ContentFinder<Texture2D>.Get("UI/Buttons/SingleFire", false);
        public static readonly Texture2D Burst =
            ContentFinder<Texture2D>.Get("UI/Buttons/BurstFire", false);
        public static readonly Texture2D FullAuto =
            ContentFinder<Texture2D>.Get("UI/Buttons/AutoFire", false);
    }
}
