"""Generate the placeholder sound effects for Biplanes (T948).

    python tools/synth_sfx.py           writes Biplanes/Assets/Biplanes/Resources/Audio/sfx/<name>.wav
    python tools/synth_sfx.py --check   regenerates in memory, compares with the files on disk
                                        and validates format/levels; writes nothing

Style: a period WWI air war heard through the drawing board (GDD Style Guide, "Sound
effects"). Rotary engines, machine guns and black-powder thumps, recorded "dry and slightly
lo-fi", with paper and pencil foley (crumple, scratch, page turn, stamp) for the drawn layer.

Python standard library only. Seeded per clip, so two runs give byte-identical files.
Format: mono, 16-bit PCM, 44.1 kHz. Gameplay cues peak at -3 dBFS, UI cues at -13 dBFS.
One-shots get a 2 ms fade-in and a 10 ms fade-out. Loops (engine_loop, zeppelin_hum,
smoke_crackle) are synthesised circularly (events wrap around, noise is filtered twice
round the ring, tones have whole periods in the loop) so the end runs into the start.
"""
import argparse
import array
import io
import math
import random
import sys
import wave
from pathlib import Path

SR = 44100
SEED = "biplanes-sfx-v1"
ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "Biplanes" / "Assets" / "Biplanes" / "Resources" / "Audio" / "sfx"
TAU2 = 2.0 * math.pi


# ---------------------------------------------------------------- basics
def ns(sec):
    return int(round(sec * SR))


def zeros(sec):
    return [0.0] * ns(sec)


def add(buf, sig, at=0.0, gain=1.0, wrap=False):
    """Mix sig into buf starting at time `at` (s). wrap=True wraps around (loops)."""
    o = ns(at)
    n = len(buf)
    if wrap:
        for i, v in enumerate(sig):
            buf[(o + i) % n] += v * gain
    else:
        end = min(n, o + len(sig))
        for i in range(max(0, o), end):
            buf[i] += sig[i - o] * gain
    return buf


def noise(rng, n):
    u = rng.uniform
    return [u(-1.0, 1.0) for _ in range(n)]


def env_exp(n, tau, attack=0.0):
    """Linear attack (s) then exponential decay with time constant tau (s)."""
    a = max(1, ns(attack))
    k = math.exp(-1.0 / (tau * SR))
    out = [0.0] * n
    v = 1.0
    for i in range(n):
        if i < a and attack > 0:
            out[i] = i / a
        else:
            out[i] = v
            v *= k
    return out


def mul(a, b):
    return [x * y for x, y in zip(a, b)]


def scale(a, g):
    return [x * g for x in a]


# ---------------------------------------------------------------- filters (RBJ biquads)
def _biquad(x, b0, b1, b2, a0, a1, a2):
    b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0
    y = [0.0] * len(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, v in enumerate(x):
        o = b0 * v + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
        x2 = x1; x1 = v; y2 = y1; y1 = o
        y[i] = o
    return y


def lp(x, fc, q=0.707):
    w = TAU2 * fc / SR
    c, al = math.cos(w), math.sin(w) / (2 * q)
    return _biquad(x, (1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al)


def hp(x, fc, q=0.707):
    w = TAU2 * fc / SR
    c, al = math.cos(w), math.sin(w) / (2 * q)
    return _biquad(x, (1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al)


def bp(x, fc, q=1.0):
    """Band-pass, 0 dB peak gain."""
    w = TAU2 * fc / SR
    c, al = math.cos(w), math.sin(w) / (2 * q)
    return _biquad(x, al, 0.0, -al, 1 + al, -2 * c, 1 - al)


def circ(fn, x, *args):
    """Apply a filter to a loop as if it repeated forever (run twice round, keep lap 2)."""
    y = fn(x + x, *args)
    return y[len(x):]


# ---------------------------------------------------------------- generators
def sweep_sine(n, f0, f1, glide=None, phase=0.0):
    """Sine gliding exponentially from f0 to f1 over `glide` s (default: whole length)."""
    g = n if glide is None else max(1, ns(glide))
    out = [0.0] * n
    ph = phase
    r = math.log(f1 / f0)
    for i in range(n):
        f = f0 * math.exp(r * min(1.0, i / g))
        out[i] = math.sin(ph)
        ph += TAU2 * f / SR
    return out


def modes(n, parts, rng=None):
    """Sum of decaying sines: parts = [(freq, amp, tau)] (a struck object)."""
    out = [0.0] * n
    for f, a, tau in parts:
        ph = rng.uniform(0, TAU2) if rng else 0.0
        k = math.exp(-1.0 / (tau * SR))
        w = TAU2 * f / SR
        v = a
        for i in range(n):
            out[i] += v * math.sin(ph + w * i)
            v *= k
            if v < 1e-5:
                break
    return out


def burst(rng, sec, tau, attack=0.0):
    n = ns(sec)
    return mul(noise(rng, n), env_exp(n, tau, attack))


def thump(sec, f0, f1, tau, glide=None):
    n = ns(sec)
    return mul(sweep_sine(n, f0, f1, glide), env_exp(n, tau, 0.002))


def crackle(rng, sec, rate0, rate1, fc, q=0.8, amp_decay=None, wrap=False):
    """Paper crumple / fire crackle: sparse tiny noise grains, band-passed.
    rate goes linearly rate0 -> rate1 (grains per second)."""
    n = ns(sec)
    raw = [0.0] * n
    i = 0
    while i < n:
        t = i / n
        rate = rate0 + (rate1 - rate0) * t
        if rate <= 0:
            break
        i += max(1, int(rng.expovariate(rate) * SR))
        if i >= n:
            break
        ln = rng.randint(15, 140)
        a = rng.uniform(0.25, 1.0) * (amp_decay(i / SR) if amp_decay else 1.0)
        k = math.exp(-5.0 / ln)
        v = a
        for j in range(ln):
            p = i + j
            if p >= n:
                if not wrap:
                    break
                p -= n
            raw[p] += v * rng.uniform(-1, 1)
            v *= k
    f = (lambda x: circ(bp, x, fc, q)) if wrap else (lambda x: bp(x, fc, q))
    return f(raw)


def grain_am(rng, n, rate, floor=0.2):
    """Rough amplitude modulation (scratchy / fluttering): random levels at `rate` Hz, smoothed."""
    step = max(1, int(SR / rate))
    out = [0.0] * n
    cur = tgt = rng.random()
    for i in range(n):
        if i % step == 0:
            tgt = rng.random()
        cur += (tgt - cur) * 0.02
        out[i] = floor + (1 - floor) * cur
    return out


def brass(sec, f, attack=0.04, release=0.08, hmax=12, fmax=6000.0, bright=0.35,
          vib=0.0, bend=0.0):
    """Additive sawtooth-ish tone whose upper harmonics bloom with the envelope (brass 'blat').
    bend: relative pitch change over the note (e.g. -0.03 droops 3%)."""
    n = ns(sec)
    a, r = ns(attack), ns(release)
    env = [0.0] * n
    for i in range(n):
        e = 1.0 - math.exp(-i / (a / 3.0)) if a else 1.0
        if i > n - r:
            e *= (n - i) / r
        env[i] = e * (1.0 - 0.15 * i / n)
    out = [0.0] * n
    hs = [h for h in range(1, hmax + 1) if f * h < fmax]
    ph = 0.0
    for i in range(n):
        t = i / SR
        ff = f * (1 + bend * i / n) * (1 + vib * math.sin(TAU2 * 5.5 * t) * min(1.0, t / 0.25))
        ph += TAU2 * ff / SR
        e = env[i]
        if e <= 0:
            continue
        s = 0.0
        for h in hs:
            s += math.sin(h * ph) * (e ** (1.0 + bright * (h - 1))) / h
        out[i] = s
    return out


def snare_stroke(rng, amp):
    s = scale(bp(burst(rng, 0.09, 0.018), 2600, 0.7), amp)
    add(s, modes(ns(0.09), [(190, 0.35 * amp, 0.02), (330, 0.15 * amp, 0.012)], rng))
    return s


def pencil_scratch(rng, sec):
    n = ns(sec)
    s = bp(noise(rng, n), 3800, 1.4)
    s = mul(s, grain_am(rng, n, 180, 0.05))
    env = [math.sin(math.pi * i / n) ** 0.6 for i in range(n)]
    return mul(s, env)


def echoes(x, taps, fc=2500):
    """Cheap 'distance': low-passed early reflections [(delay_s, gain)]."""
    w = lp(x, fc)
    out = list(x) + [0.0] * ns(max(d for d, _ in taps))
    for d, g in taps:
        add(out, w, d, g)
    return out


# ---------------------------------------------------------------- clips
def c_engine_loop(rng):
    # Rotary engine: 9-cylinder firing pattern at 36 Hz (54 pulses in 1.5 s), each pulse a
    # band-passed noise pop with a low body; prop wash; whole-period low fundamental.
    L = 1.5
    n = ns(L)
    period = n // 54  # 1225 samples
    pulses = [0.0] * n
    body = [0.0] * n
    pattern = [1.0, 0.82, 0.93, 0.76, 0.97, 0.85, 0.9, 0.79, 0.88]
    for k in range(54):
        a = pattern[k % 9] * rng.uniform(0.85, 1.1)
        o = k * period + rng.randint(-25, 25)
        b = burst(rng, 0.014, 0.0032, 0.0004)
        for j, v in enumerate(b):
            pulses[(o + j) % n] += v * a
        m = modes(ns(0.03), [(118, a, 0.009), (236, 0.4 * a, 0.006)])
        for j, v in enumerate(m):
            body[(o + j) % n] += v
    pops = circ(bp, pulses, 650, 0.9)
    bark = circ(bp, pulses, 1800, 1.2)
    wash = circ(lp, noise(rng, n), 450)
    out = [0.0] * n
    for i in range(n):
        t = i / SR
        out[i] = (1.6 * pops[i] + 0.8 * bark[i] + 0.3 * body[i] + 0.25 * wash[i]
                  + 0.08 * math.sin(TAU2 * 36 * t) + 0.05 * math.sin(TAU2 * 72 * t + 0.7))
    return circ(lp, out, 7000)


def gun_shot(rng, sec, crack_hp, bark_f, thump_f, thump_tau, body_lp, clack_f, clack_at,
             tail_lp, tail_tau, gains, drive=1.0):
    """One machine-gun round, built from noise and a pitched-down sine, no ringing modes
    (pure decaying sines above 2 kHz read as tin). Layers: 2 ms broadband muzzle crack,
    muzzle bark (wide band-passed noise), powder thump (sine glide + low-passed noise,
    soft-saturated for weight), dull bolt/belt clack (band-passed noise), low-passed air
    tail. gains = (crack, bark, thump, clack, tail)."""
    gc, gb, gt, gk, ga = gains
    out = zeros(sec)
    # Muzzle crack: very short broadband noise, lightly high-passed so it stays a click.
    add(out, hp(burst(rng, 0.006, 0.0015), crack_hp), 0, gc)
    # Muzzle bark: the blast's mid-band body, wide (q 0.7) so it has no pitch.
    add(out, bp(burst(rng, 0.03, 0.006, 0.0005), bark_f, 0.7), 0, gb)
    # Powder thump: falling sine plus a low noise puff, both under body_lp, then tanh drive.
    f0, f1 = thump_f
    n = ns(0.09)
    body = [0.0] * n
    add(body, thump(0.09, f0, f1, thump_tau, 0.03), 0, 1.0)
    add(body, lp(lp(burst(rng, 0.09, thump_tau * 0.8, 0.001), body_lp), body_lp), 0, 1.4)
    pk = max(abs(v) for v in body) or 1.0
    body = [math.tanh(drive * v / pk) / math.tanh(drive) for v in body]
    add(out, body, 0.0005, gt)
    # Action clack: bolt slam then belt pull, dull band-passed noise grains (no tone).
    for at, g in ((clack_at, 1.0), (clack_at + 0.022, 0.45)):
        k = lp(bp(burst(rng, 0.012, 0.0022), clack_f, 1.1), 2500)
        add(out, k, at, gk * g)
    # Air tail: dry low-passed noise, quick swell then decay.
    tail = lp(lp(burst(rng, sec, tail_tau, 0.004), tail_lp), tail_lp)
    add(out, tail, 0.002, ga)
    return out


def c_player_gun(rng):
    # Vickers-style round: crack + weighty 150 -> 75 Hz powder thump + bolt/belt clack + air.
    return lp(gun_shot(rng, 0.15, 500, 1100, (150, 75), 0.016, 260, 650, 0.034, 2500, 0.022,
                       (0.6, 0.9, 0.22, 0.7, 0.4)), 8000)


def c_enemy_gun(rng):
    # Same family, lower and duller, further away: softer crack, 115 -> 55 Hz thump,
    # darker clack, longer air tail, low-passed 2.2 kHz + early reflections (distance).
    dry = gun_shot(rng, 0.2, 300, 700, (115, 55), 0.02, 200, 480, 0.038, 1500, 0.035,
                   (0.8, 0.6, 0.16, 0.6, 0.35))
    dry = lp(dry, 2200)
    out = echoes(dry, [(0.019, 0.35), (0.034, 0.25), (0.053, 0.15)], 1400)
    return out[:ns(0.2)] + [0.0] * max(0, ns(0.2) - len(out))


def c_bomb_release(rng):
    # Latch clack (two metal ticks), then a short falling whistle.
    out = zeros(0.45)
    add(out, modes(ns(0.04), [(1850, 0.5, 0.006), (3100, 0.35, 0.004)], rng))
    add(out, hp(burst(rng, 0.006, 0.001), 1200), 0, 0.5)
    add(out, modes(ns(0.04), [(1400, 0.35, 0.007), (2600, 0.25, 0.004)], rng), 0.028)
    n = ns(0.36)
    wh = sweep_sine(n, 1900, 1150)
    breath = bp(noise(rng, n), 1500, 3.0)
    env = [min(1.0, i / ns(0.06)) * (1 - i / n) ** 1.4 for i in range(n)]
    add(out, mul([0.55 * a + 0.6 * b for a, b in zip(wh, breath)], env), 0.07, 0.6)
    return out


def c_bomb_impact(rng):
    # Deep dirt-and-wood thump: falling sine + low-passed noise + dirt/wood debris.
    out = zeros(0.5)
    add(out, thump(0.5, 75, 34, 0.13, 0.3), 0, 1.0)
    add(out, lp(burst(rng, 0.4, 0.07, 0.002), 380), 0, 1.6)
    add(out, lp(burst(rng, 0.03, 0.006), 1800), 0, 0.4)
    add(out, modes(ns(0.1), [(190, 0.2, 0.03), (330, 0.12, 0.02)], rng), 0.005)
    add(out, crackle(rng, 0.4, 70, 5, 1300, 0.7, lambda t: math.exp(-t / 0.15)), 0.03, 0.4)
    return out


def c_hit_enemy(rng):
    # Round through taut doped canvas: dry noise tick (no ringing modes, which read as tin),
    # a small 320 -> 200 Hz "thup" of the fabric, then a quick grainy canvas rip. Light.
    out = zeros(0.1)
    add(out, bp(burst(rng, 0.008, 0.0012), 2600, 0.8), 0, 1.0)
    add(out, hp(burst(rng, 0.004, 0.0007), 2500), 0, 0.4)
    add(out, thump(0.04, 320, 200, 0.006), 0.001, 0.15)
    n = ns(0.045)
    rip = mul(bp(noise(rng, n), 1700, 0.7), mul(grain_am(rng, n, 900, 0.0),
                                               [(1 - i / n) for i in range(n)]))
    add(out, rip, 0.008, 0.45)
    return lp(out, 7000)


def c_hit_player(rng):
    # Heavier wood-and-canvas crack, lower, plus a short pencil scratch after it.
    out = zeros(0.3)
    add(out, lp(burst(rng, 0.08, 0.012), 1700), 0, 1.0)
    add(out, modes(ns(0.15), [(225, 0.6, 0.03), (470, 0.45, 0.02), (910, 0.3, 0.012)], rng))
    add(out, thump(0.15, 115, 60, 0.04), 0, 0.7)
    add(out, pencil_scratch(rng, 0.09), 0.06, 0.8)
    return out


def c_flak_fire(rng):
    # Dull distant pop from the ground.
    out = zeros(0.35)
    add(out, thump(0.3, 125, 55, 0.05), 0, 1.0)
    add(out, lp(burst(rng, 0.2, 0.03, 0.002), 600), 0, 1.2)
    out = echoes(out, [(0.045, 0.3), (0.09, 0.15)], 500)
    return out[:ns(0.35)]


def c_flak_burst(rng):
    # Sharp crack, then a puff of air with a low body.
    out = zeros(0.45)
    add(out, hp(burst(rng, 0.03, 0.003), 1200), 0, 1.6)
    add(out, thump(0.3, 95, 45, 0.06), 0, 0.35)
    n = ns(0.4)
    puff = bp(noise(rng, n), 800, 0.6)
    env = [min(1.0, i / ns(0.02)) * math.exp(-i / ns(0.09)) for i in range(n)]
    add(out, mul(puff, env), 0.004, 0.9)
    add(out, crackle(rng, 0.25, 60, 0, 2400, 0.8, lambda t: math.exp(-t / 0.08)), 0.01, 0.25)
    return out


def c_explosion(rng):
    # Low boom with a paper-crumple tail (~1 s, the 6-frame sketch animation).
    out = zeros(1.0)
    add(out, thump(1.0, 62, 30, 0.22, 0.6), 0, 1.0)
    n = ns(0.9)
    rum = burst(rng, 0.9, 0.18, 0.004)
    add(out, lp(rum, 320), 0, 2.0)
    add(out, lp(burst(rng, 0.06, 0.012), 2500), 0, 0.5)
    add(out, crackle(rng, 0.85, 160, 10, 2100, 0.7, lambda t: math.exp(-t / 0.35)), 0.1, 0.7)
    return out


def c_explosion_big(rng):
    # Zeppelin destruction: long rolling boom, staggered blasts, long crumple tail.
    out = zeros(2.6)
    for at, g, f0 in [(0.0, 1.0, 55), (0.35, 0.7, 48), (0.8, 0.55, 42), (1.3, 0.35, 38)]:
        add(out, thump(1.2, f0, 24, 0.32, 0.8), at, g)
        add(out, lp(burst(rng, 1.0, 0.25, 0.01), 260), at, 1.8 * g)
        add(out, lp(burst(rng, 0.05, 0.012), 2000), at, 0.35 * g)
    add(out, lp(burst(rng, 2.5, 0.8, 0.05), 140), 0, 1.2)
    add(out, crackle(rng, 2.3, 140, 6, 1900, 0.7, lambda t: math.exp(-t / 0.9)), 0.15, 0.4)
    return out


def c_zeppelin_hum(rng):
    # Slow, deep hum: big slow engines (15 Hz pulses), beating low tones, rumble. Loop 2 s.
    L = 2.0
    n = ns(L)
    period = n // 30
    pulses = [0.0] * n
    for k in range(30):
        a = rng.uniform(0.8, 1.0)
        b = mul(noise(rng, ns(0.05)), env_exp(ns(0.05), 0.012, 0.002))
        m = modes(ns(0.12), [(46, 0.8, 0.04), (92, 0.3, 0.025)])
        o = k * period + rng.randint(-40, 40)
        for j, v in enumerate(b):
            pulses[(o + j) % n] += v * a
        for j, v in enumerate(m):
            pulses[(o + j) % n] += v * a
    thr = circ(lp, pulses, 260)
    rum = circ(lp, noise(rng, n), 150)
    out = [0.0] * n
    for i in range(n):
        t = i / SR
        hum = (math.sin(TAU2 * 55 * t) + math.sin(TAU2 * 55.5 * t + 1.0)
               + 0.5 * math.sin(TAU2 * 110 * t + 0.3) + 0.25 * math.sin(TAU2 * 165.5 * t))
        out[i] = 0.25 * hum + 0.9 * thr[i] + 0.8 * rum[i]
    return out


def c_smoke_crackle(rng):
    # Faint crackle and pencil-scribble hiss. Loop 1.5 s, scribble strokes have whole periods.
    L = 1.5
    n = ns(L)
    cr = crackle(rng, L, 45, 45, 2600, 0.9, wrap=True)
    hiss = circ(bp, noise(rng, n), 4200, 1.1)
    out = [0.0] * n
    for i in range(n):
        t = i / SR
        am = 0.5 + 0.5 * math.sin(TAU2 * (6 / L) * t) * math.sin(TAU2 * (4 / L) * t + 0.4)
        out[i] = 0.9 * cr[i] + 0.35 * hiss[i] * am * am
    return out


def c_crash(rng):
    # Landing crunch only (B034): the engine loop already does the whine-down in the air.
    # ~0.1 s of rising wind whistle, the crunch at 0.1 s (dirt thump + wood splinter + bent
    # metal), then a settle tail: two wood creaks, debris ticks and a paper-crumple fade.
    out = zeros(1.0)
    t0 = 0.1
    n = ns(t0)
    ramp = [(i / n) ** 2 for i in range(n)]
    whistle = [0.5 * a + 0.8 * b for a, b in zip(sweep_sine(n, 700, 1050),
                                                 bp(noise(rng, n), 1200, 1.5))]
    add(out, mul(whistle, ramp), 0, 0.3)
    # Impact: deep dirt thump, low-passed dirt noise, short splinter crack.
    add(out, thump(0.7, 82, 30, 0.15, 0.35), t0, 1.2)
    add(out, lp(burst(rng, 0.45, 0.08, 0.002), 320), t0, 2.0)
    add(out, lp(burst(rng, 0.05, 0.01), 2400), t0, 0.9)
    add(out, hp(crackle(rng, 0.12, 900, 60, 2600, 0.8, lambda t: math.exp(-t / 0.03)), 900),
        t0 + 0.004, 1.6)
    # Wood body and bent metal.
    add(out, modes(ns(0.25), [(230, 0.5, 0.045), (410, 0.4, 0.03), (760, 0.3, 0.02)], rng), t0)
    add(out, modes(ns(0.4), [(1170, 0.16, 0.07), (1740, 0.12, 0.05), (2630, 0.07, 0.035)],
                   rng), t0 + 0.012)
    # Settle: two stick-slip wood creaks (click trains, rate gliding down).
    for at, r0, r1, fc, g in [(0.36, 70, 38, 520, 0.35), (0.58, 55, 30, 680, 0.25)]:
        cl = 0.18
        m = ns(cl)
        raw = [0.0] * m
        t = 0.0
        while t < cl:
            i = ns(t)
            if i >= m:
                break
            raw[i] = rng.uniform(0.6, 1.0) * math.sin(math.pi * t / cl)
            t += 1.0 / (r0 + (r1 - r0) * t / cl)
        add(out, bp(bp(raw, fc, 4.0), fc, 4.0), at, g * 50.0)
    # Debris ticks, then the paper crumple fading out.
    add(out, crackle(rng, 0.6, 90, 4, 1500, 0.7, lambda t: math.exp(-t / 0.25)), t0 + 0.05, 2.0)
    add(out, crackle(rng, 0.72, 120, 10, 2200, 0.7, lambda t: math.exp(-t / 0.4)), 0.25, 2.2)
    return out


def c_pickup(rng):
    # Soft warm ruler tap with a consonant chime (G5 + D6, a fifth). The one pleasant cue.
    out = zeros(0.45)
    add(out, modes(ns(0.06), [(520, 0.5, 0.012), (1340, 0.25, 0.007)], rng))
    add(out, lp(burst(rng, 0.01, 0.0015), 3000), 0, 0.3)
    n = ns(0.43)
    env = env_exp(n, 0.11, 0.004)
    tone = [math.sin(TAU2 * 784 * i / SR) + 0.55 * math.sin(TAU2 * 1176 * i / SR)
            + 0.2 * math.sin(TAU2 * 392 * i / SR) for i in range(n)]
    add(out, mul(tone, env), 0.008, 0.45)
    return out


def c_damage_warning(rng):
    # One short alarm bell: inharmonic partials, faster decay higher up, strike click.
    f = 1046.0
    parts = [(f * 0.5, 0.35, 0.22), (f, 1.0, 0.16), (f * 1.19, 0.5, 0.12), (f * 1.5, 0.35, 0.09),
             (f * 2.0, 0.45, 0.07), (f * 2.51, 0.3, 0.05), (f * 2.66, 0.2, 0.04),
             (f * 3.01, 0.15, 0.03)]
    out = zeros(0.55)
    add(out, modes(ns(0.55), parts, rng))
    add(out, hp(burst(rng, 0.005, 0.0008), 2000), 0, 0.8)
    return out


def c_ui_click(rng):
    # Pencil tick on paper.
    out = zeros(0.05)
    add(out, hp(burst(rng, 0.006, 0.0008), 1800), 0, 1.0)
    add(out, modes(ns(0.03), [(2500, 0.3, 0.004), (1200, 0.2, 0.003)], rng))
    return out


def c_ui_page(rng):
    # Page turn: fluttering paper rustle swelling, ending in a soft flap.
    out = zeros(0.25)
    n = ns(0.22)
    rustle = bp(noise(rng, n), 2600, 0.5)
    rustle = mul(rustle, grain_am(rng, n, 140, 0.15))
    env = [math.sin(math.pi * min(1.0, i / n)) ** 1.5 for i in range(n)]
    add(out, mul(rustle, env), 0, 1.0)
    add(out, lp(burst(rng, 0.04, 0.008), 600), 0.19, 0.8)
    return out


def c_ui_pause(rng):
    # Muffled hush (a breath through paper).
    n = ns(0.12)
    h = lp(noise(rng, n), 900)
    env = [math.sin(math.pi * i / n) ** 2 for i in range(n)]
    return mul(h, env)


def c_typewriter(rng):
    # One key clack: sharp click, typebar ring, carriage thud.
    out = zeros(0.08)
    add(out, hp(burst(rng, 0.004, 0.0006), 2500), 0, 1.0)
    add(out, modes(ns(0.06), [(1150, 0.35, 0.008), (2650, 0.3, 0.006), (4100, 0.2, 0.004)], rng))
    add(out, thump(0.04, 190, 140, 0.008), 0.004, 0.2)
    add(out, hp(burst(rng, 0.003, 0.0005), 1500), 0.012, 0.4)
    return out


def c_stamp(rng):
    # Rubber stamp: dull thud plus paper slap.
    out = zeros(0.35)
    add(out, thump(0.3, 92, 55, 0.05), 0, 1.0)
    add(out, lp(burst(rng, 0.1, 0.018, 0.001), 450), 0, 1.0)
    add(out, bp(burst(rng, 0.02, 0.004), 2200, 0.6), 0.002, 0.7)
    return out


def c_win(rng):
    # Short brass flourish (G4 C5 E5 -> G5 chord) over a snare roll, with a stamp thud.
    out = zeros(2.0)
    t = 0.0
    k = 0
    while t < 0.6:
        add(out, snare_stroke(rng, (0.3 + 0.5 * t / 0.6) * (1.0 if k % 2 else 0.8)), t)
        t += 1 / 26.0
        k += 1
    add(out, snare_stroke(rng, 1.2), 0.62)
    for at, f, d in [(0.1, 392.0, 0.17), (0.27, 523.25, 0.17), (0.44, 659.26, 0.17)]:
        add(out, brass(d, f, attack=0.03, release=0.04), at, 0.28)
    add(out, brass(1.3, 783.99, attack=0.04, release=0.35, vib=0.006), 0.62, 0.28)
    add(out, brass(1.3, 523.25, attack=0.05, release=0.35), 0.62, 0.18)
    add(out, brass(1.3, 261.63, attack=0.05, release=0.35, hmax=8), 0.62, 0.18)
    add(out, thump(0.3, 90, 55, 0.05), 0.62, 0.5)
    add(out, lp(burst(rng, 0.1, 0.018), 450), 0.62, 0.5)
    return lp(out, 7000)


def c_lose(rng):
    # Muted, falling phrase: G4 F4 Eb4 -> C4 with a droop, dark and soft.
    out = zeros(2.3)
    notes = [(0.0, 392.0, 0.38, 0.0), (0.42, 349.23, 0.38, 0.0), (0.84, 311.13, 0.38, 0.0),
             (1.26, 261.63, 0.95, -0.035)]
    for at, f, d, bend in notes:
        tone = brass(d, f, attack=0.06, release=0.2 if d > 0.5 else 0.08, hmax=8, fmax=2200,
                     bright=0.6, vib=0.004 if d > 0.5 else 0.0, bend=bend)
        add(out, lp(tone, 1100), at, 0.5)
    add(out, thump(0.5, 70, 45, 0.12), 1.26, 0.35)
    return out


#            name             fn                 kind    peak dB  (min s, max s)
CLIPS = [
    ("engine_loop",    c_engine_loop,    "loop", -3.0, (1.0, 2.0)),
    ("player_gun",     c_player_gun,     "shot", -3.0, (0.05, 0.5)),
    ("bomb_release",   c_bomb_release,   "shot", -3.0, (0.1, 0.5)),
    ("bomb_impact",    c_bomb_impact,    "shot", -3.0, (0.2, 0.5)),
    ("hit_enemy",      c_hit_enemy,      "shot", -3.0, (0.03, 0.5)),
    ("hit_player",     c_hit_player,     "shot", -3.0, (0.1, 0.5)),
    ("enemy_gun",      c_enemy_gun,      "shot", -6.0, (0.05, 0.5)),
    ("flak_fire",      c_flak_fire,      "shot", -3.0, (0.1, 0.5)),
    ("flak_burst",     c_flak_burst,     "shot", -3.0, (0.1, 0.5)),
    ("explosion",      c_explosion,      "shot", -3.0, (0.8, 1.2)),
    ("explosion_big",  c_explosion_big,  "shot", -3.0, (2.0, 3.0)),
    ("zeppelin_hum",   c_zeppelin_hum,   "loop", -3.0, (1.0, 2.0)),
    ("smoke_crackle",  c_smoke_crackle,  "loop", -3.0, (1.0, 2.0)),
    ("crash",          c_crash,          "shot", -3.0, (0.8, 1.2)),
    ("pickup",         c_pickup,         "shot", -3.0, (0.1, 0.5)),
    ("damage_warning", c_damage_warning, "shot", -3.0, (0.2, 0.6)),
    ("ui_click",       c_ui_click,       "ui",  -13.0, (0.02, 0.12)),
    ("ui_page",        c_ui_page,        "ui",  -13.0, (0.2, 0.3)),
    ("ui_pause",       c_ui_pause,       "ui",  -13.0, (0.05, 0.12)),
    ("typewriter",     c_typewriter,     "ui",  -13.0, (0.03, 0.12)),
    ("stamp",          c_stamp,          "shot", -6.0, (0.2, 0.5)),
    ("win",            c_win,            "shot", -3.0, (1.5, 2.5)),
    ("lose",           c_lose,           "shot", -6.0, (1.5, 2.5)),
]


# ---------------------------------------------------------------- mastering
def master(x, kind, peak_db):
    n = len(x)
    if kind == "loop":
        m = sum(x) / n
        y = [v - m for v in x]
    else:
        # DC blocker (one-pole HP ~20 Hz), then remove the residual mean, then fades.
        r = 1.0 - TAU2 * 20 / SR
        y = [0.0] * n
        px = py = 0.0
        for i, v in enumerate(x):
            py = v - px + r * py
            px = v
            y[i] = py
        m = sum(y) / n
        y = [v - m for v in y]
        fi, fo = ns(0.002), ns(0.010)
        for i in range(fi):
            y[i] *= 0.5 - 0.5 * math.cos(math.pi * i / fi)
        for i in range(fo):
            y[n - 1 - i] *= 0.5 - 0.5 * math.cos(math.pi * i / fo)
    pk = max(abs(v) for v in y) or 1.0
    g = 10 ** (peak_db / 20.0) / pk
    a = array.array("h", (max(-32768, min(32767, int(round(v * g * 32767)))) for v in y))
    return a


def to_wav_bytes(a):
    if sys.byteorder != "little":
        a = array.array("h", a)
        a.byteswap()
    buf = io.BytesIO()
    with wave.open(buf, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(a.tobytes())
    return buf.getvalue()


# ---------------------------------------------------------------- analysis
def db(v):
    return 20 * math.log10(v) if v > 0 else -120.0


def analyse(a, kind):
    x = [v / 32768.0 for v in a]
    n = len(x)
    pk = max(abs(v) for v in x)
    rms = math.sqrt(sum(v * v for v in x) / n)
    dc = sum(x) / n
    tot = sum(v * v for v in x) or 1e-12
    lo = lp(lp(x, 300), 300)
    hi = hp(hp(x, 3000), 3000)
    el = sum(v * v for v in lo) / tot
    eh = sum(v * v for v in hi) / tot
    em = max(0.0, 1.0 - el - eh)
    # Ringing: energy above 2 kHz left after the first 15 ms (a struck-metal 'tin' tail).
    h2 = hp(hp(x, 2000), 2000)
    ring = sum(v * v for v in h2[ns(0.015):]) / tot
    diffs = sorted(abs(x[i + 1] - x[i]) for i in range(n - 1))
    p99 = diffs[int(0.99 * (len(diffs) - 1))]
    seam = abs(x[0] - x[-1]) if kind == "loop" else None
    return dict(dur=n / SR, peak=db(pk), rms=db(rms), dc=dc, first=a[0], last=a[-1],
                seam=seam, p99=p99, low=el, mid=em, high=eh, ring=ring)


# Autofire rate (shots/s) for the burst-mix check; player.fireRate in Balance.json is 8.
AUTOFIRE = {"player_gun": 8.0, "enemy_gun": 8.0}


def burst_peak(a, rate, sec=1.0):
    """Peak dBFS of `sec` s of the clip retriggered at `rate`/s, summed (no limiter)."""
    n = ns(sec) + len(a)
    mix = [0] * n
    k = 0
    while k / rate < sec - 1e-9:
        o = ns(k / rate)
        for i, v in enumerate(a):
            mix[o + i] += v
        k += 1
    return db(max(abs(v) for v in mix) / 32768.0)


def validate(name, kind, peak_db, rng_dur, r):
    errs = []
    lo, hi = rng_dur
    if not (lo - 1e-6 <= r["dur"] <= hi + 1e-6):
        errs.append(f"duration {r['dur']:.3f}s outside {lo}-{hi}s")
    if abs(r["peak"] - peak_db) > 0.3:
        errs.append(f"peak {r['peak']:.2f} dBFS, want {peak_db}")
    if abs(r["dc"]) > 0.001:
        errs.append(f"DC offset {r['dc']:.5f}")
    if kind == "loop":
        if r["seam"] > max(r["p99"], 1e-4):
            errs.append(f"loop seam jump {r['seam']:.4f} > p99 step {r['p99']:.4f}")
    elif r["first"] != 0 or r["last"] != 0:
        errs.append(f"first/last sample {r['first']}/{r['last']} not 0 (fade)")
    return errs


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--check", action="store_true", help="verify files on disk, write nothing")
    ap.add_argument("--only", nargs="*", help="limit to these clip names")
    args = ap.parse_args()

    if not args.check:
        OUT.mkdir(parents=True, exist_ok=True)
    errors = 0
    total = 0
    print(f"{'clip':15} {'kind':4} {'dur s':>6} {'peak':>6} {'rms':>6} {'dc':>8} "
          f"{'first':>6} {'last':>6} {'seam':>7} {'low%':>5} {'mid%':>5} {'high%':>5} "
          f"{'ring%':>5}  status")
    names = {c[0] for c in CLIPS}
    bursts = []
    for name, fn, kind, peak_db, dur in CLIPS:
        if args.only and name not in args.only:
            continue
        rng = random.Random(f"{SEED}:{name}")
        a = master(fn(rng), kind, peak_db)
        data = to_wav_bytes(a)
        total += len(data)
        r = analyse(a, kind)
        errs = validate(name, kind, peak_db, dur, r)
        path = OUT / f"{name}.wav"
        if args.check:
            if not path.exists():
                errs.append("missing on disk")
            elif path.read_bytes() != data:
                errs.append("file on disk differs from generator output")
        else:
            path.write_bytes(data)
        seam = f"{r['seam']:.4f}" if r["seam"] is not None else "-"
        status = "OK" if not errs else "ERROR: " + "; ".join(errs)
        errors += len(errs)
        print(f"{name:15} {kind:4} {r['dur']:6.3f} {r['peak']:6.1f} {r['rms']:6.1f} "
              f"{r['dc']:8.5f} {r['first']:6d} {r['last']:6d} {seam:>7} "
              f"{100 * r['low']:5.1f} {100 * r['mid']:5.1f} {100 * r['high']:5.1f} "
              f"{100 * r['ring']:5.1f}  {status}")
        if name in AUTOFIRE:
            bursts.append((name, burst_peak(a, AUTOFIRE[name])))
    for name, pk in bursts:
        flag = "OK" if pk <= -0.5 else "ERROR: autofire burst clips"
        if pk > -0.5:
            errors += 1
        print(f"autofire {name}: 1 s at {AUTOFIRE[name]:g} shots/s, mixed peak {pk:.1f} dBFS  {flag}")
    if OUT.exists() and not args.only:
        for p in sorted(OUT.glob("*.wav")):
            if p.stem not in names:
                print(f"{p.stem:15} ERROR: unknown clip (not in CLIPS)")
                errors += 1
    print(f"total {total / 1e6:.2f} MB, {errors} error(s)"
          + (" (check mode, nothing written)" if args.check else ""))
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
