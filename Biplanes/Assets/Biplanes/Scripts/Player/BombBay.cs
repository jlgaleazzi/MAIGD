using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Player {

  /// <summary>Manages the player's bomb loadout: capacity, remaining count,
  /// and spawning bombs via the shared Bomb pool. Starts each mission full.
  /// Every change of the count is raised on GameEvents.BombsChanged, including the
  /// starting count in Start (B022), so the HUD is right without waiting for a drop.</summary>
  public class BombBay : MonoBehaviour {

    /// <summary>Used only when Balance.json can't be read (GDD: 7 bombs per mission).</summary>
    private const int FallbackCapacity = 7;

    [SerializeField] private int _capacity = FallbackCapacity;
    [SerializeField] private int _remaining = FallbackCapacity;

    public int BombsLeft => _remaining;
    public int Capacity => _capacity;
    public event System.Action<int> OnBombsChanged;

    void Awake() {
      var g = GameBalance.Get();
      if (g != null && g.player != null && g.player.bombCapacity > 0) _capacity = g.player.bombCapacity;
      _remaining = _capacity;
    }

    void Start() {
      // Runs after the HUD subscribed (MissionRunner builds the plane, then the HUD, in the
      // same frame), so the HUD sees the full bay from the first frame.
      _remaining = _capacity;
      RaiseChanged();
    }

    /// <summary>Drop one bomb at <paramref name="position"/> inheriting the
    /// plane's velocity. Returns false when no bombs remain.</summary>
    public bool TryDropBomb(Vector3 position, Vector2 parentVelocity) {
      if (_remaining <= 0) return false;
      _remaining--;
      Weapons.Bomb.Spawn(position, parentVelocity);
      RaiseChanged();
      return true;
    }

    /// <summary>Restore all bombs to full capacity (e.g. health-balloon pickup).</summary>
    public void Refill() {
      _remaining = _capacity;
      RaiseChanged();
    }

    private void RaiseChanged() {
      OnBombsChanged?.Invoke(_remaining);
      GameEvents.RaiseBombsChanged(_remaining);
    }
  }
}
