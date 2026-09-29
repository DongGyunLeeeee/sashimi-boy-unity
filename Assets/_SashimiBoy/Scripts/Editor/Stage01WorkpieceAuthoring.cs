using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage01PlayableAuthoring
    {
        [MenuItem("Sashimi Boy/Stage 01/Repair Open Fish Workpiece Poses")]
        public static void ApplyWorkpiecePosesBatch()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play mode before authoring.");
            for(int i=0;i<SceneManager.sceneCount;i++)
                Require(!SceneManager.GetSceneAt(i).isDirty,"Save unsaved scenes before authoring.");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            CalibrateOpenFish(Find<Stage01ButcheryPresenter>(scene));
            ScanScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Stage1 workpiece save failed.");
            NormalizeSceneWhitespace();
        }

        // Called after cooking-space calibration, including from the original Stage1 generator.
        // Change only the scene assembly poses and their matching contact anchors; retain source meshes/GUIDs.
        private static void CalibrateOpenFish(Stage01ButcheryPresenter view)
        {
            Bounds body = WorkpieceBounds(view.assembly.body.gameObject);
            Vector3 center = body.center + Vector3.back * .04f;
            FitWorkpiece(view.assembly.fillet, Quaternion.identity,
                new Vector3(body.size.x * .90f, body.size.y * .55f, body.size.z * .92f), center, body.min.y);
            Bounds fillet = WorkpieceBounds(view.assembly.fillet.gameObject);
            FitWorkpiece(view.assembly.spine, Quaternion.Euler(0f,90f,90f),
                new Vector3(fillet.size.x * .88f, body.size.y * .25f, fillet.size.z * .65f),
                fillet.center, fillet.max.y + .003f);
            Bounds spine = WorkpieceBounds(view.assembly.spine.gameObject);
            // The detached fins begin at the fish surface, not above Kevin's line of sight.
            var fins = view.assembly.fins;
            fins.transform.localPosition = Vector3.zero;
            fins.transform.localRotation = Quaternion.Euler(0f,0f,90f);
            Bounds finBounds = WorkpieceBounds(fins.gameObject);
            fins.transform.position += new Vector3(body.center.x-finBounds.center.x,
                body.max.y-finBounds.min.y, body.center.z-finBounds.center.z);
            CapturePiecePose(fins, view.assembly.finsAnchor);
            CapturePiecePose(view.assembly.fillet, view.assembly.filletAnchor);
            CapturePiecePose(view.assembly.spine, view.assembly.spineAnchor);
            SetOpenFishTargets(view, 2, spine);
            SetOpenFishTargets(view, 3, fillet);
            var cooking = view.GetComponent<Stage01CookingView>();
            if (cooking != null)
            {
                var focus = view.transform.Find("OpenFishFocus") ?? Child(view.transform,"OpenFishFocus");
                focus.position = fillet.center;
                cooking.openFishFocus = focus;
                EditorUtility.SetDirty(cooking);
            }
            EditorUtility.SetDirty(view);
            Debug.Log("[WorkpieceApplied] body=" + body + " fillet=" + fillet + " spine=" + spine);
        }

        private static void FitWorkpiece(SalmonAssemblyPieceView piece, Quaternion localRotation,
            Vector3 size, Vector3 center, float bottom)
        {
            piece.transform.localRotation = localRotation;
            piece.transform.localPosition = Vector3.zero;
            piece.transform.localScale = Vector3.one;
            Bounds bounds = WorkpieceBounds(piece.gameObject);
            Vector3 factors = new Vector3(size.x/bounds.size.x, size.y/bounds.size.y, size.z/bounds.size.z);
            // The authored axes are orthogonal. Convert positive world-axis sizing to each local axis.
            float Factor(Vector3 axis)
            {
                Vector3 world = piece.transform.rotation * axis;
                return Vector3.Dot(new Vector3(Mathf.Abs(world.x),Mathf.Abs(world.y),Mathf.Abs(world.z)), factors);
            }
            piece.transform.localScale = new Vector3(Factor(Vector3.right),Factor(Vector3.up),Factor(Vector3.forward));
            bounds = WorkpieceBounds(piece.gameObject);
            piece.transform.position += new Vector3(center.x-bounds.center.x,bottom-bounds.min.y,center.z-bounds.center.z);
        }

        private static void CapturePiecePose(SalmonAssemblyPieceView piece, Transform anchor)
        {
            piece.CaptureAuthoredPose();
            PrefabUtility.RecordPrefabInstancePropertyModifications(piece.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
            if (anchor != null)
            {
                anchor.SetPositionAndRotation(piece.transform.position,piece.transform.rotation);
                PrefabUtility.RecordPrefabInstancePropertyModifications(anchor);
            }
        }

        private static void SetOpenFishTargets(Stage01ButcheryPresenter view, int phase, Bounds bounds)
        {
            float inset = bounds.size.x * .10f;
            view.workAnchors[phase].position = new Vector3(bounds.min.x+inset,bounds.max.y+.005f,bounds.center.z);
            view.workEndAnchors[phase].position = new Vector3(bounds.max.x-inset,bounds.max.y+.005f,bounds.center.z);
        }

        public static void InspectWorkpiecePosesBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var view = Find<Stage01ButcheryPresenter>(scene);
            foreach (var piece in view.assembly.Pieces)
            {
                Debug.Log("[Workpiece] " + piece.StableId + " pose=" + piece.transform.localEulerAngles +
                    " scale=" + piece.transform.lossyScale + " geometry=" + WorkpieceBounds(piece.gameObject) + " envelope=" + WorldBounds(piece.gameObject));
                if (piece != view.assembly.spine) continue;
                var rotation = piece.transform.localRotation;
                foreach (var angles in new[] { Vector3.zero, new Vector3(90,0,0), new Vector3(0,0,90), new Vector3(0,0,270) })
                {
                    piece.transform.localRotation = Quaternion.Euler(angles);
                    Debug.Log("[SpineCandidate] " + angles + " geometry=" + WorkpieceBounds(piece.gameObject));
                }
                piece.transform.localRotation = rotation;
            }
        }

        private static Bounds WorkpieceBounds(GameObject root) => PointsBounds(root.GetComponentsInChildren<MeshFilter>(true)
            .SelectMany(filter => filter.sharedMesh.vertices.Select(filter.transform.TransformPoint)));
    }
}
