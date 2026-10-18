#!/usr/bin/env python3
"""Synthesises AVASentinel's sound effects and tunes into ../src/AVASentinel/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the tunes
are new compositions: a slow, eerie theme for the title and a low drone for the landscapes. The warning is spoken by
the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say


RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASentinel", "Sounds")
RNG = random.Random(1986)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def computer(words):
    voice = say(words, rate=RATE, pitch=104, speed=0.95, size=1.05)
    hum = say(words, rate=RATE, pitch=156, speed=0.95, size=1.05, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    write("Absorb.wav", tone(0.35, 900, 120, "tri", attack=0.005, vibrato=0.05, vib_rate=30, release=0.6, gain=0.6), 0.24)
    write("Create.wav", tone(0.3, 140, 900, "tri", attack=0.005, vibrato=0.05, vib_rate=30, release=0.6, gain=0.6), 0.22)
    write("Transfer.wav", cat(tone(0.25, 300, 1600, "saw", attack=0.01, release=0.4, gain=0.4), tone(0.25, 1600, 600, "sine", release=0.6, gain=0.5)), 0.26)
    write("Hyperspace.wav", mix(tone(1.2, 2000, 80, "noise", attack=0.05, release=0.6, gain=0.6), tone(1.2, 60, 900, "saw", vibrato=0.1, vib_rate=8, release=0.6, gain=0.4)), 0.34)
    write("Deny.wav", tone(0.12, 160, 140, "square", attack=0.002, release=0.6, gain=0.5), 0.16)
    write("SentinelTurn.wav", cat(tone(0.08, 110, 90, "square", attack=0.002, release=0.5, gain=0.5), tone(0.12, 220, 180, "tri", release=0.6, gain=0.4)), 0.2)
    write("Watched.wav", cat(*[tone(0.15, f, kind="sine", release=0.4, gain=0.6) for f in (880, 660, 880, 660)]), 0.26)
    write("Drain.wav", tone(0.4, 600, 90, "saw", attack=0.01, vibrato=0.1, vib_rate=12, release=0.5, gain=0.5), 0.26)
    write("Die.wav", cat(tone(1.0, 900, 40, "saw", attack=0.01, vibrato=0.15, vib_rate=7, release=0.4, gain=0.6), tone(0.8, 2000, 60, "noise", release=0.7, gain=0.4)), 0.4)
    write("SentinelGone.wav", cat(tone(0.8, 1200, 60, "saw", vibrato=0.08, vib_rate=20, release=0.5, gain=0.5), *[tone(0.1, hz(n), kind="pulse", release=0.3) for n in ["A4", "C5", "E5", "A5"]]), 0.34)
    write("Won.wav", cat(*[tone(0.1, hz(n), kind="pulse", release=0.3) for n in ["A4", "C5", "E5", "A5", "C6", "E6"]], tone(0.8, hz("A6"), kind="pulse", vibrato=0.02, release=0.6)), 0.36)
    write("Tick.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    # "It sees you!"
    words = "IH1 T , S IY1 Z , Y UW1 !"
    write("SayWatched.wav", mix(say(words, rate=RATE, pitch=80, speed=0.9, size=1.15), [s * 0.4 for s in say(words, rate=RATE, pitch=120, speed=0.9, size=1.15, seed=11)]), 0.5)

    # The landscapes (an original composition): a low, slow drone that moves now and then.
    drone = [("A2", 4), ("A2", 4), ("F2", 4), ("G2", 4), ("A2", 4), ("A2", 4), ("D2", 4), ("E2", 4)]
    over = [("-", 2), ("E4", 2), ("-", 2), ("C4", 2), ("-", 2), ("D4", 2), ("-", 2), ("B3", 2), ("-", 2), ("E4", 2), ("-", 2), ("A4", 2), ("-", 2), ("F4", 2), ("-", 2), ("G#4", 2)]
    write("Music.wav", tune(70, over, drone, kind="tri"), 0.22)
    # The title (an original composition): slow and eerie.
    title = [("A4", 1), ("E5", 1), ("D5", 0.5), ("C5", 0.5), ("B4", 1), ("E4", 2), ("F4", 1), ("C5", 1), ("B4", 0.5), ("A4", 0.5), ("G#4", 1), ("A4", 3)]
    tbass = [("A2", 2), ("A2", 2), ("E2", 2), ("E2", 2), ("F2", 2), ("D2", 2), ("E2", 2), ("A2", 2)]
    write("TitleTune.wav", cat(tune(80, title * 2, tbass * 2, kind="tri"), silence(1.0)), 0.34)
    write("Victory.wav", tune(150, [("A4", 0.5), ("C#5", 0.5), ("E5", 0.5), ("A5", 1.5), ("G#5", 0.5), ("A5", 0.5), ("B5", 0.5), ("C#6", 3)],
                              [("A2", 1), ("E2", 1), ("F#2", 1), ("D2", 1), ("E2", 1), ("A2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(60, [("A4", 1), ("F4", 1), ("D4", 1), ("A3", 1), ("D4", 3)], [("D3", 2), ("A#2", 2), ("D2", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
