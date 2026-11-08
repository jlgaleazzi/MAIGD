using System;
using UnityEngine;

namespace Biplanes.Core {

    /// <summary>
    /// Damage contract for any game entity that can take damage.
    /// Health is the primary implementation; every hittable object
    /// (player plane, enemy, projectile, health balloon) implements it.
    /// </summary>
    public interface IDamageable {
        Team Team { get; }
        void ApplyDamage(float percent, DamageSource src);
        bool IsDead { get; }
        void Heal(float amount);
    }
}
