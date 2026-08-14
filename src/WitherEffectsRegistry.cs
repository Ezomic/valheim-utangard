using UnityEngine;

namespace Wither
{
    /// <summary>
    /// Builds and hands out the two status effect prototypes.
    ///
    /// These are ScriptableObjects made at runtime rather than assets in a bundle, which is
    /// the whole reason this mod is a single DLL. SEMan clones whatever you hand it
    /// (MemberwiseClone, in AddStatusEffect) and never looks the prototype up in ObjectDB,
    /// so there is nothing to register - the effects only have to exist.
    /// </summary>
    internal static class WitherEffectsRegistry
    {
        /// <summary>
        /// Prefab-style names, because NameHash is taken from them and SEMan keys on that
        /// hash. Prefixed so they cannot collide with a vanilla effect or another mod's.
        /// </summary>
        private const string MarkerName = "Wither_Denied";
        private const string SappedName = "Wither_Sapped";

        public static readonly int MarkerHash = MarkerName.GetStableHashCode();
        public static readonly int SappedHash = SappedName.GetStableHashCode();

        private static WitherMarker _marker;
        private static SappedEffect _sapped;

        private static Sprite _markerIcon;
        private static Sprite _sappedIcon;

        /// <summary>
        /// Build the prototypes and re-borrow their icons. Safe to call repeatedly - the
        /// objects are made once and only the borrowed sprites are refreshed, because those
        /// come out of an ObjectDB that gets replaced wholesale when you join a server.
        /// </summary>
        public static void Build()
        {
            if (_marker == null)
            {
                _marker = ScriptableObject.CreateInstance<WitherMarker>();
                _marker.name = MarkerName;
                _marker.m_name = "Denied";
                _marker.m_tooltip =
                    "This land does not recognise you. Nothing you eat or drink will take "
                    + "hold here, and what you carry is burning away.";

                // Nothing in the game owns this object, so without the flag a scene load can
                // collect it and every player quietly stops being gated.
                _marker.hideFlags = HideFlags.HideAndDontSave;
            }

            if (_sapped == null)
            {
                _sapped = ScriptableObject.CreateInstance<SappedEffect>();
                _sapped.name = SappedName;
                _sapped.m_name = "Sapped";
                _sapped.m_tooltip =
                    "The land took something with it. Stamina returns slowly until this "
                    + "wears off.";
                _sapped.hideFlags = HideFlags.HideAndDontSave;
            }

            _markerIcon = BorrowIcon(WitherConfig.WitherIconFrom.Value);
            _sappedIcon = BorrowIcon(WitherConfig.SappedIconFrom.Value);
        }

        /// <summary>
        /// Make sure this player is carrying both effects, and keep their icons in step with
        /// the config.
        ///
        /// The icon is set on the live clone rather than only on the prototype because
        /// SEMan.GetHUDStatusEffects skips any effect whose icon is null - that null is how
        /// ShowStatusEffects hides them, and it has to be able to change its mind without
        /// waiting for the effect to expire and come back.
        /// </summary>
        public static void Ensure(Player player)
        {
            SEMan seman = player.GetSEMan();
            if (seman == null) return;

            bool show = WitherConfig.ShowStatusEffects.Value;

            // The marker is signage and nothing else, so when signage is off it does not
            // need to exist at all. Sapped is a mechanic and is applied either way.
            if (show) Apply(seman, MarkerHash, _marker, _markerIcon);

            Apply(seman, SappedHash, _sapped, show ? _sappedIcon : null);
        }

        private static void Apply(SEMan seman, int hash, StatusEffect prototype, Sprite icon)
        {
            if (prototype == null) return;

            StatusEffect live = seman.GetStatusEffect(hash);
            if (live == null)
            {
                prototype.m_icon = icon;
                live = seman.AddStatusEffect(prototype);
            }

            if (live != null) live.m_icon = icon;
        }

        /// <summary>
        /// Take a vanilla effect's sprite.
        ///
        /// Borrowing rather than shipping a png is the same argument as borrowing a material:
        /// it matches the art by construction and survives a game update. The cost is that
        /// two effects on the HUD wear a face that belongs to something else, which is worth
        /// a hand-drawn icon eventually and is one config field away in the meantime.
        /// </summary>
        private static Sprite BorrowIcon(string donorName)
        {
            if (string.IsNullOrEmpty(donorName)) return null;

            ObjectDB db = ObjectDB.instance;
            if (db == null) return null;

            foreach (StatusEffect effect in db.m_StatusEffects)
            {
                if (effect != null && effect.name == donorName)
                {
                    if (effect.m_icon == null)
                        WitherPlugin.Log.LogWarning(
                            "Icon donor '" + donorName + "' exists but has no sprite.");
                    return effect.m_icon;
                }
            }

            // Logged and skipped rather than thrown: a bad donor name should cost you an
            // icon, not a working mod.
            WitherPlugin.Log.LogWarning(
                "Icon donor '" + donorName + "' not found in ObjectDB - that effect will be "
                + "invisible on the HUD. Turn on Diagnostics.LogBlockedEffects for names.");
            return null;
        }
    }
}
