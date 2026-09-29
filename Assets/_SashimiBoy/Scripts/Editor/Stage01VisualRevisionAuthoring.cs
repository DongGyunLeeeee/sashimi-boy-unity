using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SashimiBoy.Semantics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SashimiBoy.EditorTools
{
    public static partial class Stage01PlayableAuthoring
    {
        private static void BuildSliceSurfaces(GameObject wrapper, Renderer[] renderers)
        {
            Bounds bounds = GeometryBounds(wrapper);
            var meshes = wrapper.GetComponentsInChildren<MeshFilter>().Select(filter =>
            {
                Matrix4x4 matrix = wrapper.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                return (vertices: filter.sharedMesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                    triangles: filter.sharedMesh.triangles);
            }).ToArray();
            Material material = GetMaterial("CrossSection", "Standard");
            material.mainTexture = Texture(Source + "CrossSection/salmon_crosssection.png");
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Glossiness", .18f);
            AssetDatabase.SaveAssetIfDirty(material);
            var surface = wrapper.AddComponent<Stage01FilletSurface>();
            surface.sourceRenderers = renderers;
            surface.cutCaps = new GameObject[12];
            surface.cutPositions = new float[12];
            surface.workHeight = bounds.max.y - bounds.size.y * .18f;
            for (int cutIndex = 0; cutIndex < 12; cutIndex++)
            {
                float cut = Mathf.Lerp(bounds.max.x - bounds.size.x * .12f,
                    bounds.min.x + bounds.size.x * .17f, cutIndex / 11f);
                surface.cutPositions[cutIndex] = cut;
                var points = new List<Vector2>();
                foreach (var mesh in meshes)
                    for (int i = 0; i < mesh.triangles.Length; i += 3)
                        for (int edge = 0; edge < 3; edge++)
                        {
                            Vector3 a = mesh.vertices[mesh.triangles[i + edge]], b = mesh.vertices[mesh.triangles[i + (edge + 1) % 3]];
                            if ((a.x < cut) == (b.x < cut) || Mathf.Abs(a.x - b.x) < .000001f) continue;
                            Vector3 p = Vector3.Lerp(a, b, (cut - a.x) / (b.x - a.x));
                            points.Add(new Vector2(p.y, p.z));
                        }
                var hull = Hull(points);
                Require(hull.Count >= 3, "No cross-section contour at authored cut " + cutIndex);
                int n = hull.Count;
                float minY = hull.Min(p => p.x), maxY = hull.Max(p => p.x);
                float minZ = hull.Min(p => p.y), maxZ = hull.Max(p => p.y);
                var positions = new Vector3[n * 2]; var normals = new Vector3[n * 2]; var uv = new Vector2[n * 2];
                var triangles = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    positions[i] = positions[n + i] = new Vector3(cut + .0015f, hull[i].x, hull[i].y);
                    normals[i] = Vector3.right; normals[n + i] = Vector3.left;
                    uv[i] = uv[n + i] = new Vector2(Mathf.Lerp(.2f, .8f, Mathf.InverseLerp(minZ, maxZ, hull[i].y)),
                        Mathf.Lerp(.25f, .75f, Mathf.InverseLerp(minY, maxY, hull[i].x)));
                    if (i >= 2) triangles.AddRange(new[] { 0, i - 1, i, n, n + i, n + i - 1 });
                }
                var meshAsset = new Mesh { name = "FilletCut_" + cutIndex, vertices = positions, normals = normals,
                    uv = uv, triangles = triangles.ToArray() };
                meshAsset.RecalculateBounds();
                Mesh saved = SaveMesh(meshAsset, Root + "/FilletCut_" + cutIndex.ToString("00") + ".asset");
                var cap = new GameObject("CutFace_" + cutIndex.ToString("00"), typeof(MeshFilter), typeof(MeshRenderer));
                cap.transform.SetParent(wrapper.transform, false);
                cap.GetComponent<MeshFilter>().sharedMesh = saved;
                cap.GetComponent<MeshRenderer>().sharedMaterial = material;
                surface.cutCaps[cutIndex] = cap;
            }
            surface.ShowCut(0);
            Debug.Log("[Stage1Revision] Half retains its original top; 12 vertical cut faces authored from its mesh.");
        }

        private static Mesh SaveMesh(Mesh generated, string path)
        {
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved == null) { AssetDatabase.CreateAsset(generated, path); saved = generated; }
            else { EditorUtility.CopySerialized(generated, saved); Object.DestroyImmediate(generated); }
            EditorUtility.SetDirty(saved); AssetDatabase.SaveAssetIfDirty(saved);
            return saved;
        }

        private static void BuildGripHandAsset()
        {
            const string handRoot = "Assets/_SashimiBoy/Art/Source/Shared/Hands/DevMops/";
            ConfigureModel(handRoot + "arms_low_poly.fbx");
            Mesh source = AssetDatabase.LoadAssetAtPath<GameObject>(handRoot + "arms_low_poly.fbx").GetComponentInChildren<MeshFilter>().sharedMesh;
            // The CC0 FBX has a static mesh; its Rigify rig is in the separate .blend.
            // Author a grip variant here, preserving every source byte and its UVs.
            Vector3[] original = source.vertices;
            Vector3[] positions = new Vector3[original.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = original[i] * 100f;
                if (p.y < -.545f && p.x > .425f)
                {
                    float angle = Mathf.Clamp01((-p.y - .545f) / .19f) * 2.3f;
                    p.y = -.545f - Mathf.Sin(angle) * .085f;
                    p.z -= (1f - Mathf.Cos(angle)) * .085f;
                }
                else if (p.x < .44f && p.y < -.40f)
                {
                    float curl = Mathf.Clamp01((.44f - p.x) / .08f);
                    p.x += .075f * curl; p.z -= .06f * curl;
                }
                positions[i] = new Vector3(p.x - .505f, p.z - .465f, -(p.y + .55f)) * 1.75f;
            }
            var triangles = new List<int>(); var sourceTriangles = source.triangles;
            for (int i = 0; i < sourceTriangles.Length; i += 3)
            {
                int a = sourceTriangles[i], b = sourceTriangles[i + 1], c = sourceTriangles[i + 2];
                if (original[a].x <= 0f || original[b].x <= 0f || original[c].x <= 0f ||
                    original[a].y > .0018f || original[b].y > .0018f || original[c].y > .0018f) continue;
                triangles.AddRange(new[] { a, b, c });
            }
            var mesh = new Mesh { name = "DevMops_KnifeGrip", vertices = positions, uv = source.uv, triangles = triangles.ToArray() };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var saved = SaveMesh(mesh, Root + "/MS_KnifeGripHand.asset");
            var material = GetMaterial("KnifeGripHand", "Standard");
            material.mainTexture = Texture(handRoot + "Hand Texture.png");
            material.SetFloat("_Metallic", 0f); material.SetFloat("_Glossiness", .15f);
            AssetDatabase.SaveAssetIfDirty(material);
            var root = new GameObject("PF_KnifeGripHand", typeof(MeshFilter), typeof(MeshRenderer));
            root.GetComponent<MeshFilter>().sharedMesh = saved;
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            try { PrefabUtility.SaveAsPrefabAsset(root, Root + "/PF_KnifeGripHand.prefab"); }
            finally { Object.DestroyImmediate(root); }
        }

        private static void ApplyOwnerVisualRevision(Scene scene, Stage01ButcheryPresenter view, Stage01SalmonPresentationController presentation)
        {
            var timing = view.timing;
            if (ApprovedPhaseChart.ApplyPlayableActionSequence(timing.semanticBeatmap.chart))
            {
                EditorUtility.SetDirty(timing.semanticBeatmap);
                AssetDatabase.SaveAssetIfDirty(timing.semanticBeatmap);
            }
            timing.emptyInputScorePenalty = 100; timing.emptyInputQualityPenalty = .35f;
            timing.useAuthoredStageCamera = true;
            GroundFishAndFillets(scene, view);
            view.filletSurface = view.filletHalf.GetComponent<Stage01FilletSurface>();
            view.reservedFilletHalf = Instance(Root + "/PF_FilletHalf.prefab", view.transform, "ReservedFilletHalf");
            view.reservedFilletHalf.transform.position = view.filletHalf.transform.position + new Vector3(0f, 0f, 1.08f);
            view.reservedFilletHalf.SetActive(false);
            PlaceBonesOnHalf(view);
            AuthorWorkAnchors(view);
            presentation.playerKnife.gameObject.SetActive(false);
            view.knifeRoot = BuildRealKnife(view.transform);
            view.knifeGripAnchor = view.knifeRoot.Find("GripAnchor");
            view.knifeContactAnchor = view.knifeRoot.Find("BladeContact");
            if (presentation.bossDemo != null)
            {
                presentation.bossDemo.butchery = view;
                EditorUtility.SetDirty(presentation.bossDemo);
            }
            view.handRoot.rotation = Quaternion.identity;
            ConfigurePerspective(scene);
            BuildPhaseGauge(presentation.hud);
            ConfigureJudgementFeedback(scene);
            Material plateMat = GetMaterial("ServingPlate", "Standard");
            plateMat.color = new Color(.86f, .89f, .85f); plateMat.SetFloat("_Glossiness", .25f);
            AssetDatabase.SaveAssetIfDirty(plateMat);
            var plate = All<Transform>(scene).First(t => t.name == "CompletedFishPlate");
            foreach (Renderer renderer in plate.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = plateMat;
            // Scene-specific material variants keep the supplied fish from looking metallic.
            foreach (Renderer renderer in view.assembly.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                {
                    string id = "StageFish_" + source.name;
                    var mat = GetMaterial(id, "Standard");
                    mat.CopyPropertiesFromMaterial(source);
                    mat.DisableKeyword("_METALLICGLOSSMAP"); mat.SetFloat("_Metallic", 0f);
                    mat.SetFloat("_Glossiness", .25f); mat.SetFloat("_BumpScale", .45f);
                    AssetDatabase.SaveAssetIfDirty(mat); return mat;
                }).ToArray();
            }
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(presentation.hud);
        }

        private static Transform BuildRealKnife(Transform parent)
        {
            var holder = Child(parent, "SashimiKnife_ProvidedAsset");
            var model = Instance("Assets/_SashimiBoy/Art/Generated/Prefabs/FishShop/PF_Prop_KitchenKnife.prefab", holder, "KitchenKnife");
            Vector3[] points = ModelPoints(holder);
            Vector3 mean = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Length;
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (Vector3 value in points)
            {
                Vector3 p = value - mean;
                xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z;
                yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z;
            }
            Vector3 major = Vector3.one.normalized;
            for (int i = 0; i < 24; i++) major = new Vector3((float)(xx * major.x + xy * major.y + xz * major.z),
                (float)(xy * major.x + yy * major.y + yz * major.z), (float)(xz * major.x + yz * major.y + zz * major.z)).normalized;
            model.transform.localRotation = Quaternion.FromToRotation(major, Vector3.forward);
            points = ModelPoints(holder); mean = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Length;
            xx = xy = yy = 0;
            foreach (var value in points) { var p = value - mean; xx += p.x * p.x; xy += p.x * p.y; yy += p.y * p.y; }
            float widthAngle = .5f * Mathf.Atan2((float)(2 * xy), (float)(xx - yy)) * Mathf.Rad2Deg;
            model.transform.localRotation = Quaternion.AngleAxis(90f - widthAngle, Vector3.forward) * model.transform.localRotation;
            points = ModelPoints(holder); Bounds bounds = PointsBounds(points);
            if (BandBounds(points, bounds, .7f, .9f).size.y < BandBounds(points, bounds, .1f, .3f).size.y)
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            bounds = GeometryBounds(holder.gameObject);
            float scale = 1.55f / bounds.size.z;
            model.transform.localScale *= scale;
            model.transform.localPosition = -bounds.center * scale;
            points = ModelPoints(holder); bounds = PointsBounds(points);
            Bounds handle = BandBounds(points, bounds, .07f, .26f);
            Bounds blade = BandBounds(points, bounds, .5f, .72f);
            Child(holder, "GripAnchor").localPosition = handle.center;
            Child(holder, "BladeContact").localPosition = new Vector3(blade.center.x, blade.min.y, blade.center.z);
            foreach (var collider in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
            Debug.Log("[Stage1Revision] Provided KitchenKnife bounds=" + GeometryBounds(holder.gameObject));
            return holder;
        }

        private static Vector3[] ModelPoints(Transform root) => root.GetComponentsInChildren<MeshFilter>(true)
            .SelectMany(filter => filter.sharedMesh.vertices.Select(p => root.InverseTransformPoint(filter.transform.TransformPoint(p)))).ToArray();
        private static Bounds PointsBounds(IEnumerable<Vector3> points)
        {
            bool found = false; Bounds bounds = default;
            foreach (var point in points) { if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point); }
            Require(found, "Empty authored geometry band."); return bounds;
        }
        private static Bounds BandBounds(Vector3[] points, Bounds bounds, float from, float to) =>
            PointsBounds(points.Where(p => p.z >= Mathf.Lerp(bounds.min.z, bounds.max.z, from) && p.z <= Mathf.Lerp(bounds.min.z, bounds.max.z, to)));

        private static void GroundFishAndFillets(Scene scene, Stage01ButcheryPresenter view)
        {
            var board = All<Transform>(scene).First(t => t.name == "BoardSurface");
            float surface = WorldBounds(board.gameObject).max.y + .008f;
            foreach (var piece in new[] { view.assembly.body, view.assembly.head })
            {
                piece.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                if (piece == view.assembly.head) { var local = piece.transform.localPosition; local.z = 1.34f; piece.transform.localPosition = local; }
                var position = piece.transform.position; position.y += surface - WorldBounds(piece.gameObject).min.y;
                piece.transform.position = position; piece.CaptureAuthoredPose();
                PrefabUtility.RecordPrefabInstancePropertyModifications(piece.transform); PrefabUtility.RecordPrefabInstancePropertyModifications(piece);
            }
            var fillet = view.assembly.fillet;
            var filletPosition = fillet.transform.position; filletPosition.y += surface - WorldBounds(fillet.gameObject).min.y;
            fillet.transform.position = filletPosition; fillet.CaptureAuthoredPose();
            PrefabUtility.RecordPrefabInstancePropertyModifications(fillet.transform); PrefabUtility.RecordPrefabInstancePropertyModifications(fillet);
            var halfPosition = view.filletHalf.transform.position; halfPosition.y = surface; view.filletHalf.transform.position = halfPosition;
            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube); tray.name = "RemovedPartsTray"; tray.transform.SetParent(view.transform, false);
            tray.transform.position = new Vector3(-3.25f, surface, 1.2f); tray.transform.localScale = new Vector3(2.15f, .055f, 1.35f);
            Object.DestroyImmediate(tray.GetComponent<Collider>());
            var mat = GetMaterial("WasteTray", "Standard"); mat.color = new Color(.16f, .20f, .21f); mat.SetFloat("_Glossiness", .2f);
            AssetDatabase.SaveAssetIfDirty(mat); tray.GetComponent<Renderer>().sharedMaterial = mat;
            view.wasteAnchor.position = new Vector3(-3.55f, surface + .04f, .94f);
        }

        private static void AuthorWorkAnchors(Stage01ButcheryPresenter view)
        {
            Bounds body = WorldBounds(view.assembly.body.gameObject), head = WorldBounds(view.assembly.head.gameObject);
            Bounds fillet = WorldBounds(view.assembly.fillet.gameObject);
            bool headAtLeft = head.center.x < body.center.x;
            float neck = headAtLeft ? (head.max.x + body.min.x) * .5f : (head.min.x + body.max.x) * .5f;
            Vector3 neckPoint = new Vector3(neck, body.max.y - .07f, body.center.z);
            var starts = new[] { neckPoint,
                new Vector3(body.max.x - .25f, body.max.y - .1f, body.min.z + .07f),
                new Vector3(fillet.max.x - .2f, fillet.max.y + .08f, fillet.center.z),
                new Vector3(fillet.max.x - .18f, fillet.max.y + .02f, fillet.center.z),
                view.filletHalf.transform.position, view.filletSurface.NextCutPosition(0) };
            var ends = new[] { neckPoint,
                new Vector3(body.min.x + .25f, body.max.y - .1f, body.max.z - .07f),
                new Vector3(fillet.min.x + .2f, fillet.max.y + .08f, fillet.center.z),
                new Vector3(fillet.min.x + .18f, fillet.max.y + .02f, fillet.center.z), starts[4], starts[5] };
            view.workAnchors = new Transform[6]; view.workEndAnchors = new Transform[6];
            var root = Child(view.transform, "AuthoredWorkTargets");
            for (int i = 0; i < 6; i++)
            {
                view.workAnchors[i] = Child(root, "Action_" + i + "_Start"); view.workAnchors[i].position = starts[i];
                view.workEndAnchors[i] = Child(root, "Action_" + i + "_End"); view.workEndAnchors[i].position = ends[i];
            }
            Debug.Log("[Stage1Revision] Neck contact=" + neckPoint + "; body=" + body + "; head=" + head);
        }

        private static Bounds WorldBounds(GameObject source)
        {
            var points = new List<Vector3>();
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++) points.Add(filter.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            }
            return PointsBounds(points);
        }

        private static void PlaceBonesOnHalf(Stage01ButcheryPresenter view)
        {
            view.filletHalf.SetActive(true);
            var colliders = view.filletSurface.sourceRenderers.Select(renderer =>
            {
                var collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh; return collider;
            }).ToArray();
            try
            {
                Physics.SyncTransforms();
                Bounds bounds = WorldBounds(view.filletSurface.sourceRenderers[0].gameObject);
                for (int i = 0; i < view.assembly.pinBones.Length; i++)
                {
                    Vector3 point = new Vector3(Mathf.Lerp(bounds.min.x + .35f, bounds.max.x - .35f, i / 7f), bounds.max.y + 2f, bounds.center.z);
                    float height = float.NegativeInfinity;
                    foreach (var collider in colliders)
                        if (collider.Raycast(new Ray(point, Vector3.down), out var hit, 5f)) height = Mathf.Max(height, hit.point.y);
                    Require(float.IsFinite(height), "No half surface for PinBone " + i);
                    point.y = height + .035f;
                    var bone = view.assembly.pinBones[i]; bone.transform.position = point; bone.CaptureAuthoredPose();
                    view.assembly.pinBoneAnchors[i].position = point;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone.transform);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(bone);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view.assembly.pinBoneAnchors[i]);
                }
            }
            finally { foreach (var collider in colliders) Object.DestroyImmediate(collider); view.filletHalf.SetActive(false); }
        }

        private static void ConfigurePerspective(Scene scene)
        {
            Camera camera = Find<Camera>(scene);
            camera.orthographic = false; camera.fieldOfView = 43f;
            camera.transform.position = new Vector3(2.8f, 6f, -6.5f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0f, .5f, .2f) - camera.transform.position, Vector3.up);
            camera.nearClipPlane = .08f; camera.farClipPlane = 100f;
            RenderSettings.ambientLight = new Color(.43f, .45f, .48f);
            foreach (var light in All<Light>(scene))
                if (light.type == LightType.Directional) light.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(camera);
        }

        private static Image Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            Transform existing = parent.Find(name);
            var obj = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = obj.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
            return image;
        }

        private static void BuildPhaseGauge(Stage01SalmonHUD hud)
        {
            var panel = Panel(hud.transform, "PhaseQualityGauge", Vector2.zero, new Vector2(202f, 400f), new Color(.025f, .045f, .055f, .9f));
            var rect = panel.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(0f, .5f);
            rect.pivot = new Vector2(0f, .5f); rect.anchoredPosition = new Vector2(22f, -12f);
            TextObject(panel.transform, "Title", "단계 통과 품질", new Vector2(0f, 164f), new Vector2(192f, 40f), 24);
            var track = Panel(panel.transform, "Track", new Vector2(-68f, -3f), new Vector2(25f, 246f), new Color(.2f, .24f, .25f));
            var fill = Panel(track.transform, "Fill", Vector2.zero, track.rectTransform.sizeDelta, new Color(1f, .7f, .2f));
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Vertical; fill.fillOrigin = 0; fill.fillAmount = 0f;
            // Filled Image requires a sprite even for a solid-color meter.
            fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            hud.phaseQualityFill = fill;
            var marker = Panel(track.transform, "PassLine", Vector2.zero, new Vector2(45f, 3f), Color.white);
            hud.phaseThresholdMarker = marker.rectTransform;
            hud.phaseRequirementText = TextObject(panel.transform, "Required", "", new Vector2(23f, 65f), new Vector2(126f, 88f), 22);
            hud.phaseQualityText = TextObject(panel.transform, "Current", "", new Vector2(23f, -35f), new Vector2(126f, 65f), 23);
            hud.phaseRemainingText = TextObject(panel.transform, "Remaining", "", new Vector2(0f, -155f), new Vector2(190f, 75f), 21);
        }

        private static void ConfigureJudgementFeedback(Scene scene)
        {
            var material = GetMaterial("JudgementCutout", "SashimiBoy/UIWhiteKey"); AssetDatabase.SaveAssetIfDirty(material);
            foreach (var feedback in All<JudgementFeedbackView>(scene))
            {
                var rect = feedback.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(.86f, .66f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(220f, 160f); rect.localScale = Vector3.one;
                var backdrop = feedback.GetComponent<Image>(); if (backdrop != null) backdrop.color = Color.clear;
                if (feedback.icon != null)
                {
                    feedback.icon.material = material; feedback.icon.rectTransform.sizeDelta = new Vector2(190f, 100f);
                    feedback.icon.rectTransform.anchoredPosition = new Vector2(0f, 26f);
                    feedback.icon.raycastTarget = false;
                }
                if (feedback.fallbackText != null) feedback.fallbackText.fontSize = 26;
                if (feedback.offsetText != null) { feedback.offsetText.fontSize = 20; feedback.offsetText.rectTransform.anchoredPosition = new Vector2(0f, -36f); }
                if (feedback.directionText != null) { feedback.directionText.fontSize = 19; feedback.directionText.rectTransform.anchoredPosition = new Vector2(0f, -62f); }
                feedback.initialScale = .94f; feedback.scaleInDuration = .04f; feedback.holdDuration = .25f; feedback.fadeOutDuration = .12f;
                EditorUtility.SetDirty(feedback);
            }
        }
    }
}
