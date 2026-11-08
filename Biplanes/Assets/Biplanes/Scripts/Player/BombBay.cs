using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Player {

  /// <summary>Manages the player's bomb loadout: capacity, remaining count,
  /// and spawning bombs via the shared Bomb pool. Starts each mission full.</summary>
  public class BombBay : MonoBehaviour {

    [SerializeField] private int _capacity = 7;
    [SerializeField] private int _remaining = 7;

    public int BombsLeft => _remaining;
    public event System.Action<int> OnBombsChanged;

    void Start() {
      _remaining = _capacity;
    }

    /// <summary>Drop one bomb at <paramref name="position"/> inheriting the
    /// plane's velocity. Returns false when no bombs remain.</summary>
    public bool TryDropBomb(Vector3 position, Vector2 parentVelocity) {
      if (_remaining <= 0) return false;
      _remaining--;
      Weapons.Bomb.Spawn(position, parentVelocity);
      OnBombsChanged?.Invoke(_remaining);
      Biplanes.Core.GameEvents.RaiseBombsChanged(_remaining);
      return true;
    }

    /// <summary>Restore all bombs to full capacity (e.g. health-balloon pickup).</summary>
    public void Refill() {
      _remaining = _capacity;
    }
  }
}
