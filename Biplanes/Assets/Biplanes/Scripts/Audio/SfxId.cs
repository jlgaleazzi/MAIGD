namespace Biplanes.Audio {

    /// <summary>
    /// One value per sound clip (ARCHITECTURE §3 Audio, 23 clips). The clip for each id is
    /// Resources/Audio/sfx/&lt;AudioMix.ClipName(id)&gt;, the snake_case form of the name
    /// (ExplosionBig -> explosion_big). Append new ids at the end and add their name to
    /// AudioMix.ClipName and AudioMix.Priority.
    /// </summary>
    public enum SfxId {
        EngineLoop,
        PlayerGun,
        BombRelease,
        BombImpact,
        HitEnemy,
        HitPlayer,
        EnemyGun,
        FlakFire,
        FlakBurst,
        Explosion,
        ExplosionBig,
        ZeppelinHum,
        SmokeCrackle,
        Crash,
        Pickup,
        DamageWarning,
        UiClick,
        UiPage,
        UiPause,
        Typewriter,
        Stamp,
        Win,
        Lose
    }
}
