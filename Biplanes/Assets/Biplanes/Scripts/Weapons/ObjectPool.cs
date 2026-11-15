using System.Collections.Generic;
using UnityEngine;

namespace Biplanes.Weapons {

    /// <summary>
    /// Generic, allocation-free ObjectPool for MonoBehaviour-derived game objects.
    /// Pre-fills its internal list on construction by instantiating `count` items
    /// from the provided factory. All items start deactivated and are recycled
    /// via Push / Pop — never Instantiate or Destroy at runtime.
    ///
    /// Every item lives under one hidden pool root for its whole life. Dispose destroys the
    /// root and every item (pooled or in flight), so a pool rebuilt per mission frees the
    /// previous one instead of leaking it (B006a).
    /// </summary>
    public class ObjectPool<T> where T : Component {

        private readonly Queue<T> _pool;
        private readonly List<T> _items;
        private readonly Transform _parent;
        private readonly GameObject _root;
        private int _capacity;
        private bool _disposed;

        /// <summary>Total number of objects ever instantiated for this pool.</summary>
        public int Capacity => _capacity;

        /// <summary>How many items are currently available (not in use).</summary>
        public int Available => _pool.Count;

        /// <summary>True once Dispose has run; the pool then hands out nothing.</summary>
        public bool IsDisposed => _disposed;

        /// <summary>Creates a new pool, pre-filling it with `count` prefabricated objects.</summary>
        /// <param name="factory">A lambda that returns an active, instantiated GameObject's component.</param>
        /// <param name="count">Initial pool size (default 20 — enough for most weapon bursts).</param>
        /// <param name="parent">Optional transform parent for organisation in the scene hierarchy.</param>
        public ObjectPool(System.Func<T> factory, int count = 20, Transform parent = null) {
            _pool = new Queue<T>(count);
            _items = new List<T>(count);
            _capacity = 0;

            _root = new GameObject($"Pool<{typeof(T).Name}>") {
                hideFlags = HideFlags.HideAndDontSave
            };
            if (parent != null) {
                _root.transform.SetParent(parent, false);
            }
            _parent = _root.transform;

            // DontSave objects outlive scene loads and, in the editor, leaving Play mode.
            // Free the pool when the application (or Play mode) ends.
            if (Application.isPlaying) Application.quitting += Dispose;

            AddItems(factory, count);
        }

        /// <summary>Retrieve an active object from the pool. Returns null if the pool is empty or disposed.</summary>
        public T Pop() {
            if (_disposed) return null;

            // Skip items destroyed from outside (scene teardown, tests).
            while (_pool.Count > 0) {
                T item = _pool.Dequeue();
                if (!IsAlive(item)) continue;
                item.gameObject.SetActive(true);
                return item;
            }

            Debug.LogWarning($"Pool<{typeof(T).Name}> exhausted — consider increasing initial capacity.");
            return null;
        }

        /// <summary>Return an object to the pool for reuse. Ignored once the pool is disposed.</summary>
        public void Push(T item) {
            if (!IsAlive(item)) return;

            item.gameObject.SetActive(false);
            if (_disposed) return; // Dispose already scheduled its destruction.
            if (_parent != null) item.transform.SetParent(_parent, false);
            _pool.Enqueue(item);
        }

        /// <summary>Force-expand the pool by `count` more items (used when Pop() hits zero).</summary>
        public void Expand(System.Func<T> factory, int count) {
            if (_disposed) return;
            AddItems(factory, count);
        }

        /// <summary>
        /// Destroys the pool root and every item it ever created, including items currently
        /// in flight. Safe to call more than once, and on items already destroyed elsewhere.
        /// Uses DestroyImmediate outside Play mode.
        /// </summary>
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            Application.quitting -= Dispose;

            foreach (var item in _items) {
                if (!IsAlive(item)) continue;
                item.gameObject.SetActive(false);
                DestroyObject(item.gameObject);
            }
            _items.Clear();
            _pool.Clear();

            if (_root != null) DestroyObject(_root);
        }

        /// <summary>Clean debug log of current pool stats.</summary>
        public void DebugInfo() {
            Debug.Log($"Pool<{typeof(T).Name}> capacity={_capacity} available={Available}");
        }

        /// <summary>Non-generic push for use by pooled-item subclasses that store the pool as object.</summary>
        public void PushBase(object item) => Push((T)item);

        // ── Internals ─────────────────────────────────────────────────────

        private void AddItems(System.Func<T> factory, int count) {
            for (int i = 0; i < count; i++) {
                T item = factory();
                if (!IsAlive(item)) continue;
                item.gameObject.SetActive(false);
                // Parent under the root from the start so the root owns every item.
                item.transform.SetParent(_parent, false);
                _pool.Enqueue(item);
                _items.Add(item);
                _capacity++;
            }
        }

        /// <summary>Unity-aware null check (a destroyed component compares equal to null).</summary>
        private static bool IsAlive(T item) {
            return (Object)item != null;
        }

        private static void DestroyObject(Object obj) {
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }
    }
}
