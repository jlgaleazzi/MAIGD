using System;
using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Team affiliation used by collision and projectile logic.
    /// Avoids Unity tags / physics layers entirely (ARCHITECTURE §2 rule 3).
    /// </summary>
    public enum Team { Player, Enemy, Neutral }

    /// <summary>
    /// Lightweight MonoBehaviour component that marks the team of any
    /// entity (player plane, enemy, projectile, obstacle, etc.).
    ///
    /// Used by:
    ///  - Projectile to skip own-team projectiles.
    ///  - Collision / damage logic to identify friend vs foe.
    /// </summary>
    public class TeamTag : MonoBehaviour {

        /// <summary>The team this entity belongs to.</summary>
        [SerializeField] public Team team = Team.Neutral;

        public Team Team => team;
    }
}
