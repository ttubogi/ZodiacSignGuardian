using System.Collections.Generic;
using System.Linq;

namespace ZodiacSignGuardian.Core.Tests.Architecture
{
    /// <summary>
    /// 컴파일된 어셈블리의 참조 목록이 설계서 4절의 방향을 지키는지 판정한다.
    /// 입력은 이름 목록뿐이라, 테스트에서 일부러 틀린 목록을 넣어 검사가 위반을 잡는지 확인할 수 있다.
    /// </summary>
    internal static class ReferenceRules
    {
        internal const string ProjectPrefix = "ZodiacSignGuardian.";
        internal const string Core = "ZodiacSignGuardian.Core";
        internal const string Data = "ZodiacSignGuardian.Data";

        /// <summary>판정 쪽(Core·Data)이 참조해도 되는 우리 어셈블리.</summary>
        static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            { Data, new string[0] },
            { Core, new[] { Data } },
        };

        internal static List<string> Check(string assemblyName, IEnumerable<string> referencedNames)
        {
            var violations = new List<string>();
            var refs = referencedNames.ToList();

            if (!Allowed.TryGetValue(assemblyName, out var allowed))
            {
                violations.Add($"{assemblyName}: 참조 규칙이 정해지지 않은 어셈블리");
                return violations;
            }

            foreach (var name in refs.Where(IsUnity))
            {
                violations.Add($"{assemblyName}: Unity 어셈블리 {name}를 참조함 (판정은 Unity를 모른다)");
            }

            foreach (var name in refs.Where(n => n.StartsWith(ProjectPrefix)))
            {
                if (!allowed.Contains(name))
                {
                    violations.Add($"{assemblyName}: 허용되지 않은 참조 {name}");
                }
            }

            return violations;
        }

        static bool IsUnity(string name)
        {
            return name == "UnityEngine" || name == "UnityEditor"
                || name.StartsWith("UnityEngine.") || name.StartsWith("UnityEditor.")
                || name.StartsWith("Unity.");
        }
    }
}
