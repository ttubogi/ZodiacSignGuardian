using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Structure.Tests
{
    /// <summary>
    /// 프로젝트 지시 1-1: 규칙은 AGENTS.md 한 곳에만, CLAUDE.md에는 @AGENTS.md 한 줄만,
    /// AGENTS.md 맨 위에는 세 원칙.
    /// </summary>
    internal static class RuleFileRules
    {
        internal static readonly string[] TopPrinciples =
        {
            "최신 지시가 이 파일보다 우선",
            "직접 고친 파일은 덮어쓰지 않",
            "자동 검사 통과는 재미·미술 승인이 아니",
        };

        internal static List<string> Check(string claudeMd, string agentsMd)
        {
            var v = new List<string>();

            var claudeLines = Lines(claudeMd).Where(l => l.Trim().Length > 0).ToArray();
            if (!(claudeLines.Length == 1 && claudeLines[0].Trim() == "@AGENTS.md"))
            {
                v.Add("CLAUDE.md는 '@AGENTS.md' 한 줄만 있어야 함");
            }

            // 첫 번째 '## ' 절이 최우선 원칙이고, 세 원칙이 그 절 안에 있어야 한다.
            var lines = Lines(agentsMd);
            var sectionStarts = lines.Select((l, i) => (l, i)).Where(x => x.l.StartsWith("## ")).Select(x => x.i).ToArray();
            if (sectionStarts.Length == 0)
            {
                v.Add("AGENTS.md에 '## ' 절이 없음");
                return v;
            }
            var firstEnd = sectionStarts.Length > 1 ? sectionStarts[1] : lines.Length;
            var firstSection = string.Join("\n", lines.Skip(sectionStarts[0]).Take(firstEnd - sectionStarts[0]));
            foreach (var p in TopPrinciples)
            {
                if (!firstSection.Contains(p))
                {
                    v.Add($"AGENTS.md 첫 절에 '{p}' 원칙이 없음");
                }
            }
            return v;
        }

        static string[] Lines(string text)
        {
            return text.Replace("\r\n", "\n").Split('\n');
        }
    }
}
