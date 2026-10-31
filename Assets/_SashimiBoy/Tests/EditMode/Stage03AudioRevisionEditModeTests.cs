using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SashimiBoy.Tests
{
    // Test-only, one-revision contract. This is not a production asset resolver.
    internal static class Stage03AudioRevisionContract
    {
        internal const string RevisionPath = "Docs/Assets/STAGE03_AUDIO_REVISION_MANIFEST.json";
        internal const string ReceiptPath = "Docs/Assets/STAGE03_ASSET_MANIFEST.json";
        internal const string ReceiptHash = "8a586c697634f95bc42dedb8ee33f9c6f1370ebcffb43104a068db2fc0f8de26";
        internal const string MusicPath = "Assets/_SashimiBoy/Audio/Music/Stage_03_Flounder/flounder1.wav";
        internal const string MusicGuid = "c4fa323b3fd58a44dbbb5f5f6b64d734";
        internal const string MetaHash = "58ced175e226bad09531c208934246d88a8addbc19bc54dd58430c3813019366";
        internal const string OriginalHash = "3d277b2dcd67feb1bebf937460d2cd694347aa3db020ece0bd6079f5a29fb065";
        internal const string CurrentHash = "3572605dec84991aabf1e4911eee443287da0f7e5c403b0a449aee403cf31062";
        internal const long OriginalBytes = 28485292L;
        internal const long CurrentBytes = 44350480L;
        internal const string OwnerDecision = "https://github.com/DongGyunLeeeee/sashimi-boy-unity/issues/85#issuecomment-6522789964";

        [Serializable] internal sealed class Audio
        {
            public string encoding;
            public int channels, sampleRate, bitsPerSample, frames;
            public double durationSeconds;
        }
        [Serializable] internal sealed class ReceiptReference { public string path, sha256; }
        [Serializable] internal sealed class Original
        {
            public string path, guid, inputName, sha256;
            public long bytes;
            public Audio audio;
        }
        [Serializable] internal sealed class Replacement
        {
            public string sourceName, path, guid, sha256;
            public long bytes;
            public Audio audio;
        }
        [Serializable] internal sealed class Revision
        {
            public int schemaVersion, issue;
            public string ownerDecisionUrl;
            public ReceiptReference legacyReceipt;
            public Original original;
            public Replacement replacement;
            public bool rawCopy, transformed;
        }
        [Serializable] private sealed class Receipt { public int issue; public ReceiptEntry[] assets; public Audio audio; }
        [Serializable] private sealed class ReceiptEntry { public string path, role, entry, sha256; public long bytes; }

        internal static Revision LoadOptional(string projectRoot)
        {
            string revisionPath = Path.Combine(projectRoot, RevisionPath);
            if (!File.Exists(revisionPath)) return null;
            return Validate(File.ReadAllText(revisionPath), File.ReadAllBytes(Path.Combine(projectRoot, ReceiptPath)));
        }

        internal static Revision LoadRequired(string projectRoot)
        {
            Revision revision = LoadOptional(projectRoot);
            Require(revision != null, "The approved Issue #85 audio revision must be present.");
            return revision;
        }

        internal static Revision Validate(string json, byte[] receiptBytes)
        {
            Require(receiptBytes != null && Hash(receiptBytes) == ReceiptHash, "The original 68-file receipt changed.");
            Revision revision;
            Receipt receipt;
            try
            {
                revision = JsonUtility.FromJson<Revision>(json);
                receipt = JsonUtility.FromJson<Receipt>(Encoding.UTF8.GetString(receiptBytes));
            }
            catch (ArgumentException error) { throw new InvalidDataException("Invalid audio revision JSON.", error); }
            Require(revision != null && revision.schemaVersion == 1 && revision.issue == 85, "Only the explicit Issue #85 revision is approved.");
            Require(revision.ownerDecisionUrl == OwnerDecision, "The approved Owner decision identity changed.");
            Require(revision.legacyReceipt != null && revision.legacyReceipt.path == ReceiptPath &&
                revision.legacyReceipt.sha256 == ReceiptHash, "The revision is not bound to the frozen legacy receipt.");
            Require(Regex.Matches(json, @"(?<!\\)""rawCopy""\s*:").Count == 1 &&
                Regex.Matches(json, @"(?<!\\)""transformed""\s*:").Count == 1 &&
                revision.rawCopy && !revision.transformed, "The revision must explicitly preserve raw, untransformed input bytes.");
            Require(receipt != null && receipt.issue == 70 && receipt.assets != null && receipt.assets.Length == 68, "The legacy intake contract changed.");
            var music = receipt.assets.Where(e => e != null && (e.role == "Music" || e.path == MusicPath)).ToArray();
            Require(music.Length == 1 && music[0].role == "Music" && music[0].path == MusicPath &&
                music[0].entry == "flounder1.wav" && music[0].bytes == OriginalBytes && music[0].sha256 == OriginalHash,
                "Only the receipt's exact single Music entry may be superseded.");
            Require(revision.original != null && revision.original.path == MusicPath &&
                revision.original.guid == MusicGuid && revision.original.inputName == "flounder1.wav" &&
                revision.original.bytes == OriginalBytes && revision.original.sha256 == OriginalHash,
                "The original audio identity does not match the receipt.");
            RequireAudio(receipt.audio, 7121280, 148.36, "legacy receipt");
            RequireAudio(revision.original.audio, 7121280, 148.36, "original identity");
            Require(revision.replacement != null && revision.replacement.sourceName == "flounder.wav" &&
                revision.replacement.path == MusicPath && revision.replacement.guid == MusicGuid &&
                revision.replacement.bytes == CurrentBytes && revision.replacement.sha256 == CurrentHash,
                "The current identity is not the exact Owner-supplied replacement.");
            RequireAudio(revision.replacement.audio, 11084160, 230.92, "approved current identity");
            return revision;
        }

        private static void RequireAudio(Audio audio, int frames, double duration, string context) =>
            Require(audio != null && audio.encoding == "PCM" && audio.channels == 2 && audio.sampleRate == 48000 &&
                audio.bitsPerSample == 16 && audio.frames == frames &&
                !double.IsNaN(audio.durationSeconds) && !double.IsInfinity(audio.durationSeconds) &&
                Math.Abs(audio.durationSeconds - duration) < .0000001, "Audio metadata mismatch: " + context);

        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        internal static string FileHash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }

    public sealed class Stage03AudioRevisionEditModeTests
    {
        private static string Root => Directory.GetParent(Application.dataPath).FullName;

        [Test]
        public void ApprovedRevision_BindsFrozenReceiptAndExactRawReplacement()
        {
            var revision = Stage03AudioRevisionContract.LoadRequired(Root);
            string music = Path.Combine(Root, Stage03AudioRevisionContract.MusicPath);
            Assert.That(new FileInfo(music).Length, Is.EqualTo(Stage03AudioRevisionContract.CurrentBytes));
            Assert.That(Stage03AudioRevisionContract.FileHash(music), Is.EqualTo(Stage03AudioRevisionContract.CurrentHash));
            Assert.That(revision.replacement.path, Is.EqualTo(revision.original.path));
            Assert.That(revision.replacement.guid, Is.EqualTo(revision.original.guid));
            Assert.That(Stage03AudioRevisionContract.FileHash(Path.Combine(Root, Stage03AudioRevisionContract.ReceiptPath)),
                Is.EqualTo(Stage03AudioRevisionContract.ReceiptHash));
        }

        [Test]
        public void ExistingImporterMetaAndGuid_ArePreservedByteForByte()
        {
            string path = Stage03AudioRevisionContract.MusicPath;
            string meta = Path.Combine(Root, path + ".meta");
            Assert.That(new FileInfo(meta).Length, Is.EqualTo(485L));
            Assert.That(Stage03AudioRevisionContract.FileHash(meta), Is.EqualTo(Stage03AudioRevisionContract.MetaHash));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(Stage03AudioRevisionContract.MusicGuid));
            Assert.That(AssetDatabase.GUIDToAssetPath(Stage03AudioRevisionContract.MusicGuid), Is.EqualTo(path));
            Assert.That(AssetImporter.GetAtPath(path), Is.InstanceOf<AudioImporter>());
        }

        [Test]
        public void ActualWavHeader_RetainsFullPcmDataAndUnknownMetadataChunks()
        {
            Stage03AudioRevisionContract.LoadRequired(Root);
            string path = Path.Combine(Root, Stage03AudioRevisionContract.MusicPath);
            using (var input = File.OpenRead(path))
            using (var reader = new BinaryReader(input, Encoding.ASCII))
            {
                Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("RIFF"));
                Assert.That((long)reader.ReadUInt32() + 8L, Is.EqualTo(input.Length));
                Assert.That(Encoding.ASCII.GetString(reader.ReadBytes(4)), Is.EqualTo("WAVE"));
                int formatChunks = 0, dataChunks = 0;
                long dataBytes = 0;
                bool sawList = false, sawC2pa = false;
                while (input.Position + 8 <= input.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint size = reader.ReadUInt32();
                    long end = input.Position + size;
                    Assert.That(end, Is.LessThanOrEqualTo(input.Length), id);
                    if (id == "fmt ")
                    {
                        formatChunks++;
                        Assert.That(size, Is.GreaterThanOrEqualTo(16));
                        Assert.That(reader.ReadUInt16(), Is.EqualTo(1), "PCM format tag");
                        Assert.That(reader.ReadUInt16(), Is.EqualTo(2));
                        Assert.That(reader.ReadUInt32(), Is.EqualTo(48000));
                        Assert.That(reader.ReadUInt32(), Is.EqualTo(192000));
                        Assert.That(reader.ReadUInt16(), Is.EqualTo(4));
                        Assert.That(reader.ReadUInt16(), Is.EqualTo(16));
                    }
                    if (id == "data") { dataChunks++; dataBytes += size; }
                    if (id == "LIST") sawList = true;
                    if (id == "C2PA") sawC2pa = true;
                    input.Position = end + (size & 1);
                }
                Assert.That(formatChunks, Is.EqualTo(1));
                Assert.That(dataChunks, Is.EqualTo(1));
                Assert.That(dataBytes, Is.EqualTo(44336640L));
                Assert.That(dataBytes / 4, Is.EqualTo(11084160L));
                Assert.That(sawList && sawC2pa, Is.True, "The supplied ancillary metadata was not stripped.");
                Assert.That(input.Position, Is.EqualTo(input.Length));
            }
        }

        [TestCase("receiptHash")]
        [TestCase("originalIdentity")]
        [TestCase("replacementPath")]
        [TestCase("replacementGuid")]
        [TestCase("replacementHash")]
        [TestCase("issue")]
        [TestCase("sourceName")]
        [TestCase("transformed")]
        [TestCase("missingCopyDeclaration")]
        [TestCase("audioMetadata")]
        [TestCase("ownerDecision")]
        public void UnapprovedOrBrokenRevisionChain_FailsClosed(string defect)
        {
            var revision = Stage03AudioRevisionContract.LoadRequired(Root);
            switch (defect)
            {
                case "receiptHash": revision.legacyReceipt.sha256 = new string('0', 64); break;
                case "originalIdentity": revision.original.sha256 = revision.replacement.sha256; break;
                case "replacementPath": revision.replacement.path = "Assets/Unapproved.wav"; break;
                case "replacementGuid": revision.replacement.guid = new string('0', 32); break;
                case "replacementHash": revision.replacement.sha256 = revision.original.sha256; break;
                case "issue": revision.issue = 86; break;
                case "sourceName": revision.replacement.sourceName = "guessed.wav"; break;
                case "transformed": revision.transformed = true; break;
                case "missingCopyDeclaration": revision.rawCopy = false; break;
                case "audioMetadata": revision.replacement.audio.frames = revision.original.audio.frames; break;
                case "ownerDecision": revision.ownerDecisionUrl = "https://example.com/unapproved"; break;
                default: Assert.Fail("Unknown negative fixture."); break;
            }
            string json = JsonUtility.ToJson(revision, true);
            byte[] receipt = File.ReadAllBytes(Path.Combine(Root, Stage03AudioRevisionContract.ReceiptPath));
            Assert.Throws<InvalidDataException>(() => Stage03AudioRevisionContract.Validate(json, receipt), defect);
        }

        [Test]
        public void ChangedLegacyReceiptBytes_CannotAuthorizeAnOverride()
        {
            string json = File.ReadAllText(Path.Combine(Root, Stage03AudioRevisionContract.RevisionPath));
            byte[] receipt = File.ReadAllBytes(Path.Combine(Root, Stage03AudioRevisionContract.ReceiptPath));
            receipt[0] ^= 1;
            Assert.Throws<InvalidDataException>(() => Stage03AudioRevisionContract.Validate(json, receipt));
        }
    }
}
