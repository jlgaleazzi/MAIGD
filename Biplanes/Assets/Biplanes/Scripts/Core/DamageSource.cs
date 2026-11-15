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
        Heal,         // Health balloon restore
        // Appended last so the existing values keep their ordinals.
        ZeppelinTurret, // Flak shell fired by a zeppelin turret (player scales it by damageTaken.zeppelinTurret)
        PlaneCollision  // Mid-air contact with an enemy fighter/bomber (player scales it by damageTaken.planeCollision; fatal for enemies)
    }
}
