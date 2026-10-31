using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace SashimiBoy.EditorTools
{
    // A successful compilation cannot prove that Git LFS payloads were checked out.
    // Stop authoring/Player packaging before placeholder assets become a playable build.
    public sealed class Stage03BuildInputGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder=>-1000;
        public void OnPreprocessBuild(BuildReport report)=>ValidateMaterializedAssets();
        public static void ValidateMaterializedAssets()
        {
            var pointers=Directory.EnumerateFiles(Application.dataPath,"*",SearchOption.AllDirectories)
                .Where(path=>new[]{".fbx",".wav",".mp3",".asset"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))
                .Where(IsLfsPointer).Take(8).ToArray();
            if(pointers.Length>0)throw new BuildFailedException("Git LFS payloads are missing; materialize and verify the local assets before building: "+string.Join(", ",pointers));
        }
        public static bool IsLfsPointer(string path)
        {
            var bytes=new byte[160];
            using(var input=File.OpenRead(path))
            {
                int count=input.Read(bytes,0,bytes.Length);
                return Encoding.ASCII.GetString(bytes,0,count).StartsWith("version https://git-lfs.github.com/spec/v1",StringComparison.Ordinal);
            }
        }
    }
}
