using System.Collections.Generic;
using UnityEngine;

namespace Biplanes.Weapons {

    /// <summary>
    /// Generic, allocation-free ObjectPool for MonoBehaviour-derived game objects.
    /// Pre-fills its internal list during Awake by instantiating `count` items
    /// from the provided factory. All items start deactivated and are recycled
    /// via Push / Pop — never Instantiate or Destroy at runtime.
    /// </summary>
    public class ObjectPool<T> where T : Component {

        private readonly Queue<T> _pool;
        private readonly Transform _parent;
        private int _capacity;

        /// <summary>Total number of objects ever instantiated for this pool.</summary>
        public int Capacity => _capacity;

        /// <summary>How many items are currently available (not in use).</summary>
        public int Available => _pool.Count;

        /// <summary>Creates a new pool, pre-filling it with `count` prefabricated objects.</summary>
        /// <param name="factory">A lambda that returns an active, instantiated GameObject's component.</param>
        /// <param name="count">Initial pool size (default 20 — enough for most weapon bursts).</param>
        /// <param name="parent">Optional transform parent for organisation in the scene hierarchy.</param>
        public ObjectPool(System.Func<T> factory, int count = 20, Transform parent = null) {
            _pool = new Queue<T>(count);
            _capacity = 0;

            GameObject poolRoot = new GameObject($"Pool<{typeof(T).Name}>") {
                hideFlags = HideFlags.HideAndDontSave
            };
            if (parent != null) {
                poolRoot.transform.SetParent(parent, false);
            }
            _parent = poolRoot.transform;

            for (int i = 0; i < count; i++) {
                T item = factory();
                item.gameObject.SetActive(false);
                _pool.Enqueue(item);
                _capacity++;
            }
        }

        /// <summary>Retrieve an active object from the pool. Returns null if pool is empty and auto-expand is disabled.</summary>
        public T Pop() {
            if (_pool.Count == 0) {
                Debug.LogWarning($"Pool<{typeof(T).Name}> exhausted — consider increasing initial capacity.");
                return null;
            }

            T item = _pool.Dequeue();
            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>Return an object to the pool for reuse. If the caller wants no limit on expansion, set `allowExpansion` true.</summary>
        public void Push(T item) {
            if (item == null) return;

            item.gameObject.SetActive(false);
            item.transform.SetParent(_parent, false);
            _pool.Enqueue(item);
        }

        /// <summary>Force-expand the pool by `count` more items (used when Pop() hits zero).</summary>
        public void Expand(System.Func<T> factory, int count) {
            for (int i = 0; i < count; i++) {
                T item = factory();
                item.gameObject.SetActive(false);
                _pool.Enqueue(item);
                _capacity++;
            }
        }

        /// <summary>Clean debug log of current pool stats.</summary>
        public void DebugInfo() {
            Debug.Log($"Pool<{typeof(T).Name}> capacity={_capacity} available={Available}");
        }

        /// <summary>Non-generic push for use by pooled-item subclasses that store the pool as object.</summary>
        public void PushBase(object item) => Push((T)item);
    }
}
