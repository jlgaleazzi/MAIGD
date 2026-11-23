using System;
using UnityEngine;
using Biplanes.Core;

namespace Biplanes.Enemies {

    /// <summary>
    /// Small world-space damage bar floating above a tough aircraft (B076, B077): the bomber and
    /// the zeppelin. Shown at all times while the aircraft is alive, so the player can see a
    /// goal target is a tough one and how close it is to going down.
    ///
    /// • Look (GDD Style Guide, white only): a pencil #9DB4CC frame, a paper #0F2A4A backing,
    ///   and an ink #E8F1FA fill showing the REMAINING health fraction, anchored on the left:
    ///   full at 0% damage, shrinking as the aircraft is hit, gone at 100%. Drawn with one
    ///   generated 1x1 sprite (no art files) at sortingOrder 11 / 12 / 13: above the aircraft
    ///   (5) and FX smoke (10), under projectiles (15) and the player (20).
    /// • Thin at any resolution (B077): the inner height is InnerHeightPx and the frame FramePx
    ///   screen pixels, converted to world units from the camera (px per unit = pixelHeight /
    ///   (2 * orthographicSize)) and re-laid out whenever that changes. Without an orthographic
    ///   camera the world fallbacks (the `height` passed to Attach, OutlineThickness) apply.
    ///   The width stays in world units (BomberWidth / ZeppelinWidth).
    /// • Every hit reads (B077): the frame flashes ink for FlashSeconds whenever the fill
    ///   drops, and the first hit always removes at least MinStepPx pixels of fill.
    /// • A child of the aircraft, so it follows it and is removed with it (crash, despawn,
    ///   mission teardown or restart) with no bookkeeping. Each LateUpdate it cancels the
    ///   parent's turn and scale (CancelAngle, CompensateScale) and sits OffsetY above the
    ///   aircraft's pivot, so the aircraft's pitch, rotation, flipX or a negative x-scale never
    ///   turn, mirror or squash it. Kept under the screen top (ClampBelowTop) so a zeppelin
    ///   hugging the top edge still shows its bar.
    /// • Visual only: no collider, no Rigidbody2D, no events. Score, objectives, EnemyPressure,
    ///   the minimap and collisions never see it. Hidden the moment the bound "is dead" check
    ///   is true (the bomber's killing hit before its wreck appears; the zeppelin's Die).
    /// • EditMode-safe: built in the aircraft's static factory (Awake/LateUpdate don't run in
    ///   EditMode tests); tests call Refresh() / Tick(dt) directly.
    /// </summary>
    public class EnemyHealthBar : MonoBehaviour {

        // ── Layout presets (visual only, not gameplay tunables) ───────────

        /// <summary>Bomber bar: inner width (world units), fallback inner height (world units,
        /// used only without an orthographic camera), and height of the bar centre above the
        /// bomber's pivot (the 1.4-unit-tall art reaches ~0.7, a climbing nose a little more).</summary>
        public const float BomberWidth = 1.6f;
        public const float BomberHeight = 0.09f;
        public const float BomberOffsetY = 1.0f;

        /// <summary>Zeppelin bar: wider, above the top turrets (envelope ink top ~0.85, turret
        /// tops ~1.2). Same on-screen thickness as the bomber's.</summary>
        public const float ZeppelinWidth = 3.0f;
        public const float ZeppelinHeight = 0.09f;
        public const float ZeppelinOffsetY = 1.5f;

        /// <summary>On-screen inner bar height in pixels (B077): with the 2 px frame on each
        /// side the bar is 14 px tall at any resolution.</summary>
        public const float InnerHeightPx = 10f;

        /// <summary>On-screen frame thickness in pixels (B077).</summary>
        public const float FramePx = 2f;

        /// <summary>Fallback frame thickness in world units when there is no orthographic camera.</summary>
        public const float OutlineThickness = 0.02f;

        /// <summary>The first hit always removes at least this many screen pixels of fill, so a
        /// 2.5% bullet on a wide bar is never lost to rounding (B077).</summary>
        public const float MinStepPx = 2f;

        /// <summary>Seconds the frame shows ink after the fill drops (hit flash, B077).</summary>
        public const float FlashSeconds = 0.15f;

        /// <summary>Gap kept between the bar's top edge and the screen top.</summary>
        public const float ScreenTopMargin = 0.1f;

        /// <summary>Draw order (ARCHITECTURE §3): frame, backing, fill, between FX (10) and
        /// projectiles (15).</summary>
        public const int FrameSortingOrder = 11;
        public const int BackSortingOrder = 12;
        public const int FillSortingOrder = 13;

        /// <summary>White-only palette (GDD Style Guide).</summary>
        public static readonly Color Ink = new Color(0xE8 / 255f, 0xF1 / 255f, 0xFA / 255f, 1f);
        public static readonly Color Pencil = new Color(0x9D / 255f, 0xB4 / 255f, 0xCC / 255f, 1f);
        public static readonly Color Paper = new Color(0x0F / 255f, 0x2A / 255f, 0x4A / 255f, 1f);

        /// <summary>Child GameObject name (one bar per aircraft).</summary>
        public const string ObjectName = "EnemyHealthBar";

        private static Sprite _pixel;

        // ── State ─────────────────────────────────────────────────────────

        private Transform _target;
        private Func<float> _damagePercent;
        private Func<bool> _isDead;
        private float _width;
        private float _fallbackHeight;
        private float _height;
        private float _frameThickness;
        private float _layoutPpu = -1f;
        private float _ppu;
        private float _offsetY;
        private SpriteRenderer _frame;
        private SpriteRenderer _back;
        private SpriteRenderer _fill;
        private Camera _cam;
        private float _fraction = 1f;
        private bool _shown = true;
        private float _flashTimer;

        /// <summary>Remaining-health fraction shown by the fill (0..1) after the last Refresh.</summary>
        public float Fraction => _fraction;

        /// <summary>True while the bar is drawn (the aircraft is alive).</summary>
        public bool IsShown => _shown;

        /// <summary>True while the hit flash is on (frame drawn in ink).</summary>
        public bool IsFlashing => _flashTimer > 0f;

        /// <summary>Inner bar width in world units.</summary>
        public float Width => _width;

        /// <summary>Inner bar height in world units (InnerHeightPx on screen, or the fallback).</summary>
        public float Height => _height;

        /// <summary>Frame thickness in world units (FramePx on screen, or OutlineThickness).</summary>
        public float FrameThickness => _frameThickness;

        /// <summary>Screen pixels per world unit used for the last layout; 0 = no camera (fallbacks).</summary>
        public float PixelsPerUnitUsed => _ppu;

        /// <summary>Current world width of the ink fill.</summary>
        public float FillWorldWidth => _fill != null && _fill.enabled ? _fill.transform.localScale.x : 0f;

        /// <summary>The three renderers (frame, backing, fill), for tests.</summary>
        public SpriteRenderer FrameRenderer => _frame;
        public SpriteRenderer BackRenderer => _back;
        public SpriteRenderer FillRenderer => _fill;

        // ── Factories ─────────────────────────────────────────────────────

        /// <summary>Bar bound to an enemy-mode Health (bomber): fill = 1 - CurrentDamagePercent
        /// / 100, hidden once Health.IsDead. Returns null when `target` or `health` is null.</summary>
        public static EnemyHealthBar Attach(Transform target, Health health, float width, float height, float offsetY) {
            if (target == null || health == null) return null;
            return Attach(target, () => health != null ? health.CurrentDamagePercent : 100f,
                () => health == null || health.IsDead, width, height, offsetY);
        }

        /// <summary>
        /// Builds the bar as a child of `target`: `damagePercent` returns the aircraft's damage
        /// 0..100 and `isDead` hides the bar when true (null = never). `height` is the world
        /// inner height used only without an orthographic camera (otherwise InnerHeightPx).
        /// Reuses an existing bar on `target` (rebinding it) instead of stacking a second one.
        /// Returns null when `target` or `damagePercent` is null.
        /// </summary>
        public static EnemyHealthBar Attach(Transform target, Func<float> damagePercent, Func<bool> isDead,
                                            float width, float height, float offsetY) {
            if (target == null || damagePercent == null) return null;

            EnemyHealthBar bar = null;
            var existing = target.Find(ObjectName);
            if (existing != null) bar = existing.GetComponent<EnemyHealthBar>();
            if (bar == null) {
                var go = new GameObject(ObjectName);
                go.transform.SetParent(target, false);
                bar = go.AddComponent<EnemyHealthBar>();
                bar.Build();
            }

            bar._target = target;
            bar._damagePercent = damagePercent;
            bar._isDead = isDead;
            bar._width = Mathf.Max(0.01f, width);
            bar._fallbackHeight = Mathf.Max(0.01f, height);
            bar._offsetY = offsetY;
            bar._layoutPpu = -1f;           // force a fresh layout for the new size
            bar._fraction = FillFraction(damagePercent());
            bar._flashTimer = 0f;
            bar.Refresh();
            return bar;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────

        private void LateUpdate() {
            Tick(Time.deltaTime);
        }

        /// <summary>Refresh without advancing the hit flash (tests, Attach).</summary>
        public void Refresh() {
            Tick(0f);
        }

        /// <summary>
        /// Advances the hit flash by `dt` seconds, follows the aircraft (unrotated, unit world
        /// scale, OffsetY above it, under the screen top), re-lays out the pixel-sized height
        /// when the camera's pixels per unit changed, and updates the fill from the bound
        /// damage (flashing the frame when it dropped), or hides the bar when the aircraft is
        /// dead. Called every LateUpdate; tests call it directly.
        /// </summary>
        public void Tick(float dt) {
            if (_flashTimer > 0f && dt > 0f) _flashTimer = Mathf.Max(0f, _flashTimer - dt);

            if (_target == null) _target = transform.parent;
            if (_target == null || _damagePercent == null) {
                SetShown(false);
                return;
            }

            if (_isDead != null && _isDead()) {
                _fraction = 0f;
                _flashTimer = 0f;
                SetShown(false);
                return;
            }

            // Pixel-sized thickness: re-layout only when the camera scale changed.
            if (_cam == null) _cam = Camera.main;
            float ppu = _cam != null && _cam.orthographic
                ? PixelsPerUnit(_cam.pixelHeight, _cam.orthographicSize) : 0f;
            if (!Mathf.Approximately(ppu, _layoutPpu)) Layout(ppu);

            // Follow: the parent's rotation and scale (mirror included) are cancelled, so the
            // aircraft's pitch / flip never reach the bar. Setting transform.rotation directly
            // is not enough under a negative-scale parent (Unity ignores the mirror there).
            Vector3 parentScale = _target.parent == null ? _target.localScale : _target.lossyScale;
            transform.localRotation = Quaternion.Euler(0f, 0f,
                CancelAngle(_target.rotation.eulerAngles.z, parentScale));
            transform.localScale = CompensateScale(parentScale);

            Vector3 p = _target.position;
            float y = p.y + _offsetY;
            if (_cam != null && _cam.orthographic) {
                float top = _cam.transform.position.y + _cam.orthographicSize;
                y = ClampBelowTop(y, top, _height * 0.5f + _frameThickness, ScreenTopMargin);
            }
            transform.position = new Vector3(p.x, y, p.z);

            float next = FillFraction(_damagePercent());
            if (next < _fraction - 1e-6f) _flashTimer = FlashSeconds;   // a hit landed
            _fraction = next;
            SetShown(true);
            ApplyFill();
        }

        // ── Pure helpers (unit-tested) ────────────────────────────────────

        /// <summary>Remaining-health fraction for a damage percent: 1 - damage / 100, clamped to
        /// 0..1 (0% damage = full bar, 100% or more = empty). NaN counts as undamaged (1). Pure.</summary>
        public static float FillFraction(float damagePercent) {
            if (float.IsNaN(damagePercent)) return 1f;
            return Mathf.Clamp01(1f - damagePercent / 100f);
        }

        /// <summary>Local x of the fill's centre for a left-anchored fill of `fraction` (clamped
        /// 0..1) inside a bar `width` wide centred on 0. Pure.</summary>
        public static float FillCenterX(float width, float fraction) {
            float f = Mathf.Clamp01(fraction);
            return -width * 0.5f + width * f * 0.5f;
        }

        /// <summary>Screen pixels per world unit of an orthographic camera `pixelHeight` pixels
        /// tall showing `orthoSize` half-height: pixelHeight / (2 * orthoSize). 0 when either is
        /// not positive (no usable camera). Pure.</summary>
        public static float PixelsPerUnit(float pixelHeight, float orthoSize) {
            if (pixelHeight <= 0f || orthoSize <= 0f || float.IsNaN(pixelHeight) || float.IsNaN(orthoSize)) return 0f;
            return pixelHeight / (2f * orthoSize);
        }

        /// <summary>`px` screen pixels in world units at `pixelsPerUnit`; `fallbackWorld` when
        /// pixelsPerUnit is not positive. Pure.</summary>
        public static float PixelsToWorld(float px, float pixelsPerUnit, float fallbackWorld) {
            return pixelsPerUnit > 0f ? px / pixelsPerUnit : fallbackWorld;
        }

        /// <summary>
        /// World width of the drawn fill for a bar `width` wide at `fraction` (clamped 0..1):
        /// width * fraction, except that a damaged bar (fraction &lt; 1) is drawn at least
        /// MinStepPx screen pixels short of full, so the first small hit is always visible.
        /// Without a camera scale (pixelsPerUnit &lt;= 0) it is exactly width * fraction. Pure.
        /// </summary>
        public static float VisibleFillWidth(float width, float fraction, float pixelsPerUnit) {
            float f = Mathf.Clamp01(fraction);
            float w = width * f;
            if (f >= 1f || f <= 0f || pixelsPerUnit <= 0f) return w;
            float maxDamaged = width - MinStepPx / pixelsPerUnit;
            return Mathf.Max(0f, Mathf.Min(w, maxDamaged));
        }

        /// <summary>`y` lowered, if needed, so a bar `halfHeight` tall keeps `margin` below the
        /// screen top `screenTop`. Never raises `y`. Pure.</summary>
        public static float ClampBelowTop(float y, float screenTop, float halfHeight, float margin) {
            return Mathf.Min(y, screenTop - halfHeight - margin);
        }

        /// <summary>Local scale that cancels the parent's lossy scale (sign included), so the bar
        /// keeps a positive unit world scale under a mirrored or scaled parent. A zero axis
        /// gives 1. Pure.</summary>
        public static Vector3 CompensateScale(Vector3 parentLossyScale) {
            return new Vector3(Inverse(parentLossyScale.x), Inverse(parentLossyScale.y), 1f);
        }

        /// <summary>Local z angle (degrees) that cancels a parent turned `parentAngle` degrees
        /// with scale `parentScale`: -parentAngle, sign-flipped when the parent is mirrored on
        /// exactly one axis (a mirror reverses the turn direction). With CompensateScale the
        /// child's world rotation-scale is the identity. Pure.</summary>
        public static float CancelAngle(float parentAngle, Vector3 parentScale) {
            float mirror = (parentScale.x < 0f) != (parentScale.y < 0f) ? -1f : 1f;
            return -parentAngle * mirror;
        }

        private static float Inverse(float s) {
            return Mathf.Abs(s) > 1e-5f ? 1f / s : 1f;
        }

        // ── Internal helpers ──────────────────────────────────────────────

        private void Build() {
            _frame = MakePart("Frame", Pencil, FrameSortingOrder);
            _back = MakePart("Back", Paper, BackSortingOrder);
            _fill = MakePart("Fill", Ink, FillSortingOrder);
        }

        private SpriteRenderer MakePart(string partName, Color color, int order) {
            var go = new GameObject(partName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = PixelSprite();
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Sizes the frame and backing for the current width and the pixel-sized
        /// height at `ppu` screen pixels per unit (0 = world fallbacks).</summary>
        private void Layout(float ppu) {
            _layoutPpu = ppu;
            _ppu = ppu;
            _height = PixelsToWorld(InnerHeightPx, ppu, _fallbackHeight);
            _frameThickness = PixelsToWorld(FramePx, ppu, OutlineThickness);
            float t = _frameThickness * 2f;
            if (_frame != null) {
                _frame.transform.localPosition = Vector3.zero;
                _frame.transform.localScale = new Vector3(_width + t, _height + t, 1f);
            }
            if (_back != null) {
                _back.transform.localPosition = Vector3.zero;
                _back.transform.localScale = new Vector3(_width, _height, 1f);
            }
        }

        private void ApplyFill() {
            if (_frame != null) _frame.color = _flashTimer > 0f ? Ink : Pencil;
            if (_fill == null) return;
            if (_fraction <= 0f) {
                _fill.enabled = false;
                return;
            }
            float w = VisibleFillWidth(_width, _fraction, _ppu);
            if (w <= 0f) {
                _fill.enabled = false;
                return;
            }
            _fill.enabled = true;
            _fill.transform.localPosition = new Vector3(FillCenterX(_width, w / _width), 0f, 0f);
            _fill.transform.localScale = new Vector3(w, _height, 1f);
        }

        private void SetShown(bool shown) {
            _shown = shown;
            if (_frame != null) _frame.enabled = shown;
            if (_back != null) _back.enabled = shown;
            if (_fill != null) _fill.enabled = shown && _fraction > 0f;
        }

        /// <summary>One shared 1x1 white sprite, 1 world unit at scale 1, tinted per part.</summary>
        private static Sprite PixelSprite() {
            if (_pixel != null) return _pixel;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply(false);
            _pixel = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f,
                0, SpriteMeshType.FullRect);
            return _pixel;
        }
    }
}
