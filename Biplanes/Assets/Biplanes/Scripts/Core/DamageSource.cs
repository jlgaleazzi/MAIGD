using System;
using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Origin of a damage event. Used by projectiles, terrain contact,
    /// and health calculations to apply the correct damage value.
    /// </summary>
    [Serializable]
    public enum DamageSource {
        Bullet,   // Player machine-gun bullet
        Bomb,     // Dropped bomb (ground attack)
        Flak,         // AAA flak shell
        Collision,    // Terrain / building crash
        Heal          // Health balloon restore
    }
}
