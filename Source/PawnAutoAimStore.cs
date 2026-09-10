using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TSA.CE_Aimbot
{
    /// <summary>
    /// Global store for the per-pawn "CE Aimbot" toggle.
    /// Default follows mod setting; we track only explicit per-pawn overrides.
    /// Bound to the current Game so a stale static cannot outlive a quit-to-menu / new game.
    /// </summary>
    public class PawnAutoAimStore : GameComponent
    {
        private static PawnAutoAimStore? _instance;
        private static Game? _boundGame;

        // When GizmoEnabledByDefault is true, track pawns the player turned off.
        private HashSet<string> disabledPawnIds = new HashSet<string>();
        // When GizmoEnabledByDefault is false, track pawns the player turned on.
        private HashSet<string> enabledPawnIds = new HashSet<string>();

        public PawnAutoAimStore() { } // for scribing

        public PawnAutoAimStore(Game game)
        {
            Bind(this, game);
        }

        public static PawnAutoAimStore? Instance
        {
            get
            {
                var game = Current.Game;
                if (game == null)
                {
                    _instance = null;
                    _boundGame = null;
                    return null;
                }

                if (_instance != null && _boundGame == game)
                    return _instance;

                _instance = game.GetComponent<PawnAutoAimStore>();
                if (_instance == null)
                {
                    _instance = new PawnAutoAimStore(game);
                    game.components.Add(_instance);
                }
                else
                {
                    Bind(_instance, game);
                }

                return _instance;
            }
        }

        private static void Bind(PawnAutoAimStore store, Game game)
        {
            _instance = store;
            _boundGame = game;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref disabledPawnIds, "disabledPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref enabledPawnIds, "enabledPawnIds", LookMode.Value);
            if (disabledPawnIds == null) disabledPawnIds = new HashSet<string>();
            if (enabledPawnIds == null) enabledPawnIds = new HashSet<string>();

            // Scribe-created components never hit the Game ctor; rebind after load.
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (Current.Game != null)
                    Bind(this, Current.Game);
            }
        }

        private static bool DefaultEnabled => Mod_Aimbot.Settings?.GizmoEnabledByDefault ?? true;

        public void ClearOverrides()
        {
            disabledPawnIds.Clear();
            enabledPawnIds.Clear();
        }

        public bool IsEnabledFor(Pawn pawn)
        {
            if (pawn == null) return false;
            var id = pawn.GetUniqueLoadID();
            if (string.IsNullOrEmpty(id)) return false;
            return DefaultEnabled
                ? !disabledPawnIds.Contains(id)
                : enabledPawnIds.Contains(id);
        }

        public void EnableFor(Pawn pawn)
        {
            if (pawn == null) return;
            var id = pawn.GetUniqueLoadID();
            if (string.IsNullOrEmpty(id)) return;
            if (DefaultEnabled)
                disabledPawnIds.Remove(id);
            else
                enabledPawnIds.Add(id);
        }

        public void DisableFor(Pawn pawn)
        {
            if (pawn == null) return;
            var id = pawn.GetUniqueLoadID();
            if (string.IsNullOrEmpty(id)) return;
            if (DefaultEnabled)
                disabledPawnIds.Add(id);
            else
                enabledPawnIds.Remove(id);
        }

        public void SetEnabled(Pawn pawn, bool enabled)
        {
            if (enabled) EnableFor(pawn);
            else DisableFor(pawn);
        }
    }
}
