#!/usr/bin/env python3
"""
EcoQuest CC0 Audio Generator & Mastering Tool
Generates 100% original CC0 music loops, ambient beds, UI cues, gameplay SFX,
and 3D traffic sounds using pure Python (numpy/wave) + ffmpeg mastering.
All outputs are seamless, DC-free, peak-limited, and consistently leveled.
"""

import math
import os
import pathlib
import random
import shutil
import subprocess
import wave
import numpy as np

SR = 44100
SEED = 20261114
OUT_ROOT = pathlib.Path(__file__).resolve().parent.parent  # Assets/EcoQuest/Audio
FFMPEG = shutil.which("ffmpeg") or "ffmpeg"


# ---------------------------------------------------------------------------
# DSP Building Blocks
# ---------------------------------------------------------------------------

def db_to_lin(db: float) -> float:
    return 10.0 ** (db / 20.0)


def normalize_peak(x: np.ndarray, target_dbfs: float = -3.0) -> np.ndarray:
    x = x - np.mean(x, axis=0, keepdims=True)
    peak = float(np.max(np.abs(x)))
    if peak < 1e-8:
        return x
    return x * (db_to_lin(target_dbfs) / peak)


def biquad_lowpass(x: np.ndarray, cutoff_hz: float, q: float = 0.707, sr: int = SR) -> np.ndarray:
    cutoff_hz = max(20.0, min(cutoff_hz, sr * 0.45))
    w0 = 2.0 * math.pi * cutoff_hz / sr
    cos_w0 = math.cos(w0)
    alpha = math.sin(w0) / (2.0 * q)
    b0 = (1.0 - cos_w0) * 0.5
    b1 = 1.0 - cos_w0
    b2 = (1.0 - cos_w0) * 0.5
    a0 = 1.0 + alpha
    a1 = -2.0 * cos_w0
    a2 = 1.0 - alpha
    b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0

    def _flt(ch: np.ndarray) -> np.ndarray:
        y = np.empty_like(ch)
        x1 = x2 = y1 = y2 = 0.0
        for i in range(len(ch)):
            x0 = float(ch[i])
            y0 = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2
            y[i] = y0
            x2, x1 = x1, x0
            y2, y1 = y1, y0
        return y

    if x.ndim == 1:
        return _flt(x)
    return np.column_stack([_flt(x[:, c]) for c in range(x.shape[1])])


def soft_one_pole_lpf(x: np.ndarray, cutoff_hz: float, sr: int = SR) -> np.ndarray:
    alpha = 1.0 - math.exp(-2.0 * math.pi * cutoff_hz / sr)
    # Vectorized via exponential impulse approximation for short arrays or fast loop for 1D/2D
    if x.ndim == 1:
        y = np.empty_like(x)
        prev = 0.0
        for i in range(len(x)):
            prev += alpha * (float(x[i]) - prev)
            y[i] = prev
        return y
    return np.column_stack([soft_one_pole_lpf(x[:, c], cutoff_hz, sr) for c in range(x.shape[1])])


def one_pole_hpf(x: np.ndarray, cutoff_hz: float, sr: int = SR) -> np.ndarray:
    return x - soft_one_pole_lpf(x, cutoff_hz, sr)


def pink_noise(n: int, rng: np.random.Generator) -> np.ndarray:
    # Paul Kellet Voss-McCartney approximation vectorized
    white = rng.standard_normal(n)
    b = np.zeros(7)
    out = np.empty(n, dtype=np.float64)
    for i in range(n):
        w = float(white[i])
        b[0] = 0.99886 * b[0] + w * 0.0555179
        b[1] = 0.99332 * b[1] + w * 0.0750759
        b[2] = 0.96900 * b[2] + w * 0.1538520
        b[3] = 0.86650 * b[3] + w * 0.3104856
        b[4] = 0.55000 * b[4] + w * 0.5329522
        b[5] = -0.7616 * b[5] - w * 0.0168980
        out[i] = b[0] + b[1] + b[2] + b[3] + b[4] + b[5] + b[6] + w * 0.5362
        b[6] = w * 0.115926
    out /= (np.std(out) + 1e-8)
    return out


def midi_to_hz(midi: float) -> float:
    return 440.0 * (2.0 ** ((midi - 69.0) / 12.0))


def fade_edges(x: np.ndarray, fade_in_ms: float = 3.0, fade_out_ms: float = 10.0) -> np.ndarray:
    y = x.copy()
    fi = min(len(y) // 2, int(SR * fade_in_ms / 1000.0))
    fo = min(len(y) // 2, int(SR * fade_out_ms / 1000.0))
    if fi > 0:
        w = 0.5 - 0.5 * np.cos(np.pi * np.arange(fi) / fi)
        if y.ndim == 2:
            w = w[:, None]
        y[:fi] *= w
    if fo > 0:
        w = 0.5 + 0.5 * np.cos(np.pi * np.arange(fo) / fo)
        if y.ndim == 2:
            w = w[:, None]
        y[-fo:] *= w
    return y


def add_wrap(buf: np.ndarray, start_idx: int, note: np.ndarray, pan: float = 0.0, gain: float = 1.0) -> None:
    """Adds mono or stereo clip into circular stereo loop buffer cleanly (zero seam pop)."""
    n_loop = len(buf)
    n_note = len(note)
    if note.ndim == 1:
        l_gain = math.cos((pan + 1.0) * math.pi * 0.25) * gain
        r_gain = math.sin((pan + 1.0) * math.pi * 0.25) * gain
        stereo = np.column_stack([note * l_gain, note * r_gain])
    else:
        stereo = note * gain
    idxs = (np.arange(n_note) + start_idx) % n_loop
    np.add.at(buf, idxs, stereo)


# ---------------------------------------------------------------------------
# Musical Instruments (Warm Cozy Acoustic Palette)
# ---------------------------------------------------------------------------

def marimba_note(midi: float, dur: float = 0.65, brightness: float = 0.45) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = midi_to_hz(midi)
    atk = np.minimum(1.0, t / 0.004)
    # Warm wooden fundamental + octave + muted 4th/10th partials
    p1 = np.sin(2.0 * math.pi * f * t) * np.exp(-5.2 * t)
    p2 = 0.28 * brightness * np.sin(2.0 * math.pi * (f * 3.98) * t) * np.exp(-18.0 * t)
    p3 = 0.14 * np.sin(2.0 * math.pi * (f * 2.0) * t) * np.exp(-9.0 * t)
    # Tiny felt mallet knock
    knock = 0.12 * brightness * np.sin(2.0 * math.pi * (f * 6.2) * t) * np.exp(-95.0 * t)
    y = atk * (p1 + p2 + p3 + knock)
    return fade_edges(y, 2.0, 25.0)


def kalimba_note(midi: float, dur: float = 0.85, rng: np.random.Generator = None) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = midi_to_hz(midi)
    atk = np.minimum(1.0, t / 0.005)
    p1 = np.sin(2.0 * math.pi * f * t) * np.exp(-4.2 * t)
    p2 = 0.35 * np.sin(2.0 * math.pi * (f * 2.0) * t) * np.exp(-8.5 * t)
    p3 = 0.10 * np.sin(2.0 * math.pi * (f * 2.98) * t) * np.exp(-16.0 * t)
    # Soft thumb transient
    th = np.sin(2.0 * math.pi * min(1800.0, f * 5.1) * t) * np.exp(-75.0 * t) * 0.08
    return fade_edges(atk * (p1 + p2 + p3 + th), 2.5, 30.0)


def bass_pluck_note(midi: float, dur: float = 0.9) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = midi_to_hz(midi)
    atk = np.minimum(1.0, t / 0.012)
    y = (
        0.75 * np.sin(2.0 * math.pi * f * t) * np.exp(-2.8 * t)
        + 0.30 * np.sin(2.0 * math.pi * (f * 2.0) * t) * np.exp(-6.5 * t)
        + 0.10 * np.sin(2.0 * math.pi * (f * 3.0) * t) * np.exp(-12.0 * t)
    )
    return fade_edges(atk * y, 5.0, 40.0)


def warm_pad_chord(midis: list[float], dur: float) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    env = np.sin(math.pi * t / dur) ** 1.35
    sig = np.zeros(n)
    for m in midis:
        f = midi_to_hz(m)
        vib = 1.0 + 0.0018 * np.sin(2.0 * math.pi * 4.2 * t + m)
        phase = 2.0 * math.pi * f * np.cumsum(vib) / SR
        sig += 0.65 * np.sin(phase) + 0.22 * np.sin(2.0 * phase) + 0.06 * np.sin(3.0 * phase)
    sig /= max(1, len(midis))
    return sig * env * 0.45


def soft_flute_note(midi: float, dur: float, rng: np.random.Generator) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    f = midi_to_hz(midi)
    atk = np.minimum(1.0, t / 0.065)
    rel = np.minimum(1.0, np.maximum(0.0, (dur - t) / 0.12))
    vib_depth = np.minimum(1.0, t / 0.18) * 0.004
    vib = 1.0 + vib_depth * np.sin(2.0 * math.pi * 5.2 * t)
    phase = 2.0 * math.pi * f * np.cumsum(vib) / SR
    tone = 0.80 * np.sin(phase) + 0.18 * np.sin(2.0 * phase) + 0.04 * np.sin(3.0 * phase)
    breath = one_pole_hpf(soft_one_pole_lpf(rng.standard_normal(n), 2800.0), 800.0) * 0.04
    return (tone + breath) * atk * rel


def shaker_hit(accent: float, rng: np.random.Generator) -> np.ndarray:
    dur = 0.065
    n = int(SR * dur)
    t = np.arange(n) / SR
    noise = one_pole_hpf(soft_one_pole_lpf(rng.standard_normal(n), 7500.0), 3200.0)
    env = np.minimum(1.0, t / 0.008) * np.exp(-55.0 * t)
    return noise * env * (0.35 + 0.35 * accent)


def add_simple_stereo_reverb(stereo: np.ndarray, decay: float = 0.32, mix: float = 0.18) -> np.ndarray:
    """Wrap-safe circular multi-tap Schroeder-style early+late sheen for music loops."""
    n = len(stereo)
    wet = np.zeros_like(stereo)
    taps = [
        (int(SR * 0.029), 0.42, 0),
        (int(SR * 0.043), 0.34, 1),
        (int(SR * 0.067), 0.28, 0),
        (int(SR * 0.097), 0.22, 1),
        (int(SR * 0.139), 0.16, 0),
        (int(SR * 0.173), 0.13, 1),
    ]
    for d, g, ch in taps:
        shifted_l = np.roll(stereo[:, ch], d)
        shifted_r = np.roll(stereo[:, 1 - ch], d + int(SR * 0.007))
        wet[:, 0] += shifted_l * (g * decay * 1.8)
        wet[:, 1] += shifted_r * (g * decay * 1.8)
    wet = soft_one_pole_lpf(wet, 3600.0)
    return (1.0 - mix) * stereo + mix * wet


# ---------------------------------------------------------------------------
# Write WAV + Optional OGG Mastering
# ---------------------------------------------------------------------------

def save_wav(path: pathlib.Path, data: np.ndarray, sr: int = SR) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    x = np.clip(data, -0.99, 0.99)
    if x.ndim == 1:
        channels = 1
        pcm = (x * 32767.0).astype("<i2")
    else:
        channels = x.shape[1]
        pcm = (x * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as wf:
        wf.setnchannels(channels)
        wf.setsampwidth(2)
        wf.setframerate(sr)
        wf.writeframes(pcm.tobytes())


def save_ogg_from_array(path: pathlib.Path, data: np.ndarray, target_lufs: float = -16.0, sr: int = SR) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp_wav = path.with_suffix(".tmp.wav")
    save_wav(tmp_wav, data, sr)
    # Apply gentle two-pass or direct soft scaling + Vorbis encode while keeping sample length intact
    cmd = [
        FFMPEG, "-y", "-hide_banner", "-loglevel", "error",
        "-i", str(tmp_wav),
        "-c:a", "libvorbis", "-q:a", "6",
        str(path)
    ]
    subprocess.run(cmd, check=True)
    tmp_wav.unlink(missing_ok=True)


# ---------------------------------------------------------------------------
# Generators: Music Loops
# ---------------------------------------------------------------------------

def generate_music(rng: np.random.Generator) -> None:
    print("Generating Music loops...")

    # 1. Main Menu Theme: Cozy C-major acoustic kalimba + warm pad (8 bars @ 88 BPM)
    bpm = 88.0
    beat = 60.0 / bpm
    step_s = beat / 2.0  # 8th note
    n_bars = 8
    total_beats = n_bars * 4
    total_len = int(round(total_beats * beat * SR))
    buf = np.zeros((total_len, 2), dtype=np.float64)

    chords = [
        ([60, 64, 67, 71], 48),  # Cmaj7
        ([57, 60, 64, 67], 45),  # Am7
        ([53, 57, 60, 64], 41),  # Fmaj7
        ([55, 59, 62, 64], 43),  # G6
        ([60, 64, 67, 71], 48),
        ([57, 60, 64, 67], 45),
        ([50, 53, 57, 60], 38),  # Dm7
        ([55, 59, 62, 67], 43),  # G
    ]
    arp_pattern = [0, 1, 2, 3, 2, 1, 2, 3]
    melody_bar_steps = {
        0: [(0, 76, 0.9), (3, 74, 0.8), (4, 72, 0.9)],
        1: [(0, 72, 0.85), (3, 71, 0.8), (5, 69, 0.8)],
        2: [(0, 69, 0.85), (2, 72, 0.8), (4, 76, 0.9), (6, 74, 0.8)],
        3: [(0, 74, 0.9), (4, 71, 0.75)],
        4: [(0, 79, 0.9), (3, 76, 0.85), (5, 74, 0.8)],
        5: [(0, 76, 0.85), (2, 74, 0.8), (4, 72, 0.85)],
        6: [(0, 72, 0.85), (2, 74, 0.8), (4, 76, 0.85), (6, 77, 0.8)],
        7: [(0, 74, 0.9), (4, 71, 0.8), (6, 67, 0.7)],
    }

    for bar, (voicing, bass_m) in enumerate(chords):
        bar_t = bar * 4.0 * beat
        # Warm pad across 1 bar
        add_wrap(buf, int(bar_t * SR), warm_pad_chord(voicing, 4.0 * beat), pan=0.0, gain=0.28)
        # Soft bass on beat 1 & beat 3
        add_wrap(buf, int(bar_t * SR), bass_pluck_note(bass_m, 1.4), pan=-0.1, gain=0.38)
        add_wrap(buf, int((bar_t + 2.0 * beat) * SR), bass_pluck_note(bass_m + 7, 1.0), pan=0.1, gain=0.22)
        # Kalimba 8th-note arpeggio
        for s_idx, v_idx in enumerate(arp_pattern):
            t_sec = bar_t + s_idx * step_s
            note_m = voicing[v_idx] + (12 if s_idx % 4 == 3 else 0)
            vel = 0.85 if s_idx % 2 == 0 else 0.62
            add_wrap(buf, int(t_sec * SR), kalimba_note(note_m, 0.85, rng), pan=-0.25 if s_idx % 2 == 0 else 0.25, gain=0.20 * vel)
        # Gentle lead kalimba melody
        for s_idx, m_note, g in melody_bar_steps[bar]:
            t_sec = bar_t + s_idx * step_s
            add_wrap(buf, int(t_sec * SR), marimba_note(m_note, 0.95, 0.55), pan=0.08, gain=0.32 * g)

    buf = add_simple_stereo_reverb(buf, decay=0.35, mix=0.22)
    buf = normalize_peak(buf, -4.5)
    save_ogg_from_array(OUT_ROOT / "Music/bgm_main_menu.ogg", buf)

    # 2. Forest Theme: Peaceful F-major nature marimba & flute breeze (12 bars @ 92 BPM)
    bpm = 92.0
    beat = 60.0 / bpm
    step_s = beat / 2.0
    n_bars = 12
    total_len = int(round(n_bars * 4 * beat * SR))
    buf = np.zeros((total_len, 2), dtype=np.float64)

    f_prog = [
        ([65, 69, 72, 76], 41),  # Fmaj7
        ([62, 65, 69, 72], 38),  # Dm7
        ([58, 62, 65, 69], 46),  # Bbmaj7
        ([60, 65, 67, 72], 48),  # Csus4
        ([65, 69, 72, 76], 41),  # Fmaj7
        ([64, 67, 72, 76], 40),  # C/E
        ([62, 65, 69, 72], 38),  # Dm7
        ([60, 64, 67, 72], 48),  # C
        ([58, 62, 65, 69], 46),  # Bbmaj7
        ([65, 69, 72, 77], 41),  # F
        ([67, 70, 74, 77], 43),  # Gm7
        ([60, 64, 67, 70], 48),  # C7
    ]
    forest_flute = {
        0: (0, 77, 1.5),
        1: (2, 76, 1.1),
        2: (0, 74, 1.6),
        3: (2, 72, 1.2),
        4: (0, 81, 1.4),
        5: (2, 79, 1.3),
        6: (0, 77, 1.2),
        7: (4, 76, 1.0),
        8: (0, 74, 1.4),
        9: (2, 77, 1.4),
        10: (0, 79, 1.3),
        11: (2, 76, 1.5),
    }

    for bar, (voicing, bass_m) in enumerate(f_prog):
        bar_t = bar * 4.0 * beat
        add_wrap(buf, int(bar_t * SR), warm_pad_chord(voicing, 4.0 * beat), pan=0.0, gain=0.22)
        add_wrap(buf, int(bar_t * SR), bass_pluck_note(bass_m, 1.5), pan=-0.05, gain=0.36)
        add_wrap(buf, int((bar_t + 2.5 * beat) * SR), bass_pluck_note(bass_m + 7, 0.9), pan=0.08, gain=0.20)

        for s_idx in range(8):
            t_sec = bar_t + s_idx * step_s
            note_m = voicing[(s_idx * 3) % len(voicing)]
            pan = -0.35 + 0.7 * (s_idx % 2)
            add_wrap(buf, int(t_sec * SR), marimba_note(note_m, 0.65, 0.42), pan=pan, gain=0.18 if s_idx % 2 else 0.25)

        if bar in forest_flute:
            s_idx, f_m, f_dur_beats = forest_flute[bar]
            t_sec = bar_t + s_idx * step_s
            add_wrap(buf, int(t_sec * SR), soft_flute_note(f_m, f_dur_beats * beat, rng), pan=0.12, gain=0.23)

    buf = add_simple_stereo_reverb(buf, decay=0.38, mix=0.24)
    buf = normalize_peak(buf, -5.0)
    save_ogg_from_array(OUT_ROOT / "Music/bgm_forest.ogg", buf)

    # 3. City Theme: Upbeat sunny G-major eco-town marimba + plucked guitar + shaker (8 bars @ 108 BPM)
    bpm = 108.0
    beat = 60.0 / bpm
    step_s = beat / 2.0
    n_bars = 8
    total_len = int(round(n_bars * 4 * beat * SR))
    buf = np.zeros((total_len, 2), dtype=np.float64)

    c_prog = [
        ([67, 71, 74, 78], 43),  # Gmaj7
        ([64, 67, 71, 74], 40),  # Em7
        ([60, 64, 67, 74], 48),  # Cmaj9
        ([62, 66, 69, 72], 50),  # D7
        ([67, 71, 74, 78], 43),
        ([64, 67, 71, 76], 40),
        ([57, 60, 64, 67], 45),  # Am7
        ([62, 66, 69, 76], 50),  # D6
    ]
    city_mel = [
        [(0, 79), (2, 81), (3, 79), (5, 78), (6, 74)],
        [(0, 76), (2, 79), (4, 78), (6, 74)],
        [(0, 76), (1, 79), (3, 81), (5, 79), (6, 76)],
        [(0, 74), (2, 78), (4, 81), (6, 79)],
        [(0, 83), (2, 81), (4, 79), (6, 76)],
        [(0, 79), (2, 78), (4, 76), (6, 74)],
        [(0, 72), (2, 74), (4, 76), (6, 79)],
        [(0, 78), (2, 76), (4, 74), (6, 78)],
    ]

    for bar, (voicing, bass_m) in enumerate(c_prog):
        bar_t = bar * 4.0 * beat
        # Bouncy bass
        for b_off, m_off, g in [(0.0, 0, 0.42), (1.5, 7, 0.25), (2.0, 0, 0.36), (3.0, 12, 0.28)]:
            add_wrap(buf, int((bar_t + b_off * beat) * SR), bass_pluck_note(bass_m + m_off, 0.55), pan=0.0, gain=g)
        # Offbeat strummed chord stabs
        for b_off in (1.0, 3.0):
            for idx, vm in enumerate(voicing):
                add_wrap(buf, int((bar_t + (b_off * beat + idx * 0.012)) * SR), kalimba_note(vm, 0.32, rng), pan=-0.25, gain=0.14)
        # Light shaker 8ths
        for s_idx in range(8):
            acc = 1.0 if s_idx % 4 == 2 else (0.5 if s_idx % 2 == 0 else 0.2)
            add_wrap(buf, int((bar_t + s_idx * step_s) * SR), shaker_hit(acc, rng), pan=0.35, gain=0.13)
        # Cheerful marimba lead
        for s_idx, mm in city_mel[bar]:
            add_wrap(buf, int((bar_t + s_idx * step_s) * SR), marimba_note(mm, 0.48, 0.65), pan=0.10, gain=0.32)

    buf = add_simple_stereo_reverb(buf, decay=0.28, mix=0.16)
    buf = normalize_peak(buf, -4.5)
    save_ogg_from_array(OUT_ROOT / "Music/bgm_city.ogg", buf)


# ---------------------------------------------------------------------------
# Generators: Ambience Loops
# ---------------------------------------------------------------------------

def bird_chirp(base_hz: float, dur: float, sweep: float, rng: np.random.Generator) -> np.ndarray:
    n = int(SR * dur)
    t = np.arange(n) / SR
    env = np.sin(math.pi * t / dur) ** 1.6
    freq = base_hz + sweep * np.sin(math.pi * t / dur) + 60.0 * np.sin(2.0 * math.pi * 24.0 * t)
    phase = 2.0 * math.pi * np.cumsum(freq) / SR
    sig = np.sin(phase) + 0.12 * np.sin(2.0 * phase)
    return sig * env * 0.35


def generate_ambience(rng: np.random.Generator) -> None:
    print("Generating Ambience loops...")

    # 1. Forest Ambience (16s gentle leaf breeze + intermittent birdsong, seamless circular wrap)
    dur = 16.0
    n = int(SR * dur)
    raw_l = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 1100.0), 160.0)
    raw_r = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 1100.0), 160.0)
    # Periodic breeze modulation exact integer cycles across 16s
    t = np.arange(n) / SR
    breeze_l = 0.55 + 0.30 * np.sin(2.0 * math.pi * (2.0 / dur) * t) + 0.15 * np.sin(2.0 * math.pi * (5.0 / dur) * t + 0.6)
    breeze_r = 0.55 + 0.30 * np.sin(2.0 * math.pi * (2.0 / dur) * t + 1.1) + 0.15 * np.sin(2.0 * math.pi * (5.0 / dur) * t + 2.0)

    # Wrap-blend edges over 1.5s to erase any noise-state seam
    blend_n = int(SR * 1.5)
    wind = np.column_stack([raw_l * breeze_l, raw_r * breeze_r])
    alpha = np.linspace(0.0, 1.0, blend_n)[:, None]
    tail_head = wind[:blend_n] * alpha + wind[-blend_n:] * (1.0 - alpha)
    wind[-blend_n:] = wind[-blend_n:] * alpha + wind[:blend_n] * (1.0 - alpha)
    wind[:blend_n] = tail_head
    buf = wind * 0.26

    # Place soft, cheerful forest birds at natural moments
    bird_specs = [
        (1.2, 2350, 0.11, 350, -0.55),
        (1.38, 2550, 0.09, -220, -0.52),
        (4.8, 2850, 0.08, 420, 0.50),
        (5.05, 3100, 0.10, 260, 0.52),
        (9.1, 2200, 0.13, 300, -0.30),
        (9.32, 2480, 0.10, 480, -0.28),
        (13.2, 2700, 0.09, -260, 0.42),
        (13.42, 2900, 0.11, 320, 0.44),
    ]
    for bt, hz, bd, sw, pan in bird_specs:
        add_wrap(buf, int(bt * SR), bird_chirp(hz, bd, sw, rng), pan=pan, gain=0.16)

    buf = add_simple_stereo_reverb(buf, decay=0.45, mix=0.25)
    buf = normalize_peak(buf, -12.0)
    save_ogg_from_array(OUT_ROOT / "Ambience/amb_forest.ogg", buf)

    # 2. City Ambience (16s gentle sunlit town air & soft leaf/park breeze, seamless circular wrap)
    raw_l = soft_one_pole_lpf(pink_noise(n, rng), 420.0)
    raw_r = soft_one_pole_lpf(pink_noise(n, rng), 420.0)
    hiss_l = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 1800.0), 400.0) * 0.25
    hiss_r = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 1800.0), 400.0) * 0.25
    swell = 0.68 + 0.22 * np.sin(2.0 * math.pi * (3.0 / dur) * t)
    city_bed = np.column_stack([(raw_l + hiss_l) * swell, (raw_r + hiss_r) * swell])
    tail_head = city_bed[:blend_n] * alpha + city_bed[-blend_n:] * (1.0 - alpha)
    city_bed[-blend_n:] = city_bed[-blend_n:] * alpha + city_bed[:blend_n] * (1.0 - alpha)
    city_bed[:blend_n] = tail_head

    # Occasional very gentle distant sparrow chirps
    for bt, hz, pan in [(3.5, 2900, -0.4), (3.7, 3050, -0.38), (11.4, 2750, 0.45)]:
        add_wrap(city_bed, int(bt * SR), bird_chirp(hz, 0.08, 280, rng), pan=pan, gain=0.07)

    city_bed = normalize_peak(city_bed, -15.0)
    save_ogg_from_array(OUT_ROOT / "Ambience/amb_city.ogg", city_bed)


# ---------------------------------------------------------------------------
# Generators: UI & Gameplay SFX
# ---------------------------------------------------------------------------

def add_layer(buf: np.ndarray, offset_sec: float, clip: np.ndarray, gain: float = 1.0, sr: int = SR) -> None:
    i0 = max(0, int(round(offset_sec * sr)))
    if i0 >= len(buf):
        return
    count = min(len(clip), len(buf) - i0)
    buf[i0:i0 + count] += clip[:count] * gain


def generate_ui_sfx(rng: np.random.Generator) -> None:
    print("Generating UI SFX...")

    # 1. Button hover (micro soft wooden tap)
    t = np.arange(int(SR * 0.055)) / SR
    hover = (np.sin(2.0 * math.pi * 680.0 * t) + 0.35 * np.sin(2.0 * math.pi * 1020.0 * t)) * np.exp(-95.0 * t)
    save_wav(OUT_ROOT / "UI/ui_button_hover.wav", normalize_peak(fade_edges(hover, 1.5, 10.0), -8.0))

    # 2. Button click (crisp tactile warm wood click)
    t = np.arange(int(SR * 0.095)) / SR
    f_drop = 520.0 * np.exp(-18.0 * t) + 260.0
    click = np.sin(2.0 * math.pi * np.cumsum(f_drop) / SR) * np.exp(-52.0 * t)
    click += 0.25 * soft_one_pole_lpf(rng.standard_normal(len(t)), 2400.0) * np.exp(-140.0 * t)
    save_wav(OUT_ROOT / "UI/ui_button_click.wav", normalize_peak(fade_edges(click, 1.0, 12.0), -4.5))

    # 3. Slider tick
    t = np.arange(int(SR * 0.032)) / SR
    tick = np.sin(2.0 * math.pi * 920.0 * t) * np.exp(-140.0 * t)
    save_wav(OUT_ROOT / "UI/ui_slider_tick.wav", normalize_peak(fade_edges(tick, 1.0, 6.0), -9.0))

    # 4. Modal Open (warm ascending chime-whoosh)
    dur = 0.22
    t = np.arange(int(SR * dur)) / SR
    buf = np.zeros(len(t))
    air = one_pole_hpf(soft_one_pole_lpf(pink_noise(len(t), rng), 2400.0), 400.0) * np.sin(math.pi * t / dur)
    add_layer(buf, 0.0, air, 0.25)
    add_layer(buf, 0.0, kalimba_note(72, dur, rng), 0.55)
    add_layer(buf, 0.055, kalimba_note(79, dur - 0.055, rng), 0.65)
    save_wav(OUT_ROOT / "UI/ui_modal_open.wav", normalize_peak(fade_edges(buf, 2.0, 20.0), -5.0))

    # 5. Modal Close (gentle descending leaf-pop)
    buf = np.zeros(len(t))
    air_c = one_pole_hpf(soft_one_pole_lpf(pink_noise(len(t), rng), 1800.0), 300.0) * np.sin(math.pi * t / dur)
    add_layer(buf, 0.0, air_c, 0.25)
    add_layer(buf, 0.0, kalimba_note(76, dur, rng), 0.50)
    add_layer(buf, 0.05, kalimba_note(69, dur - 0.05, rng), 0.60)
    save_wav(OUT_ROOT / "UI/ui_modal_close.wav", normalize_peak(fade_edges(buf, 2.0, 22.0), -6.0))

    # 6. Panel Whoosh (Map / Quest Tab paper flutter)
    dur = 0.16
    n = int(SR * dur)
    t = np.arange(n) / SR
    rustle = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 2600.0), 500.0) * (np.sin(math.pi * t / dur) ** 1.4)
    pop = np.sin(2.0 * math.pi * 420.0 * t) * np.exp(-32.0 * t) * 0.18
    save_wav(OUT_ROOT / "UI/ui_panel_whoosh.wav", normalize_peak(fade_edges(rustle + pop, 3.0, 20.0), -6.5))

    # 7. Dialogue Typewriter Blip (soft vowel marimba pluck)
    t = np.arange(int(SR * 0.06)) / SR
    blip = (np.sin(2.0 * math.pi * 495.0 * t) + 0.28 * np.sin(2.0 * math.pi * 990.0 * t)) * np.minimum(1.0, t / 0.004) * np.exp(-48.0 * t)
    save_wav(OUT_ROOT / "UI/ui_dialogue_blip.wav", normalize_peak(fade_edges(blip, 1.5, 10.0), -7.5))

    # 8. Notification (G5 -> C6)
    buf = np.zeros(int(SR * 0.44))
    add_layer(buf, 0.0, kalimba_note(79, 0.28, rng), 0.65)
    add_layer(buf, 0.08, marimba_note(84, 0.36, 0.55), 0.85)
    save_wav(OUT_ROOT / "UI/ui_notification.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -4.5))

    # 9. Tutorial Step Complete (C5->E5->G5->C6 sparkle)
    buf = np.zeros(int(SR * 0.70))
    for st, mm, d in [(0.00, 72, 0.32), (0.065, 76, 0.32), (0.13, 79, 0.34), (0.195, 84, 0.45)]:
        add_layer(buf, st, marimba_note(mm, d, 0.65))
    save_wav(OUT_ROOT / "UI/ui_tutorial_step.wav", normalize_peak(fade_edges(buf, 2.0, 30.0), -4.0))

    # 10. Hunger Warning (clear friendly two-tone amber alert A4 -> F4)
    buf = np.zeros(int(SR * 0.42))
    add_layer(buf, 0.0, marimba_note(69, 0.20, 0.60), 0.85)
    add_layer(buf, 0.14, marimba_note(65, 0.26, 0.55), 0.95)
    save_wav(OUT_ROOT / "UI/ui_hunger_warning.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -4.0))

    # 11. Shop Buy (crisp coin double clink + wooden stamp)
    buf = np.zeros(int(SR * 0.46))
    t0 = np.arange(int(SR * 0.08)) / SR
    stamp = np.sin(2.0 * math.pi * 190.0 * t0) * np.exp(-38.0 * t0) * 0.45
    c1 = np.sin(2.0 * math.pi * midi_to_hz(88) * np.arange(int(SR * 0.22)) / SR) * np.exp(-15.0 * np.arange(int(SR * 0.22)) / SR)
    c2 = np.sin(2.0 * math.pi * midi_to_hz(95) * np.arange(int(SR * 0.34)) / SR) * np.exp(-9.0 * np.arange(int(SR * 0.34)) / SR)
    add_layer(buf, 0.0, stamp)
    add_layer(buf, 0.03, c1, 0.65)
    add_layer(buf, 0.11, c2, 0.90)
    save_wav(OUT_ROOT / "UI/ui_shop_buy.wav", normalize_peak(fade_edges(buf, 1.5, 25.0), -3.5))

    # 12. Shop Error / Locked (soft muted low double-woodblock)
    buf = np.zeros(int(SR * 0.26))
    for i, hz in enumerate([220.0, 165.0]):
        tt = np.arange(int(SR * 0.13)) / SR
        w = (np.sin(2.0 * math.pi * hz * tt) + 0.3 * np.sin(2.0 * math.pi * (hz * 1.48) * tt)) * np.exp(-26.0 * tt)
        add_layer(buf, i * 0.095, w)
    save_wav(OUT_ROOT / "UI/ui_shop_error.wav", normalize_peak(fade_edges(buf, 1.5, 20.0), -4.5))

    # 13. Item Use (refreshing sparkle-crunch boost chime)
    buf = np.zeros(int(SR * 0.36))
    crunch = one_pole_hpf(soft_one_pole_lpf(pink_noise(int(SR * 0.12), rng), 3200.0), 800.0) * np.exp(-25.0 * np.arange(int(SR * 0.12)) / SR)
    add_layer(buf, 0.0, crunch, 0.4)
    for idx, mm in enumerate([76, 81, 88]):
        add_layer(buf, 0.03 + idx * 0.055, kalimba_note(mm, 0.24, rng), 0.7)
    save_wav(OUT_ROOT / "UI/ui_item_use.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -4.0))


def generate_gameplay_sfx(rng: np.random.Generator) -> None:
    print("Generating Gameplay SFX...")

    # 1. Footsteps Grass (x4 mono variations)
    for v in range(1, 5):
        dur = 0.135
        n = int(SR * dur)
        t = np.arange(n) / SR
        pitch_var = 0.90 + 0.07 * v
        thud = np.sin(2.0 * math.pi * (85.0 * pitch_var) * t) * np.exp(-36.0 * t) * 0.38
        # Soft granular foliage crunch
        grains = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 3600.0 * pitch_var), 650.0)
        mod = (0.55 + 0.45 * np.sin(2.0 * math.pi * (38.0 + 7.0 * v) * t)) * np.exp(-22.0 * t)
        env = np.minimum(1.0, t / 0.008)
        step = fade_edges((thud + grains * mod * 0.72) * env, 2.0, 18.0)
        save_wav(OUT_ROOT / f"Gameplay/sfx_footstep_grass_{v:02d}.wav", normalize_peak(step, -6.0))

    # 2. Footsteps City Pavement (x4 mono crisp shoe taps)
    for v in range(1, 5):
        dur = 0.11
        n = int(SR * dur)
        t = np.arange(n) / SR
        pitch_var = 0.92 + 0.06 * v
        sole = np.sin(2.0 * math.pi * (145.0 * pitch_var) * np.cumsum(np.exp(-14.0 * t)) / SR * 12.0) * np.exp(-42.0 * t) * 0.55
        tap = np.sin(2.0 * math.pi * (230.0 * pitch_var) * t) * np.exp(-60.0 * t) * 0.40
        grit = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 2400.0), 450.0) * np.exp(-45.0 * t) * 0.35
        step = fade_edges((sole + tap + grit) * np.minimum(1.0, t / 0.003), 1.5, 14.0)
        save_wav(OUT_ROOT / f"Gameplay/sfx_footstep_city_{v:02d}.wav", normalize_peak(step, -5.5))

    # 3. Jump Takeoff
    dur = 0.16
    n = int(SR * dur)
    t = np.arange(n) / SR
    f_up = 160.0 + 260.0 * (t / dur)
    whoosh = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 2000.0), 350.0) * np.sin(math.pi * t / dur) * 0.35
    hop = np.sin(2.0 * math.pi * np.cumsum(f_up) / SR) * np.exp(-20.0 * t) * 0.55
    save_wav(OUT_ROOT / "Gameplay/sfx_jump.wav", normalize_peak(fade_edges(whoosh + hop, 2.0, 20.0), -6.0))

    # 4. Landing Thud (mono)
    dur = 0.16
    n = int(SR * dur)
    t = np.arange(n) / SR
    body = np.sin(2.0 * math.pi * (95.0 * np.exp(-8.0 * t) + 52.0) * t) * np.exp(-26.0 * t) * 0.85
    scuff = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 1900.0), 220.0) * np.exp(-32.0 * t) * 0.45
    save_wav(OUT_ROOT / "Gameplay/sfx_land.wav", normalize_peak(fade_edges(body + scuff, 1.5, 18.0), -4.5))

    # 5. Trash Pickup
    n = int(SR * 0.14)
    t = np.arange(n) / SR
    f_pop = 280.0 + 340.0 * (t / 0.14)
    pop = np.sin(2.0 * math.pi * np.cumsum(f_pop) / SR) * np.exp(-28.0 * t) * 0.75
    crinkle = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 4200.0), 1400.0) * np.exp(-38.0 * t) * 0.30
    save_wav(OUT_ROOT / "Gameplay/sfx_trash_pickup.wav", normalize_peak(fade_edges(pop + crinkle, 2.0, 15.0), -4.5))

    # 6. Trash Drop
    n = int(SR * 0.13)
    t = np.arange(n) / SR
    drop = np.sin(2.0 * math.pi * (165.0 * np.exp(-15.0 * t) + 75.0) * t) * np.exp(-34.0 * t) * 0.80
    rattle = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 2600.0), 600.0) * np.exp(-42.0 * t) * 0.28
    save_wav(OUT_ROOT / "Gameplay/sfx_trash_drop.wav", normalize_peak(fade_edges(drop + rattle, 1.5, 15.0), -5.5))

    # 7. Trash Correct Deposit (bright sparkling E5->B5 bell chime)
    buf = np.zeros(int(SR * 0.46))
    add_layer(buf, 0.0, marimba_note(76, 0.30, 0.70), 0.80)
    add_layer(buf, 0.075, marimba_note(83, 0.38, 0.75), 0.95)
    save_wav(OUT_ROOT / "Gameplay/sfx_trash_correct.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -3.5))

    # 8. Trash Wrong Deposit (gentle playful boing E4 -> Eb4)
    buf = np.zeros(int(SR * 0.34))
    add_layer(buf, 0.0, marimba_note(64, 0.18, 0.45), 0.85)
    add_layer(buf, 0.09, marimba_note(63, 0.24, 0.40), 0.95)
    save_wav(OUT_ROOT / "Gameplay/sfx_trash_wrong.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -4.0))

    # 9. Quiz Correct (G5->B5->D6 crisp sparkle)
    buf = np.zeros(int(SR * 0.50))
    for i, mm in enumerate([79, 83, 86]):
        add_layer(buf, i * 0.065, marimba_note(mm, 0.34, 0.72))
    save_wav(OUT_ROOT / "Gameplay/sfx_quiz_correct.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -3.5))

    # 10. Quiz Wrong (gentle low two-step D4->A3)
    buf = np.zeros(int(SR * 0.36))
    for i, mm in enumerate([62, 57]):
        add_layer(buf, i * 0.11, marimba_note(mm, 0.24, 0.35))
    save_wav(OUT_ROOT / "Gameplay/sfx_quiz_wrong.wav", normalize_peak(fade_edges(buf, 2.0, 25.0), -4.5))

    # 11. Coin Earn (sparkling coin bell B5->E6)
    buf = np.zeros(int(SR * 0.35))
    for i, (mm, d, g) in enumerate([(83, 0.18, 0.72), (88, 0.28, 1.0)]):
        tt = np.arange(int(SR * d)) / SR
        hz = midi_to_hz(mm)
        s = (np.sin(2.0 * math.pi * hz * tt) + 0.25 * np.sin(2.0 * math.pi * (hz * 2.0) * tt)) * np.exp(-11.0 * tt) * g
        add_layer(buf, i * 0.055, s)
    save_wav(OUT_ROOT / "Gameplay/sfx_coin_earn.wav", normalize_peak(fade_edges(buf, 1.5, 25.0), -3.5))

    # 12. Quest Complete Fanfare (C5->E5->G5->C6 triumphant eco chord)
    buf = np.zeros(int(SR * 1.15))
    for st, mm, d, g in [(0.00, 72, 0.35, 0.75), (0.11, 76, 0.35, 0.80), (0.22, 79, 0.38, 0.85), (0.34, 84, 0.78, 1.00)]:
        add_layer(buf, st, marimba_note(mm, d, 0.75), g)
    # Warm sustained chord halo at resolution
    add_layer(buf, 0.34, warm_pad_chord([72, 76, 79, 84], 0.75), 0.35)
    save_wav(OUT_ROOT / "Gameplay/sfx_quest_complete.wav", normalize_peak(fade_edges(buf, 2.5, 45.0), -3.0))

    # 13. Litterbug Throw Whoosh (mono)
    n = int(SR * 0.18)
    t = np.arange(n) / SR
    whoosh = one_pole_hpf(soft_one_pole_lpf(pink_noise(n, rng), 2400.0), 400.0) * (np.sin(math.pi * t / 0.18) ** 1.3) * 0.65
    f_sw = 320.0 * np.exp(-8.0 * t) + 120.0
    whoosh += np.sin(2.0 * math.pi * np.cumsum(f_sw) / SR) * np.exp(-16.0 * t) * 0.35
    save_wav(OUT_ROOT / "Gameplay/sfx_litter_throw.wav", normalize_peak(fade_edges(whoosh, 2.0, 20.0), -5.0))

    # 14. Litterbug Land Bounce (mono)
    n = int(SR * 0.14)
    t = np.arange(n) / SR
    plop = np.sin(2.0 * math.pi * (210.0 * np.exp(-14.0 * t) + 105.0) * t) * np.exp(-32.0 * t) * 0.85
    save_wav(OUT_ROOT / "Gameplay/sfx_litter_land.wav", normalize_peak(fade_edges(plop, 1.5, 15.0), -5.0))

    # 15. Loading Screen Ready Chime
    buf = np.zeros(int(SR * 0.56))
    for i, mm in enumerate([76, 79, 84]):
        add_layer(buf, i * 0.08, kalimba_note(mm, 0.38, rng), 0.85)
    save_wav(OUT_ROOT / "Gameplay/sfx_loading_ready.wav", normalize_peak(fade_edges(buf, 2.0, 35.0), -4.5))


def generate_traffic_sfx(rng: np.random.Generator) -> None:
    print("Generating City Traffic 3D SFX (mono)...")
    sr_tr = 22050  # lightweight 3D world sources

    # 1. Friendly Car Double Horn ("Beep-Beep!", smooth attack/decay, zero clicks)
    dur = 0.34
    n = int(sr_tr * dur)
    t = np.arange(n) / sr_tr
    def beep_env(ts, td, s):
        a = np.clip((ts - s) / 0.012, 0.0, 1.0)
        r = np.clip((s + td - ts) / 0.018, 0.0, 1.0)
        return np.where((ts >= s) & (ts <= s + td), a * r, 0.0)

    env = beep_env(t, 0.11, 0.005) + beep_env(t, 0.13, 0.165)
    sig = 0.62 * np.sin(2.0 * math.pi * 523.25 * t) + 0.38 * np.sin(2.0 * math.pi * 659.25 * t) + 0.08 * np.sin(2.0 * math.pi * 1046.5 * t)
    save_wav(OUT_ROOT / "Traffic/sfx_car_horn_friendly.wav", normalize_peak(sig * env, -5.0), sr=sr_tr)

    # 2. Car Engine Hum (2.0s seamless integer-harmonic warm purr + gentle filtered air)
    dur = 2.0
    n = int(sr_tr * dur)
    t = np.arange(n) / sr_tr
    # Integer frequencies across 2.0s -> period-exact loop (zero seam step!)
    hum = (
        0.52 * np.sin(2.0 * math.pi * 55.0 * t)
        + 0.28 * np.sin(2.0 * math.pi * 110.0 * t + 0.4)
        + 0.12 * np.sin(2.0 * math.pi * 165.0 * t + 0.9)
        + 0.06 * np.sin(2.0 * math.pi * 27.5 * t)
    )
    # Subtle soft tire/engine texture with circular edge blend
    tex = soft_one_pole_lpf(rng.standard_normal(n), 280.0, sr=sr_tr) * 0.15
    bl = int(sr_tr * 0.25)
    al = np.linspace(0.0, 1.0, bl)
    th = tex[:bl] * al + tex[-bl:] * (1.0 - al)
    tex[-bl:] = tex[-bl:] * al + tex[:bl] * (1.0 - al)
    tex[:bl] = th
    save_wav(OUT_ROOT / "Traffic/sfx_car_engine_hum.wav", normalize_peak(hum + tex, -9.0), sr=sr_tr)

    # 3. Train Horn (0.85s rich warm Fmaj chord whistle)
    dur = 0.85
    n = int(sr_tr * dur)
    t = np.arange(n) / sr_tr
    env = np.minimum(1.0, t / 0.07) * np.minimum(1.0, np.maximum(0.0, (dur - t) / 0.18))
    horn = (
        0.44 * np.sin(2.0 * math.pi * 349.23 * t)
        + 0.34 * np.sin(2.0 * math.pi * 440.00 * t)
        + 0.22 * np.sin(2.0 * math.pi * 523.25 * t)
        + 0.08 * np.sin(2.0 * math.pi * 698.46 * t)
    )
    steam = soft_one_pole_lpf(rng.standard_normal(n), 1400.0, sr=sr_tr) * 0.05
    save_wav(OUT_ROOT / "Traffic/sfx_train_horn.wav", normalize_peak((horn + steam) * env, -4.5), sr=sr_tr)

    # 4. Train Track Rumble (2.0s seamless rhythmic cadence + soft rail clack)
    dur = 2.0
    n = int(sr_tr * dur)
    t = np.arange(n) / sr_tr
    # Integer frequencies for exact seamless loop
    cadence = 0.72 + 0.28 * np.sin(2.0 * math.pi * 4.0 * t)
    rumble = (0.60 * np.sin(2.0 * math.pi * 48.0 * t) + 0.35 * np.sin(2.0 * math.pi * 96.0 * t)) * cadence
    # Subtle cyclic rail joint clacks at t = 0.25, 0.45, 1.25, 1.45
    clack_buf = np.zeros(n)
    for ct in (0.25, 0.42, 1.25, 1.42):
        i0 = int(ct * sr_tr)
        cn = int(0.04 * sr_tr)
        tt = np.arange(cn) / sr_tr
        cl = soft_one_pole_lpf(rng.standard_normal(cn), 950.0, sr=sr_tr) * np.exp(-65.0 * tt) * 0.55
        clack_buf[i0:i0 + cn] += cl
    save_wav(OUT_ROOT / "Traffic/sfx_train_rumble.wav", normalize_peak(rumble + clack_buf, -7.5), sr=sr_tr)

    # 5. Rail Crossing Bell Ding (0.42s metallic crossing bell D6 + shimmer)
    dur = 0.42
    n = int(sr_tr * dur)
    t = np.arange(n) / sr_tr
    atk = np.minimum(1.0, t / 0.002)
    bell = (
        0.62 * np.sin(2.0 * math.pi * 1174.66 * t) * np.exp(-8.0 * t)
        + 0.30 * np.sin(2.0 * math.pi * 2349.32 * t) * np.exp(-12.0 * t)
        + 0.14 * np.sin(2.0 * math.pi * 3523.98 * t) * np.exp(-18.0 * t)
    )
    save_wav(OUT_ROOT / "Traffic/sfx_rail_crossing_bell.wav", normalize_peak(fade_edges(bell * atk, 0.5, 15.0), -5.5), sr=sr_tr)


# ---------------------------------------------------------------------------
# Verification Report
# ---------------------------------------------------------------------------

def verify_all_outputs() -> int:
    ffprobe = shutil.which("ffprobe") or "ffprobe"
    files = sorted(list(OUT_ROOT.rglob("*.wav")) + list(OUT_ROOT.rglob("*.ogg")))
    print(f"\n=== VERIFYING {len(files)} GENERATED CLIPS ===")
    failures = 0
    for f in files:
        if f.suffix == ".wav":
            with wave.open(str(f), "rb") as wf:
                ch = wf.getnchannels()
                sr = wf.getframerate()
                raw = np.frombuffer(wf.readframes(wf.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
                if ch > 1:
                    raw = raw.reshape(-1, ch)
        else:
            # decode via ffmpeg to inspect exact sample stats & loop seam
            cmd = [FFMPEG, "-v", "error", "-i", str(f), "-f", "s16le", "-acodec", "pcm_s16le", "pipe:1"]
            res = subprocess.run(cmd, capture_output=True, check=True)
            p_res = subprocess.run([ffprobe, "-v", "error", "-show_entries", "stream=channels,sample_rate", "-of", "default=noprint_wrappers=1:nokey=1", str(f)], capture_output=True, text=True, check=True)
            sr_str, ch_str = p_res.stdout.strip().splitlines()[:2]
            sr, ch = int(sr_str), int(ch_str)
            raw = np.frombuffer(res.stdout, dtype="<i2").astype(np.float64) / 32768.0
            if ch > 1:
                raw = raw.reshape(-1, ch)

        dur = len(raw) / sr
        peak = float(np.max(np.abs(raw)))
        rms = float(np.sqrt(np.mean(raw ** 2)))
        peak_db = 20.0 * math.log10(max(peak, 1e-9))
        rms_db = 20.0 * math.log10(max(rms, 1e-9))
        dc = float(np.mean(raw))
        clipped = int(np.sum(np.abs(raw) >= 0.999))
        loop_diff = float(np.max(np.abs(raw[0] - raw[-1])))

        is_loop = f.parent.name in ("Music", "Ambience") or "hum" in f.name or "rumble" in f.name
        bad_loop = is_loop and loop_diff > 0.04
        if clipped > 0 or abs(dc) > 0.01 or bad_loop:
            failures += 1
            flag = " [FAIL]"
        else:
            flag = ""
        print(f"  {f.relative_to(OUT_ROOT)!s:<40} | {sr}Hz {ch}ch {dur:5.2f}s | peak={peak_db:6.2f} rms={rms_db:6.2f} | dc={dc:+.5f} loopDiff={loop_diff:.4f} clip={clipped}{flag}")
    return failures


def main() -> int:
    rng = np.random.default_rng(SEED)
    generate_music(rng)
    generate_ambience(rng)
    generate_ui_sfx(rng)
    generate_gameplay_sfx(rng)
    generate_traffic_sfx(rng)
    failures = verify_all_outputs()
    print(f"\nDone. Verification failures: {failures}")
    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
