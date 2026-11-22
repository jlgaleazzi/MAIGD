using System.Collections.Generic;
using Biplanes.Core;

namespace Biplanes.Enemies {

    /// <summary>
    /// Attack slots (B060, ARCHITECTURE §3 "Fighter chase"): at most `maxAttackers` fighters
    /// may fire at the player at any moment, so a chasing pack can't shred the player.
    ///
    /// • Every fighter reports each gun tick (Report): whether it could shoot now (nose on the
    ///   player, in range, on screen, not turning) and its distance to the player.
    /// • A fighter that could shoot asks for a slot (TryAcquire). Free slots go nearest-first:
    ///   it gets one only if fewer eligible waiting fighters (reported within FreshSeconds) are
    ///   closer than it than there are free slots. A holder is never pre-empted while it can
    ///   shoot.
    /// • A slot is released when its holder dies or is removed (Release, or its Unity object
    ///   is destroyed), re-enters (Release), can't shoot for longer than `graceSeconds`
    ///   (rotation: no starvation), or the player dies (ReleaseAll), and on a new target
    ///   (BindTarget: a new player = a new mission or Retry) everything resets.
    /// • Each slot remembers its last shot; a fighter taking over a slot waits until
    ///   `minShotGap` after that shot (the wait from TryAcquire), so handing slots round never
    ///   raises the fire rate above maxAttackers shots per fighter interval.
    ///
    /// Pure C# with explicit time, so EditMode tests drive their own instances; the game uses
    /// Shared (reset on play start without a domain reload, see FighterAI).
    /// </summary>
    public sealed class FighterAttackSlots {

        /// <summary>A waiting fighter counts as competing for a slot only if it was eligible
        /// within this many seconds (a few fixed steps).</summary>
        public const float FreshSeconds = 0.1f;

        /// <summary>The coordinator every FighterAI uses.</summary>
        public static readonly FighterAttackSlots Shared = new FighterAttackSlots();

        private sealed class Entry {
            public object Key;
            public bool Eligible;
            public float Distance;
            public float LastReport;
            public float LastEligible;
            public int Slot = -1;
            public int Order;
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private Entry[] _holders;
        private float[] _lastShot;
        private float _grace = FighterChaseData.DefaultSlotGraceSeconds;
        private object _target;
        private int _nextOrder;

        public FighterAttackSlots() : this(FighterChaseData.DefaultMaxAttackers, FighterChaseData.DefaultSlotGraceSeconds) { }

        public FighterAttackSlots(int maxAttackers, float graceSeconds) {
            Configure(maxAttackers, graceSeconds);
        }

        /// <summary>Slots available (≥ 1).</summary>
        public int MaxAttackers => _holders.Length;

        /// <summary>Seconds a holder may be unable to shoot before it loses its slot.</summary>
        public float GraceSeconds => _grace;

        /// <summary>Slots currently held.</summary>
        public int HolderCount {
            get {
                int n = 0;
                for (int i = 0; i < _holders.Length; i++) if (_holders[i] != null) n++;
                return n;
            }
        }

        /// <summary>Fighters currently reporting (holders and waiting).</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>
        /// Sets the slot count (min 1) and grace (≥ 0). A different slot count resets all
        /// state; the same values change nothing (safe to call every tick).
        /// </summary>
        public void Configure(int maxAttackers, float graceSeconds) {
            int max = maxAttackers < 1 ? 1 : maxAttackers;
            _grace = graceSeconds < 0f ? 0f : graceSeconds;
            if (_holders != null && _holders.Length == max) return;
            _holders = new Entry[max];
            _lastShot = new float[max];
            ClearState();
        }

        /// <summary>The player the slots are for. A different target (a new mission's player,
        /// Retry) resets every slot, so nothing carries over.</summary>
        public void BindTarget(object target) {
            if (ReferenceEquals(target, _target)) return;
            Reset();
            _target = target;
        }

        /// <summary>
        /// One gun-tick report from `key`: `eligible` = it could shoot right now (nose on the
        /// target and in range), `distance` = its distance to the target. A holder that hasn't
        /// been eligible for more than GraceSeconds loses its slot here.
        /// </summary>
        public void Report(object key, bool eligible, float distance, float now) {
            if (key == null) return;
            Prune(now);
            var e = Find(key);
            if (e == null) {
                e = new Entry { Key = key, Order = _nextOrder++, LastEligible = float.NegativeInfinity };
                _entries.Add(e);
            }
            e.Eligible = eligible;
            e.Distance = distance;
            e.LastReport = now;
            if (eligible) e.LastEligible = now;
            if (e.Slot >= 0 && now - e.LastEligible > _grace) FreeSlot(e);
        }

        /// <summary>
        /// True when `key` holds a slot or gets one now. It must have reported eligible (Report)
        /// this tick. `wait` is how long it must still hold fire when it has just taken over a
        /// slot whose last shot was less than `minShotGap` ago (0 otherwise).
        /// </summary>
        public bool TryAcquire(object key, float now, float minShotGap, out float wait) {
            wait = 0f;
            if (key == null) return false;
            Prune(now);
            var self = Find(key);
            if (self == null) return false;
            if (self.Slot >= 0) return true;
            if (!self.Eligible) return false;

            int free = 0;
            for (int i = 0; i < _holders.Length; i++) if (_holders[i] == null) free++;
            if (free == 0) return false;

            // Nearest first: the eligible waiting fighters closer than this one go before it.
            int closer = 0;
            for (int i = 0; i < _entries.Count; i++) {
                var e = _entries[i];
                if (e == self || e.Slot >= 0 || !e.Eligible || now - e.LastEligible > FreshSeconds) continue;
                if (e.Distance < self.Distance || (e.Distance == self.Distance && e.Order < self.Order)) closer++;
            }
            if (closer >= free) return false;

            // Take the free slot that has rested longest.
            int best = -1;
            for (int i = 0; i < _holders.Length; i++) {
                if (_holders[i] != null) continue;
                if (best < 0 || _lastShot[i] < _lastShot[best]) best = i;
            }
            _holders[best] = self;
            self.Slot = best;
            float ready = _lastShot[best] + (minShotGap > 0f ? minShotGap : 0f);
            wait = ready > now ? ready - now : 0f;
            return true;
        }

        /// <summary>True when `key` holds a slot now.</summary>
        public bool Holds(object key) {
            var e = Find(key);
            return e != null && e.Slot >= 0;
        }

        /// <summary>Records a shot by `key` on its slot (the hand-over gap starts from it).</summary>
        public void NotifyShot(object key, float now) {
            var e = Find(key);
            if (e != null && e.Slot >= 0) _lastShot[e.Slot] = now;
        }

        /// <summary>Forgets `key` and frees its slot (death, removal, re-entry).</summary>
        public void Release(object key) {
            var e = Find(key);
            if (e == null) return;
            FreeSlot(e);
            _entries.Remove(e);
        }

        /// <summary>Frees every slot and forgets every fighter (the player died). The slots'
        /// last-shot times stay.</summary>
        public void ReleaseAll() {
            for (int i = 0; i < _holders.Length; i++) _holders[i] = null;
            _entries.Clear();
        }

        /// <summary>Clears everything, including the target and the last-shot times.</summary>
        public void Reset() {
            ClearState();
            _target = null;
        }

        private void ClearState() {
            _entries.Clear();
            for (int i = 0; i < _holders.Length; i++) {
                _holders[i] = null;
                _lastShot[i] = float.NegativeInfinity;
            }
            _nextOrder = 0;
        }

        private Entry Find(object key) {
            if (key == null) return null;
            for (int i = 0; i < _entries.Count; i++) {
                if (ReferenceEquals(_entries[i].Key, key)) return _entries[i];
            }
            return null;
        }

        private void FreeSlot(Entry e) {
            if (e.Slot >= 0 && e.Slot < _holders.Length && _holders[e.Slot] == e) _holders[e.Slot] = null;
            e.Slot = -1;
        }

        /// <summary>Drops destroyed fighters and ones that stopped reporting, and frees the
        /// slots of holders that couldn't shoot for longer than the grace.</summary>
        private void Prune(float now) {
            float stale = _grace + FreshSeconds;
            for (int i = _entries.Count - 1; i >= 0; i--) {
                var e = _entries[i];
                bool destroyed = e.Key is UnityEngine.Object o && o == null;
                if (destroyed || now - e.LastReport > stale) {
                    FreeSlot(e);
                    _entries.RemoveAt(i);
                    continue;
                }
                if (e.Slot >= 0 && now - e.LastEligible > _grace) FreeSlot(e);
            }
        }
    }
}
