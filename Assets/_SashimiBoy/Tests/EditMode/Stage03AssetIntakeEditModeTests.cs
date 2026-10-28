using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    // Asset intake only: no scene generation, gameplay, timing or reward decisions.
    public sealed class Stage03AssetIntakeEditModeTests
    {
        private const string ManifestPath = "Docs/Assets/STAGE03_ASSET_MANIFEST.json";
        private const int ExpectedAssetCount = 68;
        private const int ExpectedModelCount = 11;
        private const int ExpectedTextureCount = 55;

        [Serializable]
        private sealed class IntakeManifest
        {
            public int issue;
            public string receivedDate;
            public string baseCommit;
            public AssetEntry[] assets;
            public AudioMetadata audio;
            public FontMetadata font;
            public bool planningProvided;
        }

        [Serializable]
        private sealed class AssetEntry
        {
            public string archive;
            public string entry;
            public string path;
            public string role;
            public long bytes;
            public string sha256;
        }

        [Serializable]
        private sealed class AudioMetadata
        {
            public string encoding;
            public int channels;
            public int sampleRate;
            public int bitsPerSample;
            public int frames;
            public double durationSeconds;
        }

        [Serializable]
        private sealed class FontMetadata
        {
            public string family;
            public string localizedFamily;
            public string author;
            public string copyright;
            public bool licenseProvided;
        }

        [Serializable]
        private sealed class ModelObservation
        {
            public string path;
            public string role;
            public int rendererCount;
            public int meshCount;
            public long vertexCount;
            public int materialSlots;
            public int materialSlotsWithoutMainTexture;
            public int skinnedRendererCount;
            public int boneReferences;
        }

        [Serializable]
        private sealed class AuditReport
        {
            public int issue;
            public string completedUtc;
            public int assetCount;
            public int modelCount;
            public int textureCount;
            public bool sourceHashesMatch;
            public bool importedMainAssetTypesMatch;
            public bool riggedBodyHasSkinAndBones;
            public bool audioMatchesManifest;
            public bool fontImported;
            public bool planningProvided;
            public string textureBindingScope;
            public ModelObservation[] models;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        private static IntakeManifest ReadManifest()
        {
            string path = Path.Combine(ProjectRoot, ManifestPath);
            Assert.That(File.Exists(path), Is.True, "Stage3 intake manifest must exist.");
            var manifest = JsonUtility.FromJson<IntakeManifest>(File.ReadAllText(path));
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.issue, Is.EqualTo(70));
            Assert.That(manifest.assets, Is.Not.Null);
            Assert.That(manifest.assets.Length, Is.EqualTo(ExpectedAssetCount));
            Assert.That(manifest.assets.All(a => a != null && !string.IsNullOrEmpty(a.path)), Is.True);
            Assert.That(manifest.assets.Select(a => a.path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                Is.EqualTo(manifest.assets.Length), "Each supplied asset needs one unambiguous repository path.");
            Assert.That(manifest.audio, Is.Not.Null);
            Assert.That(manifest.font, Is.Not.Null);
            return manifest;
        }

        private static string Extension(AssetEntry entry) => Path.GetExtension(entry.path).ToLowerInvariant();

        private static string SourcePath(AssetEntry entry)
        {
            Assert.That(entry.path.StartsWith("Assets/", StringComparison.Ordinal), Is.True, entry.path);
            Assert.That(entry.path.Contains('\\'), Is.False, "Manifest paths must be repository-relative slash paths.");
            string absolute = Path.GetFullPath(Path.Combine(ProjectRoot, entry.path));
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            Assert.That(absolute.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase), Is.True,
                "An intake path must remain under this project's Assets directory: " + entry.path);
            return absolute;
        }

        [Test]
        public void SuppliedOriginals_MatchAllManifestSizesAndSha256Hashes()
        {
            var manifest = ReadManifest();
            foreach (var entry in manifest.assets)
            {
                string path = SourcePath(entry);
                Assert.That(File.Exists(path), Is.True, entry.path);
                Assert.That(new FileInfo(path).Length, Is.EqualTo(entry.bytes), entry.path);
                Assert.That(entry.bytes, Is.GreaterThan(0L), entry.path);
                Assert.That(entry.sha256, Does.Match("^[a-f0-9]{64}$"), entry.path);
                using (var input = File.OpenRead(path))
                using (var sha = SHA256.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                    Assert.That(hash, Is.EqualTo(entry.sha256), entry.path);
                }
            }
        }

        [Test]
        public void SuppliedAssets_ImportAsTheirExpectedUnityMainAssetTypes()
        {
            var manifest = ReadManifest();
            int models = 0, textures = 0, music = 0, fonts = 0;
            foreach (var entry in manifest.assets)
            {
                UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(entry.path);
                Assert.That(asset, Is.Not.Null, "Unity main asset did not import: " + entry.path);
                switch (Extension(entry))
                {
                    case ".fbx": Assert.That(asset, Is.InstanceOf<GameObject>(), entry.path); models++; break;
                    case ".jpeg":
                    case ".jpg":
                    case ".png": Assert.That(asset, Is.InstanceOf<Texture2D>(), entry.path); textures++; break;
                    case ".wav": Assert.That(asset, Is.InstanceOf<AudioClip>(), entry.path); music++; break;
                    case ".ttf":
                    case ".otf": Assert.That(asset, Is.InstanceOf<Font>(), entry.path); fonts++; break;
                    default: Assert.Fail("Unexpected intake file type: " + entry.path); break;
                }
            }
            Assert.That(models, Is.EqualTo(ExpectedModelCount));
            Assert.That(textures, Is.EqualTo(ExpectedTextureCount));
            Assert.That(music, Is.EqualTo(1));
            Assert.That(fonts, Is.EqualTo(1));
        }

        private static ModelObservation InspectModel(AssetEntry entry)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(entry.path);
            Assert.That(model, Is.Not.Null, entry.path);
            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
            Assert.That(renderers.Length, Is.GreaterThan(0), "Model has no mesh renderer: " + entry.path);
            var observation = new ModelObservation { path = entry.path, role = entry.role, rendererCount = renderers.Length };
            foreach (var renderer in renderers)
            {
                Mesh mesh;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    mesh = skin.sharedMesh;
                    observation.skinnedRendererCount++;
                    observation.boneReferences += skin.bones.Length;
                }
                else
                {
                    var filter = renderer.GetComponent<MeshFilter>();
                    Assert.That(filter, Is.Not.Null, entry.path + ": " + renderer.name);
                    mesh = filter.sharedMesh;
                }
                Assert.That(mesh, Is.Not.Null, entry.path + ": " + renderer.name);
                Assert.That(mesh.vertexCount, Is.GreaterThan(0), entry.path + ": " + mesh.name);
                Assert.That(mesh.subMeshCount, Is.GreaterThan(0), entry.path + ": " + mesh.name);
                ulong indices = 0;
                for (int i = 0; i < mesh.subMeshCount; i++) indices += mesh.GetIndexCount(i);
                Assert.That(indices, Is.GreaterThan(0UL), entry.path + ": " + mesh.name);
                observation.meshCount++;
                observation.vertexCount += mesh.vertexCount;
                var materials = renderer.sharedMaterials;
                Assert.That(materials.Length, Is.GreaterThan(0), entry.path + ": " + renderer.name);
                Assert.That(materials.All(m => m != null), Is.True,
                    "Imported model contains a missing material slot: " + entry.path + ": " + renderer.name);
                observation.materialSlots += materials.Length;
                // Texture remapping/material generation belongs to later implementation.
                observation.materialSlotsWithoutMainTexture += materials.Count(m => m.mainTexture == null);
            }
            return observation;
        }

        [Test]
        public void SuppliedModels_HaveNonemptyMeshesRenderersAndMaterialSlots()
        {
            var models = ReadManifest().assets.Where(a => Extension(a) == ".fbx").ToArray();
            Assert.That(models.Length, Is.EqualTo(ExpectedModelCount));
            foreach (var entry in models) InspectModel(entry);
        }

        [Test]
        public void RiggedBody_RetainsImportedSkinAndBoneReferences()
        {
            var entries = ReadManifest().assets.Where(a => a.role == "RiggedBody" && Extension(a) == ".fbx").ToArray();
            Assert.That(entries.Length, Is.EqualTo(1));
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(entries[0].path);
            Assert.That(model, Is.Not.Null);
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skins.Length, Is.GreaterThan(0), "The supplied body needs its imported skin.");
            foreach (var skin in skins)
            {
                Assert.That(skin.sharedMesh, Is.Not.Null, skin.name);
                Assert.That(skin.bones.Length, Is.GreaterThan(0), skin.name);
                Assert.That(skin.bones.All(b => b != null), Is.True, "Missing bone reference: " + skin.name);
                Assert.That(skin.sharedMesh.bindposes.Length, Is.GreaterThan(0), skin.name);
                Assert.That(skin.sharedMesh.bindposes.Length, Is.EqualTo(skin.bones.Length), skin.name);
            }
        }

        [Test]
        public void SuppliedMusic_RetainsSourceDurationChannelsAndSampleRate()
        {
            var manifest = ReadManifest();
            var entries = manifest.assets.Where(a => Extension(a) == ".wav").ToArray();
            Assert.That(entries.Length, Is.EqualTo(1));
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(entries[0].path);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.channels, Is.EqualTo(manifest.audio.channels));
            Assert.That(clip.frequency, Is.EqualTo(manifest.audio.sampleRate));
            Assert.That(clip.samples, Is.EqualTo(manifest.audio.frames));
            Assert.That((double)clip.length, Is.EqualTo(manifest.audio.durationSeconds).Within(.001d));
        }

        [Test]
        public void SuppliedFont_ImportsAsAFontAsset()
        {
            var entries = ReadManifest().assets.Where(a => Extension(a) == ".ttf" || Extension(a) == ".otf").ToArray();
            Assert.That(entries.Length, Is.EqualTo(1));
            var font = AssetDatabase.LoadAssetAtPath<Font>(entries[0].path);
            Assert.That(font, Is.Not.Null);
            Assert.That(font.name, Is.Not.Empty);
        }

        // Deterministic Editor-only audit entry point for the manual intake run.
        // The report is local evidence, not a generated game asset or a scene mutation.
        public static void AuditBatch()
        {
            var tests = new Stage03AssetIntakeEditModeTests();
            tests.SuppliedOriginals_MatchAllManifestSizesAndSha256Hashes();
            tests.SuppliedAssets_ImportAsTheirExpectedUnityMainAssetTypes();
            tests.SuppliedModels_HaveNonemptyMeshesRenderersAndMaterialSlots();
            tests.RiggedBody_RetainsImportedSkinAndBoneReferences();
            tests.SuppliedMusic_RetainsSourceDurationChannelsAndSampleRate();
            tests.SuppliedFont_ImportsAsAFontAsset();
            var manifest = ReadManifest();
            var models = manifest.assets.Where(a => Extension(a) == ".fbx").Select(InspectModel).ToArray();
            var report = new AuditReport
            {
                issue = manifest.issue,
                completedUtc = DateTime.UtcNow.ToString("o"),
                assetCount = manifest.assets.Length,
                modelCount = models.Length,
                textureCount = manifest.assets.Count(a => Extension(a) == ".jpeg" || Extension(a) == ".jpg" || Extension(a) == ".png"),
                sourceHashesMatch = true,
                importedMainAssetTypesMatch = true,
                riggedBodyHasSkinAndBones = true,
                audioMatchesManifest = true,
                fontImported = true,
                planningProvided = manifest.planningProvided,
                textureBindingScope = "Unbound main textures are reported only; material authoring belongs to later implementation.",
                models = models
            };
            string directory = Path.Combine(ProjectRoot, "Logs", "Stage03AssetIntake");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "asset-audit.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("[Stage03AssetIntake] 68 source files, 11 model imports, 55 textures, source audio and font verified. " + path);
        }
    }
}
