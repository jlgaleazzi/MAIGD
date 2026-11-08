TASK: T077
AGENT: mechanics
STATUS: DONE
FILES:
  - Biplanes/Assets/Biplanes/Scripts/Player/PlayerPlane.cs
  - Biplanes/Assets/Biplanes/Scripts/Weapons/Bomb.cs
SUMMARY: PlayerPlane takes ApplyDamage(1f, Collision) (= 100% via damageTaken.collision) when its collider
         bottom <= TerrainStrip.HeightAt, except on the runway; it also climbs automatically for 0.6 s after
         takeoff so a D-only takeoff doesn't scrape. Bomb explodes (FxManager) and recycles on reaching HeightAt.
VERIFY: tools/unity.sh compile. In play: fly into the ground -> Lost; drop a bomb -> burst at the ground line.
REQUESTS: none
