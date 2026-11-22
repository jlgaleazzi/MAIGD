TASK: A-GUN
AGENT: art
STATUS: DONE
FILES: tools/synth_sfx.py, Biplanes/Assets/Biplanes/Resources/Audio/sfx/player_gun.wav, Biplanes/Assets/Biplanes/Resources/Audio/sfx/enemy_gun.wav, Biplanes/Assets/Biplanes/Resources/Audio/sfx/hit_enemy.wav, docs/ART_STYLE.md (Sound section), docs/crew/handoffs/A-GUN-art.md
SUMMARY: Diagnosis: the old player_gun was mostly a "receiver ring" of 3 pure decaying sines at 2.25/3.42/5.15 kHz over a high-passed crack, with a weak thump (gain 0.2, tau 10 ms), so the report showed low 9.6% / mid 54.7% / high 35.7%: a thin struck-tin ping with no body. hit_enemy (plays on every autofire hit) used the same kind of pure sines at 3.25/4.75 kHz (high 63.4%, ring 16.4%), and enemy_gun also rang at 1.35/2.05/2.9 kHz.
New shared gun_shot() uses noise and a falling sine only, with no ringing modes. It layers a 1.5 ms broadband crack, a wide 1.1 kHz noise bark, a powder thump (150 -> 75 Hz sine + 260 Hz low-passed noise, tau 16 ms, soft tanh), a dull 650 Hz bolt clack and belt clack (34/56 ms) and a low-passed air tail. Player 0.15 s; enemy_gun 0.2 s, lower (115 -> 55 Hz, 480 Hz clack, 2.2 kHz low-pass, echoes) and mastered at -6 dBFS for distance. hit_enemy is now a noise tick + small thup + canvas rip.
Report changes: a ring% column (energy above 2 kHz left after 15 ms) and a 1 s / 8 shots/s autofire mix peak per gun (an error if above -0.5 dBFS). Other 20 clips byte-identical (md5 before/after).
VERIFY: python tools/synth_sfx.py --check  (expect 0 errors, exit 0; check the player_gun/enemy_gun/hit_enemy rows and the two "autofire" lines below the table)
REQUESTS: none

NUMBERS (before -> after; low <300 Hz / mid / high >3 kHz energy %, ring% = >2 kHz energy after 15 ms)
- player_gun: 0.13 s, rms -24.4, 9.6/54.7/35.7, ring 2.9  ->  0.15 s, peak -3.0, rms -23.4, 66.9/28.8/4.3, ring 0.7; autofire 1 s @ 8/s mix peak -3.0 dBFS (shots don't stack: the 0.125 s spacing falls in the previous shot's quiet tail)
- enemy_gun: 0.2 s, -3 dBFS, rms -20.9, 46.0/53.3/0.7, ring 0.7  ->  0.2 s, peak -6.0, rms -21.8, 72.6/27.3/0.1, ring 0.2; autofire mix peak -5.9 dBFS
- hit_enemy: rms -20.2, 0.0/36.6/63.4, ring 16.4  ->  rms -17.6, 13.4/73.8/12.8, ring 12.1 (the remainder is the grain-modulated canvas-rip noise, not a tonal ring; it stays lighter than hit_player at 68.4% low)

ROUNDS (I can't listen, so each change is judged on the numbers)
1. First noise-only recipe: low 91.8 / high 0.0. Way too much thud: the tanh-saturated thump swamped the crack. Fixed by making the crack longer and adding a mid-band bark layer.
2. Low was still 89.7. Swept the layer gains: thump gain 1.0 -> 0.22 and drive 1.6 -> 1.0 gave player low 67 / high 4. Lowering the crack 1.0 -> 0.6 raised the RMS from -24.0 to -23.4 without making it duller.
3. At -3 dBFS, enemy_gun's RMS (-18.4) was 5.6 dB above the player's (-23.4). Its echoes and low-pass spread the energy, so it would read as louder and closer. Fixed by lowering its body and tail gains and mastering it at -6 dBFS (rms -21.8; it is also mostly <300 Hz, which the ear weights down). hit_enemy took 3 rounds too: thup 0.35 -> 0.15 (low had reached 40%, too close to hit_player), then the rip centre 2.4 -> 1.7 kHz (high 19 -> 13%).

NOTES
- enemy_gun now peaks at -6 dBFS, not -3 (documented in ART_STYLE.md Format). If code sets enemy-gun volume on top of this, the orchestrator may want to recheck the mix in play.
- Side effect: to get a baseline report I briefly ran `git stash` / `stash pop`. That rewrote about 18 unrelated working-copy text files (e.g. TerrainStrip.cs, PlayerPlane.cs, flak_burst_*.svg, gen_pencil_fx.py, ART_STYLE.md, mission_0x.md) from CRLF to LF line endings. Their content is unchanged, `.gitattributes` already requires LF, and no WAV or PNG was touched (checksums verified). No .meta files were written.
