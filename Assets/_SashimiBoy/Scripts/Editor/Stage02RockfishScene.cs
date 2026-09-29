using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using SashimiBoy.Semantics;
using Object=UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage02RockfishAuthoring
    {
        public const string Version="issue42-stage02-rockfish-20260910-v1";
        private static readonly int[] PhaseBars={8,8,8,6,6,8};
        [MenuItem("Sashimi Boy/Stage 02/Apply Rockfish Stage")]
        public static void ApplyBatch()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode before authoring.");
            for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save unsaved scenes first.");
            byte[] preserved=File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath);
            if(!File.Exists(MusicPath)||!File.Exists(FleshPath))throw new InvalidOperationException("Copy the supplied Stage02 audio and cut-face image first.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var audio=(AudioImporter)AssetImporter.GetAtPath(MusicPath);
            var settings=audio.defaultSampleSettings;settings.loadType=AudioClipLoadType.DecompressOnLoad;settings.compressionFormat=AudioCompressionFormat.PCM;
            settings.preloadAudioData=true;audio.defaultSampleSettings=settings;audio.SaveAndReimport();
            if(!File.Exists(ScenePath))
            {
                // Reuse the current workstation, body and tested input system. The source scene is never saved.
                File.Copy(Stage01PlayableAuthoring.ScenePath,ScenePath);
                AssetDatabase.ImportAsset(ScenePath,ImportAssetOptions.ForceSynchronousImport);
            }
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            var view=Find<Stage01ButcheryPresenter>(scene);
            BuildModels(view.plateSlots.Length);
            ConfigureTiming(Find<Stage01SalmonTimingScaffold>(scene));
            ApplyFish(view);
            var hud=Find<Stage01SalmonHUD>(scene);
            hud.stageTitle="STAGE 02";hud.fishLabel="ROCKFISH / 우럭";hud.fishProgressPrefix="우럭 · ";
            OwnerJudgementAuthoring.Apply(hud);
            var accent=Find<RockfishStageAccent>(scene)??new GameObject("Rockfish_BoomBapCamera").AddComponent<RockfishStageAccent>();
            accent.timing=Find<Stage01SalmonTimingScaffold>(scene);accent.gameCamera=Find<Stage01CookingView>(scene).gameCamera;
            var stage=AssetDatabase.LoadAssetAtPath<StageDefinition>("Assets/_SashimiBoy/Data/Generated/Stage_02_Rockfish.asset");
            ApplyMetadata(stage);EditorUtility.SetDirty(stage);
            foreach(var root in scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new InvalidOperationException("Missing script: "+t.name);
            if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<AudioListener>()).Count(a=>a.enabled)!=1)throw new InvalidOperationException("Stage02 AudioListener count.");
            if(scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EventSystem>()).Count(e=>e.enabled)!=1)throw new InvalidOperationException("Stage02 EventSystem count.");
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Stage02 save failed.");
            var build=EditorBuildSettings.scenes.ToList();
            int index=build.FindIndex(s=>s.path==ScenePath);
            if(index<0)build.Add(new EditorBuildSettingsScene(ScenePath,true));else build[index].enabled=true;
            EditorBuildSettings.scenes=build.ToArray();AssetDatabase.SaveAssets();
            if(!File.ReadAllBytes(Stage01PlayableAuthoring.ScenePath).SequenceEqual(preserved))throw new InvalidOperationException("Stage1 scene changed during Stage2 authoring.");
            Debug.Log("[Stage02] Applied actual Rockfish assets, dedicated rockfish1 chart, six judged phases, plate, failure/retry and Day2 clear. Stage1 scene bytes unchanged.");
        }

        public static void ApplyMetadata(StageDefinition stage)
        {
            if(stage==null)throw new InvalidOperationException("Rockfish progression metadata is missing.");
            stage.sceneName="Stage02_Rockfish";stage.bpm=90f;stage.implementedInPrototype=true;
        }

        private static void ConfigureTiming(Stage01SalmonTimingScaffold timing)
        {
            timing.stageId=SashimiBoyConstants.StageIds.Rockfish;timing.chartAuthoringVersion=Version;
            timing.bpm=90f;timing.firstDownbeatSec=.020d;timing.gameplayStartSec=.020d+16d*60d/90d;timing.gameplayEndSec=.020d+192d*60d/90d;
            timing.musicClip=AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);timing.audioSource.clip=timing.musicClip;
            if(timing.musicClip==null || timing.musicClip.length<timing.gameplayEndSec)throw new InvalidOperationException("Stage02 clip range is invalid.");
            var pattern=AssetDatabase.LoadAssetAtPath<Stage01NotePatternDefinition>(PatternPath);
            if(pattern==null)
            {
                pattern=ScriptableObject.CreateInstance<Stage01NotePatternDefinition>();pattern.manualBarCount=44;pattern.repeatFromBar=44;pattern.subdivisionsPerBeat=2;
                int bar=1;
                for(int p=0;p<6;p++)for(int local=0;local<PhaseBars[p];local++,bar++)
                {
                    int[] steps=p==0||p==4?new[]{0,2,4,6}:p==2||p==5?new[]{0,2,3,4,6,7}:
                        p==1?(local%2==0?new[]{0,2,3,4,6}:new[]{0,2,4,6,7}):
                        (local%2==0?new[]{0,1,2,4,6}:new[]{0,2,4,5,6});
                    foreach(int step in steps)pattern.notes.Add(new Stage01PatternNote{barIndex=bar,eighthStepInBar=step,label="Rockfish phase "+(p+1)});
                }
                AssetDatabase.CreateAsset(pattern,PatternPath);
            }
            timing.notePatternProvider.pattern=pattern;timing.notePatternProvider.Initialize(timing);
            var chart=AssetDatabase.LoadAssetAtPath<SemanticBeatmapDefinition>(ChartPath);
            if(chart==null)
            {
                chart=ScriptableObject.CreateInstance<SemanticBeatmapDefinition>();
                var notes=timing.notePatternProvider.RuntimeNotes;
                chart.chart=ApprovedPhaseChart.Build(notes.Select(n=>n.songTimeSeconds).ToArray());
                int firstBar=1;
                for(int p=0;p<6;p++)
                {
                    int lastBar=firstBar+PhaseBars[p]-1;
                    var phaseNotes=notes.Where(n=>n.sourceBarIndex>=firstBar&&n.sourceBarIndex<=lastBar).ToArray();
                    var gate=chart.chart.phases[p];gate.firstNoteId=phaseNotes.First().sequenceIndex;gate.lastNoteId=phaseNotes.Last().sequenceIndex;gate.expectedNoteCount=phaseNotes.Length;
                    gate.successTransition="stage02.phase."+p+".success";gate.failureTransition="stage02.phase."+p+".failure";
                    foreach(var note in phaseNotes){var semantic=chart.chart.notes[note.sequenceIndex];semantic.phase=(FishPhase)p;semantic.action=gate.action;semantic.target=gate.target;}
                    firstBar=lastBar+1;
                }
                chart.StampSource(timing);AssetDatabase.CreateAsset(chart,ChartPath);
            }
            var valid=SemanticChartValidator.Validate(chart.chart,timing.notePatternProvider.RuntimeNotes.Count);
            if(!valid.IsValid||!chart.MatchesSource(timing))throw new InvalidOperationException("Existing Stage02 chart must be reviewed before reauthoring: "+valid);
            timing.semanticBeatmap=chart;timing.playOnStart=true;
            Debug.Log("[Stage02Chart] "+timing.notePatternProvider.RuntimeNotes.Count+" explicit notes, 44 bars, 90 BPM, "+timing.gameplayStartSec+".."+timing.gameplayEndSec+
                "; source="+chart.sourceScheduleIdentity);
        }

        private static void ApplyFish(Stage01ButcheryPresenter view)
        {
            var oldAssembly=view.assembly.gameObject;var oldHalf=view.filletHalf;var oldReserve=view.reservedFilletHalf;
            Object.DestroyImmediate(oldAssembly);Object.DestroyImmediate(oldHalf);Object.DestroyImmediate(oldReserve);
            const float fishScale=.8f;
            var board=view.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="Stage01_BoardRoot");
            float boardTop=board.GetComponentsInChildren<Renderer>().Max(r=>r.bounds.max.y);
            Vector3 position=new Vector3(0f,boardTop+.008f,-.12f);
            Debug.Log("[OwnerRockfishGround] board top="+boardTop+" fish floor="+position.y);
            var assembly=Instance(Root+"/PF_Stage02_RockfishAssembly.prefab",view.transform,"RockfishAssembly",position);
            view.assembly=assembly.GetComponent<SalmonAssemblyView>();
            view.filletHalf=Instance(Root+"/PF_RockfishFilletHalf.prefab",view.transform,"RockfishFilletHalf",position+Vector3.back*.08f*fishScale);
            view.reservedFilletHalf=Instance(Root+"/PF_RockfishFilletHalf.prefab",view.transform,"RockfishReservedHalf",position+Vector3.forward*.15f*fishScale);
            foreach(var item in new[]{assembly,view.filletHalf,view.reservedFilletHalf}) item.transform.localScale*=fishScale;
            view.filletSurface=view.filletHalf.GetComponent<Stage01FilletSurface>();
            view.filletHalf.SetActive(false);view.reservedFilletHalf.SetActive(false);
            view.slicePrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PF_RockfishSashimiSlice.prefab");
            // New meshes are authored in world metres; the inherited knife/body workspace keeps its original scale.
            view.sliceRoot.localScale=Vector3.one/view.transform.lossyScale.x;view.sliceRoot.rotation=Quaternion.identity;
            var starts=new[]{new Vector3(.21f,.18f,-.11f),new Vector3(-.35f,.13f,.15f),new Vector3(-.43f,.10f,0f),
                new Vector3(-.43f,.087f,0f),new Vector3(-.43f,.09f,-.08f),new Vector3(.16f,.06f,-.08f)};
            var ends=new[]{new Vector3(.21f,.18f,.11f),new Vector3(.11f,.13f,.15f),new Vector3(.15f,.10f,0f),
                new Vector3(.15f,.087f,0f),new Vector3(.13f,.09f,-.08f),new Vector3(-.44f,.06f,-.08f)};
            for(int p=0;p<6;p++){view.workAnchors[p].position=position+starts[p]*fishScale;view.workEndAnchors[p].position=position+ends[p]*fishScale;}
            view.GetComponent<Stage01CookingView>().openFishFocus.position=position+new Vector3(-.14f,.05f,0)*fishScale;
            var plate=view.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="CompletedFishPlate");
            float plateTop=plate.GetComponentsInChildren<Renderer>(true).Max(r=>r.bounds.max.y);
            for(int i=0;i<view.plateSlots.Length;i++)
            {
                Vector3 slot=view.plateSlots[i].position;slot.y=plateTop+.008f;view.plateSlots[i].position=slot;
                view.plateSlots[i].rotation=Quaternion.Euler(0f,(i%6-2.5f)*6f,0f);
            }
            view.gameObject.name="Stage02_RockfishWorkstation";
            EditorUtility.SetDirty(view);PrefabUtility.RecordPrefabInstancePropertyModifications(assembly.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(view.filletHalf.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(view.reservedFilletHalf.transform);
        }

        private static GameObject Instance(string path,Transform parent,string name,Vector3 position)
        {
            var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),parent);go.name=name;
            go.transform.SetPositionAndRotation(position,Quaternion.identity);go.transform.localScale=Vector3.one/parent.lossyScale.x;
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);return go;
        }
        private static T Find<T>(Scene scene) where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).FirstOrDefault();
    }
}
