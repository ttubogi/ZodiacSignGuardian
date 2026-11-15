using System;
using System.IO;

namespace ZodiacSignGuardian.Structure.Tests
{
    internal static class RepoPaths
    {
        /// <summary>테스트 실행 폴더에서 위로 올라가며 AGENTS.md와 Assets가 함께 있는 폴더를 찾는다.</summary>
        internal static string Root
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))
                        && Directory.Exists(Path.Combine(dir.FullName, "Assets")))
                    {
                        return dir.FullName;
                    }
                    dir = dir.Parent;
                }
                throw new InvalidOperationException("저장소 루트(AGENTS.md + Assets)를 찾지 못했습니다: " + AppContext.BaseDirectory);
            }
        }

        /// <summary>저장소 기준 상대 경로를 '/'로 통일해 돌려준다.</summary>
        internal static string Relative(string fullPath)
        {
            return Path.GetRelativePath(Root, fullPath).Replace('\\', '/');
        }
    }
}
