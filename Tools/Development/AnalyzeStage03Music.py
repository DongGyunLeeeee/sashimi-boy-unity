"""Build Stage3's continuous hard chart from the unchanged, complete vocal PCM.

This is numerical analysis, not a claim of listening. Quarter-note pulses and
measured eighth-note attacks form repeatable musical phrases. No source WAV,
Stage1/2 timing asset, or shared judgement window is rewritten.
"""
import argparse
import hashlib
import json
import math
import wave
from pathlib import Path

import numpy as np

SOURCE = Path("Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav")
SHA = "3572605dec84991aabf1e4911eee443287da0f7e5c403b0a449aee403cf31062"
LABELS = ["머리 제거", "앞면 껍질 벗기기", "앞면 필렛 분리", "생선 뒤집기",
          "뒷면 껍질 벗기기", "뒷면 필렛 분리", "앞면 필렛 썰기", "뒷면 필렛 썰기"]
# Disjoint song regions leave real time for the last contact, independent part
# separation, and the flip. These are musical transitions, never clock pauses.
REGIONS = [(8, 23), (25, 47), (49, 70), (72, 74.5),
           (76, 100), (102, 127), (129, 174), (176, 225)]
MIN_GAP_SECONDS = .280
PHASE_GAP_SECONDS = 1.250
CONTACT_TAIL_SECONDS = 1.000
SUCCESS_PERCENT = 70


def r(value):
    return round(float(value), 3)


def required_hits(count):
    return (count * SUCCESS_PERCENT + 99) // 100


def local_peaks(envelope, minimum):
    return np.flatnonzero((envelope >= np.roll(envelope, 1)) &
                          (envelope > np.roll(envelope, -1)) & (envelope > minimum))


def phrase_candidates(beats, strengths, accent, times, onset, bands, duration, period):
    """Choose rhythmic phrases; snap optional offbeats to actual high-band attacks.

    Repeated sparse/steady/dense patterns give the player a learnable pulse;
    density follows measured per-bar attacks. Merely subdividing an estimated
    BPM would add notes to words/noise or silence indiscriminately.
    """
    first_bar = (0 - accent) // 4
    last_bar = (len(beats) - 1 - accent) // 4
    bar_energy = {}
    for bar in range(first_bar, last_bar + 1):
        indices = np.flatnonzero((np.arange(len(beats)) - accent) // 4 == bar)
        start = beats[indices[0]]
        stop = beats[indices[-1]] + period
        mask = (times >= start) & (times < min(stop, duration))
        bar_energy[bar] = float(np.mean(onset[mask])) if np.any(mask) else 0
    low, high = np.quantile(list(bar_energy.values()), [.25, .72])
    hat_peaks = local_peaks(bands['hats'], .22)
    hat_times = times[hat_peaks]
    candidates = []
    for index, beat in enumerate(beats):
        bar, slot = divmod(index - accent, 4)
        energy = bar_energy[bar]
        tier = 'sparse' if energy < low else 'dense' if energy >= high else 'steady'
        if tier == 'sparse':
            quarters, eighth_slots = {0, 2}, set()
        elif tier == 'steady':
            quarters = {0, 1, 2} if bar % 2 == 0 else {0, 2, 3}
            eighth_slots = {1 if bar % 2 == 0 else 3}
        else:
            quarters, eighth_slots = {0, 1, 2, 3}, {1, 3}
        if slot in quarters:
            candidates.append(dict(time=beat, expected=beat, beatIndex=index,
                                   subdivision='quarter', bar=bar, slot=slot,
                                   pattern=tier, energy=energy,
                                   strength=float(strengths[index])))
        if index + 1 >= len(beats) or slot not in eighth_slots:
            continue
        prediction = (beat + beats[index + 1]) / 2
        nearby = np.flatnonzero(np.abs(hat_times - prediction) <= .045)
        if not len(nearby):
            continue
        peak_indices = hat_peaks[nearby]
        values = bands['hats'][peak_indices] + .35 * onset[peak_indices] - .4 * ((times[peak_indices] - prediction) / .045) ** 2
        chosen = int(peak_indices[int(np.argmax(values))])
        candidate = float(times[chosen])
        if candidate - beat < MIN_GAP_SECONDS or beats[index + 1] - candidate < MIN_GAP_SECONDS:
            continue
        candidates.append(dict(time=candidate, expected=prediction, beatIndex=index,
                               subdivision='eighth', bar=bar, slot=slot + .5,
                               pattern=tier, energy=energy,
                               strength=float(onset[chosen])))
    return sorted(candidates, key=lambda item: item['time']), dict(
        lowAttackBoundary=r(low), highAttackBoundary=r(high),
        barEnergy=[dict(bar=bar, meanAttack=r(value)) for bar, value in bar_energy.items()])


def analyze(source):
    before = hashlib.sha256(source.read_bytes()).hexdigest()
    if before != SHA:
        raise ValueError("Approved vocal source changed.")
    with wave.open(str(source)) as stream:
        rate, channels, frames, width = stream.getframerate(), stream.getnchannels(), stream.getnframes(), stream.getsampwidth()
        if (rate, channels, width) != (48000, 2, 2):
            raise ValueError("Expected approved stereo PCM16/48kHz source.")
        pcm = np.frombuffer(stream.readframes(frames), dtype='<i2').reshape(-1, channels)
        mono = pcm.astype(np.float32).mean(axis=1) / 32768
    duration = frames / rate
    # Centered FIR prevents decimation aliasing; STFT timestamps include the
    # window center, rather than assigning an attack to its frame's start.
    taps = np.arange(63) - 31
    kernel = np.sinc(taps * .22) * np.hanning(63)
    kernel /= kernel.sum()
    audio = np.convolve(mono, kernel, mode='same')[::4]
    fs, nfft, hop = rate / 4, 1024, 128
    windows = np.lib.stride_tricks.sliding_window_view(audio, nfft)[::hop]
    spectrum = np.abs(np.fft.rfft(windows * np.hanning(nfft), axis=1))
    frequencies = np.fft.rfftfreq(nfft, 1 / fs)
    flux = np.maximum(0, np.diff(np.log1p(spectrum), axis=0))
    times = (np.arange(len(flux)) + 1) * hop / fs + nfft / (2 * fs)
    bands = {}
    for name, lo, hi in [('kick', 35, 180), ('body', 180, 1800), ('hats', 1800, 5500)]:
        envelope = flux[:, (frequencies >= lo) & (frequencies < hi)].mean(axis=1)
        envelope = np.maximum(0, envelope - np.median(envelope))
        bands[name] = np.minimum(3, envelope / max(1e-9, np.quantile(envelope, .95)))
    onset = .50 * bands['kick'] + .20 * bands['body'] + .30 * bands['hats']
    dt = hop / fs
    size = 1 << int(math.ceil(math.log2(2 * len(onset))))
    ac = np.fft.irfft(np.abs(np.fft.rfft(onset, size)) ** 2)[:len(onset)]
    ac /= max(1e-9, ac[0])
    bpms = np.arange(72, 145, .02)
    lags = 60 / bpms / dt
    scores = np.interp(lags, np.arange(len(ac)), ac) + .5 * np.interp(2 * lags, np.arange(len(ac)), ac)
    rough = float(bpms[np.argmax(scores)])
    best = (-1, rough, 0)
    for bpm in np.arange(max(72, rough - 2), min(145, rough + 2), .02):
        period = 60 / bpm
        phases = np.arange(0, period, .01)
        grid = phases[:, None] + np.arange(8, int((duration - 4) / period))[None, :] * period
        strength = np.maximum.reduce([np.interp(grid + offset, times, onset) for offset in (-.02, 0, .02)])
        score = strength.mean(axis=1)
        choice = int(np.argmax(score))
        if float(score[choice]) > best[0]:
            best = (float(score[choice]), float(bpm), float(phases[choice]))
    _, bpm, phase = best
    period = 60 / bpm
    peaks = local_peaks(onset, .15)
    peak_times = times[peaks]
    beats, strengths, shifts = [], [], []
    prediction, local_period = phase, period
    while prediction < duration - .1:
        nearby = np.flatnonzero(np.abs(peak_times - prediction) <= .085)
        selected = prediction
        strength = float(np.interp(prediction, times, onset))
        if len(nearby):
            values = onset[peaks[nearby]] - .5 * ((peak_times[nearby] - prediction) / .085) ** 2
            winner = nearby[int(np.argmax(values))]
            selected, strength = float(peak_times[winner]), float(onset[peaks[winner]])
        if beats:
            local_period = float(np.clip(.9 * local_period + .1 * (selected - beats[-1]), period * .96, period * 1.04))
        if selected >= 0 and (not beats or selected > beats[-1] + .35):
            beats.append(selected)
            strengths.append(strength)
            shifts.append(selected - prediction)
        prediction = selected + local_period
    beats, strengths = np.asarray(beats), np.asarray(strengths)
    accent = int(np.argmax([np.mean(strengths[np.arange(len(beats)) % 4 == i]) for i in range(4)]))
    candidates, phrase_report = phrase_candidates(beats, strengths, accent, times, onset, bands, duration, period)
    phases_out, phase_evidence, all_evidence = [], [], []
    for stage, (label, (start, end)) in enumerate(zip(LABELS, REGIONS)):
        if stage == 3:
            eligible = np.flatnonzero((beats >= start) & (beats < end))
            if not len(eligible):
                raise ValueError('Missing turnover pulse.')
            winner = int(eligible[np.argmax(strengths[eligible] - .12 * np.abs(beats[eligible] - (start + end) / 2))])
            selected = [dict(time=float(beats[winner]), expected=float(beats[winner]), beatIndex=winner,
                             subdivision='quarter', bar=(winner - accent) // 4,
                             slot=(winner - accent) % 4, pattern='turnover',
                             energy=float(np.interp(beats[winner], times, onset)), strength=float(strengths[winner]))]
        else:
            selected = [item for item in candidates if start <= item['time'] < end]
        if not selected:
            raise ValueError('Empty Stage3 phase %d.' % stage)
        notes = [r(item['time'] * 1000) for item in selected]
        needed = required_hits(len(notes))
        if stage >= 6 and needed < 12:
            raise ValueError('Each slicing phase needs at least twelve actual successful inputs.')
        phases_out.append(dict(label=label, requiredHits=needed,
                               endTimeMs=r((selected[-1]['time'] + CONTACT_TAIL_SECONDS) * 1000),
                               noteTimesMs=notes))
        evidences = []
        for ordinal, item in enumerate(selected):
            time = item['time']
            evidence = dict(phase=stage, ordinal=ordinal, timeMs=r(time * 1000),
                            subdivision=item['subdivision'], beatIndex=int(item['beatIndex']),
                            bar=int(item['bar']), beatInBar=item['slot'], pattern=item['pattern'],
                            expectedSubdivisionTimeMs=r(item['expected'] * 1000),
                            snapMs=r((time - item['expected']) * 1000),
                            pulseTrackingSnapMs=r(shifts[item['beatIndex']] * 1000) if item['subdivision'] == 'quarter' else None,
                            measuredAttack=r(item['strength']),
                            bandAttack={name: r(np.interp(time, times, value)) for name, value in bands.items()},
                            barMeanAttack=r(item['energy']))
            evidences.append(evidence)
            all_evidence.append(evidence)
        phase_evidence.append(dict(phase=stage, label=label, regionSeconds=[start, end],
                                   opportunities=len(notes), requiredHits=needed,
                                   passRule='ceil(0.70 * opportunities); all notes remain judgeable',
                                   firstNoteMs=notes[0], lastNoteMs=notes[-1], endTimeMs=phases_out[-1]['endTimeMs'],
                                   quarterNotes=sum(item['subdivision'] == 'quarter' for item in selected),
                                   eighthNotes=sum(item['subdivision'] == 'eighth' for item in selected),
                                   notes=evidences))
    notes = [note for phase_data in phases_out for note in phase_data['noteTimesMs']]
    intervals = np.diff(notes)
    if np.min(intervals) < MIN_GAP_SECONDS * 1000 - .001:
        raise ValueError('Chart violates the unchanged judgement window/short-stroke spacing.')
    phase_gaps = [phases_out[i]['noteTimesMs'][0] - phases_out[i - 1]['noteTimesMs'][-1] for i in range(1, len(phases_out))]
    if min(phase_gaps) < PHASE_GAP_SECONDS * 1000:
        raise ValueError('A musical phase boundary cannot finish the preceding physical action.')
    if any(phases_out[i]['noteTimesMs'][0] - phases_out[i - 1]['endTimeMs'] < 250 for i in range(1, len(phases_out))):
        raise ValueError('The phase deadline overlaps the next input window.')
    if notes[-1] > (duration - 3) * 1000:
        raise ValueError('Insufficient final contact/result tail.')
    if hashlib.sha256(source.read_bytes()).hexdigest() != before:
        raise ValueError('Source changed during analysis.')
    elapsed = (notes[-1] - notes[0]) / 1000
    quarters = sum(item['subdivision'] == 'quarter' for item in all_evidence)
    eighths = len(all_evidence) - quarters
    chart = dict(analysisVersion=3, bpm=round(bpm, 2), firstGridBeatSeconds=round(phase, 3), sourceSha256=before,
                 sourceDurationSeconds=duration, gameplayStartSeconds=round(notes[0] / 1000, 6),
                 gameplayEndSeconds=round(notes[-1] / 1000, 6),
                 method='Anti-aliased full PCM; centered1024FFT/128hop at12kHz; kick/body/hats log-flux; tempo/phase fit; bounded local pulse tracking; measured per-bar phrase density and high-band eighth attacks',
                 ownerDecisionUrl='https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/87#issuecomment-6555351413',
                 ownerApprovedPrototype=True, ownerMusicalSyncPending=True,
                 minimumNoteGapMs=280, minimumPhaseGapMs=1250, successPercent=SUCCESS_PERCENT,
                 continuousPlayback=True, filletCount=2, slicesPerFillet=12,
                 beatTimesMs=[r(time * 1000) for time in beats], phases=phases_out)
    sections = []
    for start in range(0, int(math.ceil(duration)), 20):
        end = min(start + 20, duration)
        mask = (times >= start) & (times < end)
        part = audio[int(start * fs):int(end * fs)]
        in_section = [item for item in all_evidence if start * 1000 <= item['timeMs'] < end * 1000]
        sections.append(dict(startSeconds=start, endSeconds=end, rms=r(np.sqrt(np.mean(part ** 2))),
                             meanAttack=r(np.mean(onset[mask])), highAttack=r(np.quantile(onset[mask], .9)),
                             chartNotes=len(in_section), eighthNotes=sum(item['subdivision'] == 'eighth' for item in in_section)))
    report = dict(source=SOURCE.as_posix(), sourceSha256=before, sourcePreserved=True, sampleRate=rate,
                  channels=channels, sampleWidthBytes=width, frames=frames, durationSeconds=duration,
                  numpyVersion=np.__version__, firTaps=63, analysisSampleRate=fs, fftSize=nfft, hopSamples=hop,
                  timestampReference='STFT window center',
                  auditoryInspection='Unavailable: audio input is unsupported. No direct-listening claim.',
                  humanMusicSyncVerification='Pending Owner playback and timing/readability/feel approval.',
                  bandHz=dict(kick=[35, 180], body=[180, 1800], hats=[1800, 5500]),
                  weights=dict(kick=.5, body=.2, hats=.3), roughAutocorrelationBpm=round(rough, 2),
                  fittedBpm=chart['bpm'], fittedPhaseSeconds=chart['firstGridBeatSeconds'], pulseCount=len(beats),
                  maximumPulseSnapMs=r(max(abs(value) for value in shifts) * 1000),
                  globalQuarterAccent=accent, phraseSelection=phrase_report,
                  chartMetrics=dict(totalNotes=len(notes), totalRequiredHits=sum(p['requiredHits'] for p in phases_out),
                                    quarterNotes=quarters, eighthNotes=eighths, averageNotesPerSecond=r(len(notes) / elapsed),
                                    minimumGapMs=r(np.min(intervals)), medianGapMs=r(np.median(intervals)),
                                    maximumGapMs=r(np.max(intervals)), minimumPhaseGapMs=r(min(phase_gaps)),
                                    phaseBoundaryGapsMs=[r(value) for value in phase_gaps],
                                    maximumEighthSnapMs=r(max([abs(item['snapMs']) for item in all_evidence if item['subdivision'] == 'eighth'] or [0])),
                                    hardChartNoteCountTarget=[280, 350], targetMet=280 <= len(notes) <= 350,
                                    oldChartNotes=62, oldRequiredHits=44, oldAverageNotesPerSecond=r(62 / (222.965333 - 10.794667)),
                                    countMultiplier=r(len(notes) / 62)),
                  animationContract=dict(maximumStrokeSeconds=.240, unchangedCasualWindowMs=125,
                                         phaseContactTailMs=1000, minimumBoundaryGapMs=1250,
                                         turnoverIsJudgedSpaceNote=True, quotaNeverDisablesLaterNotes=True,
                                         failureRestartsWholeStage=True),
                  fullSourceSections=sections, phaseEvidence=phase_evidence,
                  changesFromPrior='62 sparse fixed-count events replaced by measured quarter/eighth phrases; every phase judges all notes through its deadline; one scored turnover note; eight phases; two actual fillets, twelve slices each; source and shared Stage1/2 judgement unchanged.')
    return chart, report


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=root / SOURCE)
    parser.add_argument('--chart-output', type=Path, default=root / 'Docs/Development/STAGE03_RHYTHM_ANALYSIS.json')
    parser.add_argument('--analysis-output', type=Path, default=root / 'Docs/Development/STAGE03_BEAT_ANALYSIS.json')
    args = parser.parse_args()
    if args.source.resolve() in (args.chart_output.resolve(), args.analysis_output.resolve()):
        raise ValueError('Source is never an output.')
    chart, report = analyze(args.source)
    for target, value in [(args.chart_output, chart), (args.analysis_output, report)]:
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(bpm=chart['bpm'], phaseSeconds=chart['firstGridBeatSeconds'],
                          firstNote=chart['gameplayStartSeconds'], lastNote=chart['gameplayEndSeconds'],
                          metrics=report['chartMetrics'], phaseCounts=[len(phase['noteTimesMs']) for phase in chart['phases']],
                          sourcePreserved=True, humanMusicSync='Pending'), indent=2))


if __name__ == '__main__':
    main()
