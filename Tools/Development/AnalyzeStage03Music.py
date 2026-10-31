"""Reproduce the Stage3 beatmap from the complete, unchanged vocal PCM (numpy).

This is numerical evidence; audio input was unavailable and human music-sync
verification remains pending. No source WAV or Stage1/2 asset is rewritten.
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
LABELS = ["머리 제거", "앞면 필렛 1 분리", "앞면 필렛 2 분리", "뒷면 필렛 1 분리", "뒷면 필렛 2 분리",
          "앞면 필렛 1 썰기", "앞면 필렛 2 썰기", "뒷면 필렛 1 썰기", "뒷면 필렛 2 썰기"]
REGIONS = [(8,25),(29,46),(50,67),(72,89),(94,111),(118,138),(145,165),(172,192),(201,224)]
def r(value): return round(float(value),3)

def analyze(source):
    before=hashlib.sha256(source.read_bytes()).hexdigest()
    if before!=SHA: raise ValueError("Approved vocal source changed.")
    with wave.open(str(source)) as stream:
        rate,channels,frames,width=stream.getframerate(),stream.getnchannels(),stream.getnframes(),stream.getsampwidth()
        if (rate,channels,width)!=(48000,2,2): raise ValueError("Expected approved stereo PCM16/48kHz source.")
        pcm=np.frombuffer(stream.readframes(frames),dtype='<i2').reshape(-1,channels)
        mono=pcm.astype(np.float32).mean(axis=1)/32768
    duration=frames/rate
    # Centered FIR before decimation avoids aliasing; window timestamps below
    # include the STFT center instead of the former uncorrected frame start.
    taps=np.arange(63)-31; kernel=np.sinc(taps*.22)*np.hanning(63);kernel/=kernel.sum()
    audio=np.convolve(mono,kernel,mode='same')[::4]
    fs,nfft,hop=rate/4,1024,128
    windows=np.lib.stride_tricks.sliding_window_view(audio,nfft)[::hop]
    spectrum=np.abs(np.fft.rfft(windows*np.hanning(nfft),axis=1))
    frequencies=np.fft.rfftfreq(nfft,1/fs)
    flux=np.maximum(0,np.diff(np.log1p(spectrum),axis=0))
    times=(np.arange(len(flux))+1)*hop/fs+nfft/(2*fs)
    bands={}
    for name,lo,hi in [('kick',35,180),('body',180,1800),('hats',1800,5500)]:
        env=flux[:,(frequencies>=lo)&(frequencies<hi)].mean(axis=1)
        env=np.maximum(0,env-np.median(env));bands[name]=np.minimum(3,env/max(1e-9,np.quantile(env,.95)))
    onset=.50*bands['kick']+.20*bands['body']+.30*bands['hats']
    dt=hop/fs; size=1<<int(math.ceil(math.log2(2*len(onset))))
    ac=np.fft.irfft(np.abs(np.fft.rfft(onset,size))**2)[:len(onset)];ac/=max(1e-9,ac[0])
    bpms=np.arange(72,145,.02);lags=60/bpms/dt
    scores=np.interp(lags,np.arange(len(ac)),ac)+.5*np.interp(2*lags,np.arange(len(ac)),ac)
    rough=float(bpms[np.argmax(scores)]);best=(-1,rough,0)
    for bpm in np.arange(max(72,rough-2),min(145,rough+2),.02):
        period=60/bpm; phases=np.arange(0,period,.01)
        grid=phases[:,None]+np.arange(8,int((duration-4)/period))[None,:]*period
        strength=np.maximum.reduce([np.interp(grid+d,times,onset) for d in (-.02,0,.02)])
        score=strength.mean(axis=1);choice=int(np.argmax(score))
        if float(score[choice])>best[0]:best=(float(score[choice]),float(bpm),float(phases[choice]))
    _,bpm,phase=best;period=60/bpm
    peaks=np.flatnonzero((onset>=np.roll(onset,1))&(onset>np.roll(onset,-1))&(onset>.15))
    peak_times=times[peaks];beats=[];strengths=[];shifts=[];prediction=phase;local_period=period
    while prediction<duration-.1:
        nearby=np.flatnonzero(np.abs(peak_times-prediction)<=.085)
        selected=prediction;strength=float(np.interp(prediction,times,onset))
        if len(nearby):
            value=onset[peaks[nearby]]-.5*((peak_times[nearby]-prediction)/.085)**2
            winner=nearby[int(np.argmax(value))];selected=float(peak_times[winner]);strength=float(onset[peaks[winner]])
        if beats:local_period=float(np.clip(.9*local_period+.1*(selected-beats[-1]),period*.96,period*1.04))
        if selected>=0 and (not beats or selected>beats[-1]+.35):
            beats.append(selected);strengths.append(strength);shifts.append(selected-prediction)
        prediction=selected+local_period
    beats=np.asarray(beats);strengths=np.asarray(strengths)
    accent=int(np.argmax([np.mean(strengths[np.arange(len(beats))%4==i]) for i in range(4)]))
    phases_out=[];evidence=[]
    for stage,(label,(start,end)) in enumerate(zip(LABELS,REGIONS)):
        count=6 if stage<5 else 8;selected=[];edges=np.linspace(start,end,count+1)
        for zone in range(count):
            eligible=np.flatnonzero((beats>=edges[zone])&(beats<edges[zone+1]))
            if not len(eligible):raise ValueError('Missing pulse in phase/zone %d/%d'%(stage,zone))
            score=strengths[eligible]+.12*((eligible%4)==accent)-.08*np.abs(beats[eligible]-(edges[zone]+edges[zone+1])/2)
            options=[int(i) for i in eligible[np.argsort(score)[::-1]] if not selected or beats[i]-beats[selected[-1]]>=.65]
            if not options:raise ValueError('No complete-stroke spacing in phase %d'%stage)
            selected.append(options[0])
        notes=[r(beats[i]*1000) for i in selected]
        phases_out.append(dict(label=label,requiredHits=4 if stage<5 else 6,noteTimesMs=notes))
        evidence.append(dict(label=label,regionSeconds=[start,end],selectedBeatIndices=selected,
            measuredAttackStrength=[r(strengths[i]) for i in selected],intervalsSeconds=[r(v) for v in np.diff(beats[selected])]))
    notes=[n for p in phases_out for n in p['noteTimesMs']]
    if len(notes)!=62 or any(b<=a for a,b in zip(notes,notes[1:])):raise ValueError('Invalid 62-event ordering')
    if notes[-1]>(duration-3)*1000:raise ValueError('Insufficient final contact/result tail')
    if hashlib.sha256(source.read_bytes()).hexdigest()!=before:raise ValueError('Source changed during analysis')
    chart=dict(analysisVersion=2,bpm=round(bpm,2),firstGridBeatSeconds=round(phase,3),sourceSha256=before,
        sourceDurationSeconds=duration,gameplayStartSeconds=round(notes[0]/1000,6),gameplayEndSeconds=round(notes[-1]/1000,6),
        method='Anti-aliased full PCM; centered1024FFT/128hop at12kHz; kick/body/hats log-flux; tempo/phase fit; bounded local pulse tracking; measured accent/coverage selection',
        ownerApprovedPrototype=True,ownerMusicalSyncPending=True,beatTimesMs=[r(t*1000) for t in beats],phases=phases_out)
    sections=[]
    for start in range(0,int(math.ceil(duration)),20):
        end=min(start+20,duration);mask=(times>=start)&(times<end);part=audio[int(start*fs):int(end*fs)]
        sections.append(dict(startSeconds=start,endSeconds=end,rms=r(np.sqrt(np.mean(part**2))),meanAttack=r(np.mean(onset[mask])),highAttack=r(np.quantile(onset[mask],.9))))
    report=dict(source=SOURCE.as_posix(),sourceSha256=before,sourcePreserved=True,sampleRate=rate,channels=channels,
        sampleWidthBytes=width,frames=frames,durationSeconds=duration,numpyVersion=np.__version__,firTaps=63,
        analysisSampleRate=fs,fftSize=nfft,hopSamples=hop,timestampReference='STFT window center',
        auditoryInspection='Unavailable: audio tool did not support audio input. No direct-listening claim.',humanMusicSyncVerification='Pending Owner playback and timing/readability/feel approval.',
        bandHz=dict(kick=[35,180],body=[180,1800],hats=[1800,5500]),weights=dict(kick=.5,body=.2,hats=.3),
        roughAutocorrelationBpm=round(rough,2),fittedBpm=chart['bpm'],fittedPhaseSeconds=chart['firstGridBeatSeconds'],
        pulseCount=len(beats),maximumPulseSnapMs=r(max(abs(v) for v in shifts)*1000),fullSourceSections=sections,phaseEvidence=evidence,
        changesFromPrior='Identical fixed slots and +/-120ms all-band snapping replaced with measured accents/coverage. Late160-224s sections are used; original230.92s source and6/8 opportunities,4/6 required hits unchanged.')
    return chart,report

def main():
    root=Path(__file__).resolve().parents[2];p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path,default=root/SOURCE)
    p.add_argument('--chart-output',type=Path,default=root/'Docs/Development/STAGE03_RHYTHM_ANALYSIS.json')
    p.add_argument('--analysis-output',type=Path,default=root/'Docs/Development/STAGE03_BEAT_ANALYSIS.json')
    args=p.parse_args()
    if args.source.resolve() in (args.chart_output.resolve(),args.analysis_output.resolve()):raise ValueError('Source is never an output')
    chart,report=analyze(args.source)
    for target,value in [(args.chart_output,chart),(args.analysis_output,report)]:
        target.parent.mkdir(parents=True,exist_ok=True);target.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(bpm=chart['bpm'],phaseSeconds=chart['firstGridBeatSeconds'],firstNote=chart['gameplayStartSeconds'],lastNote=chart['gameplayEndSeconds'],opportunities=62,requiredHits=44,beats=len(chart['beatTimesMs']),sourcePreserved=True,humanMusicSync='Pending'),indent=2))
if __name__=='__main__':main()
