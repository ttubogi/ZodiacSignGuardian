using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ZodiacSignGuardian.TableBuild
{
    /// <summary>
    /// Unity 밖에서 Assets 안에 파일을 만들 때 필요한 .meta를 만든다.
    /// 이미 있는 .meta는 절대 바꾸지 않는다 (GUID가 바뀌면 Unity 안의 참조가 끊긴다).
    /// 내용은 Unity 6.3이 만든 .meta와 같은 모양이다.
    /// </summary>
    public static class UnityMeta
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static string FolderMeta(string guid) =>
            "fileFormatVersion: 2\n" +
            "guid: " + guid + "\n" +
            "folderAsset: yes\n" +
            "DefaultImporter:\n" +
            "  externalObjects: {}\n" +
            "  userData: \n" +
            "  assetBundleName: \n" +
            "  assetBundleVariant: \n";

        public static string TextAssetMeta(string guid) =>
            "fileFormatVersion: 2\n" +
            "guid: " + guid + "\n" +
            "TextScriptImporter:\n" +
            "  externalObjects: {}\n" +
            "  userData: \n" +
            "  assetBundleName: \n" +
            "  assetBundleVariant: \n";

        static string NewGuid() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// outputDir가 repoRoot/Assets 안이면, Assets 바로 아래부터 outputDir까지의 폴더와 files에 .meta가 없을 때만 만든다.
        /// 만든 .meta 경로 목록을 돌려준다.
        /// </summary>
        public static List<string> EnsureMetas(string repoRoot, string outputDir, IEnumerable<string> files)
        {
            var created = new List<string>();
            var assets = Path.GetFullPath(Path.Combine(repoRoot, "Assets"));
            var dir = Path.GetFullPath(outputDir);
            if (!dir.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return created;

            var folders = new List<string>();
            for (var d = dir; d.Length > assets.Length; d = Path.GetDirectoryName(d)!) folders.Insert(0, d);

            foreach (var folder in folders)
            {
                if (WriteIfMissing(folder + ".meta", FolderMeta(NewGuid()))) created.Add(folder + ".meta");
            }
            foreach (var file in files)
            {
                var full = Path.GetFullPath(file);
                if (WriteIfMissing(full + ".meta", TextAssetMeta(NewGuid()))) created.Add(full + ".meta");
            }
            return created;
        }

        static bool WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return false;
            File.WriteAllText(path, content, Utf8);
            return true;
        }
    }
}
