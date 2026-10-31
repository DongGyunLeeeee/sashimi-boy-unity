using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static class Stage03OwnerFeedbackAuthoring
    {
        public static void ApplyBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            for (int i=0;i<SceneManager.sceneCount;i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scenes first.");
            CaptureSeat("before");
            KevinRiggedBodyAuthoring.BuildBatch();
            DayWorldAssetAuthoring.BuildCheolsuSeatingBatch();
            DayWorldVenueAuthoring.ApplyCheolsuSeatBatch();
            CaptureSeat("after");
            Stage03PlayableAuthoring.ApplyBatch();
            KevinRiggedBodyDiagnostics.InspectBatch();
            Debug.Log("[OwnerFeedback87] Authoritative generators completed. Full tests and Owner visual/music/input/save verification remain required.");
        }

        private static void CaptureSeat(string label)
        {
            var scene=EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/FishShopDialogue.unity",OpenSceneMode.Single);
            var customer=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<DayWorldNpc>(true)).Single(n=>n.npcId=="cheolsu");
            Vector3 target=customer.transform.position+Vector3.up*.75f;
            string directory=Path.Combine(KevinRiggedBodyAuthoring.EvidenceDirectory,"Cheolsu");
            Directory.CreateDirectory(directory);
            var cameraObject=new GameObject("TemporarySeatInspectionCamera");
            var camera=cameraObject.AddComponent<Camera>();
            var texture=new RenderTexture(1280,960,24);
            RenderTexture previous=RenderTexture.active;
            try
            {
                camera.targetTexture=texture;
                camera.fieldOfView=43f;
                camera.nearClipPlane=.03f;camera.farClipPlane=50f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.13f,.15f);
                foreach(int side in new[]{-1,1})
                {
                    Vector3 position=target+customer.transform.right*side*2.25f-customer.transform.forward*.30f+Vector3.up*.08f;
                    camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(target-position,Vector3.up));
                    camera.Render();RenderTexture.active=texture;
                    var pixels=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
                    try{pixels.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(directory,label+"-side"+side+".png"),pixels.EncodeToPNG());}
                    finally{Object.DestroyImmediate(pixels);}
                }
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;
                texture.Release();Object.DestroyImmediate(texture);Object.DestroyImmediate(cameraObject);
                // Temporary camera creation dirties the editor scene even after
                // destruction. Reload only this saved scene, without persisting it.
                EditorSceneManager.OpenScene("Assets/_SashimiBoy/Scenes/FishShopDialogue.unity",OpenSceneMode.Single);
            }
        }
    }
}
