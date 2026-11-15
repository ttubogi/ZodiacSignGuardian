using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ZodiacSignGuardian.Data;

namespace ZodiacSignGuardian.Core.Tests.Architecture
{
    /// <summary>
    /// 실제로 컴파일된 Core·Data 어셈블리의 참조를 본다.
    /// Unity Test Runner와 dotnet test 양쪽에서 같은 결과가 나와야 한다.
    /// </summary>
    [TestFixture]
    public class AssemblyReferenceTests
    {
        static string[] ReferencedNames(Assembly assembly)
        {
            return assembly.GetReferencedAssemblies().Select(n => n.Name).ToArray();
        }

        [Test]
        public void Data는_Unity와_다른_프로젝트_어셈블리를_참조하지_않는다()
        {
            var assembly = typeof(DataAssembly).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo(ReferenceRules.Data));
            Assert.That(ReferenceRules.Check(ReferenceRules.Data, ReferencedNames(assembly)), Is.Empty);
        }

        [Test]
        public void Core는_Data만_참조하고_Unity를_참조하지_않는다()
        {
            var assembly = typeof(CoreAssembly).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo(ReferenceRules.Core));
            Assert.That(ReferenceRules.Check(ReferenceRules.Core, ReferencedNames(assembly)), Is.Empty);

            var projectRefs = ReferencedNames(assembly)
                .Where(n => n.StartsWith(ReferenceRules.ProjectPrefix))
                .ToArray();
            Assert.That(projectRefs, Is.EqualTo(new[] { ReferenceRules.Data }));
        }

        // ---- 위반 주입: 검사가 틀린 참조를 실제로 잡는지 ----

        [Test]
        public void 위반주입_Core가_UnityEngine을_참조하면_잡는다()
        {
            var v = ReferenceRules.Check(ReferenceRules.Core,
                new[] { "netstandard", ReferenceRules.Data, "UnityEngine.CoreModule" });
            Assert.That(v.Count, Is.EqualTo(1));
            Assert.That(v[0], Does.Contain("UnityEngine.CoreModule"));
        }

        [Test]
        public void 위반주입_Core가_화면이나_서비스를_참조하면_잡는다()
        {
            var v = ReferenceRules.Check(ReferenceRules.Core, new[]
            {
                ReferenceRules.Data, "ZodiacSignGuardian.View", "ZodiacSignGuardian.Services",
            });
            Assert.That(v.Count, Is.EqualTo(2));
        }

        [Test]
        public void 위반주입_Data가_Core를_참조하면_잡는다()
        {
            var v = ReferenceRules.Check(ReferenceRules.Data, new[] { "netstandard", ReferenceRules.Core });
            Assert.That(v.Count, Is.EqualTo(1));
            Assert.That(v[0], Does.Contain(ReferenceRules.Core));
        }

        [Test]
        public void 위반주입_Unity_패키지_어셈블리도_잡는다()
        {
            var v = ReferenceRules.Check(ReferenceRules.Data, new[] { "Unity.Mathematics", "UnityEditor" });
            Assert.That(v.Count, Is.EqualTo(2));
        }

        [Test]
        public void 위반주입_규칙이_없는_어셈블리는_통과시키지_않는다()
        {
            var v = ReferenceRules.Check("ZodiacSignGuardian.Unknown", new string[0]);
            Assert.That(v.Count, Is.EqualTo(1));
        }

        [Test]
        public void 표준_라이브러리_참조는_위반이_아니다()
        {
            // 검사가 너무 넓어서 정상 참조까지 잡지 않는지
            var v = ReferenceRules.Check(ReferenceRules.Data, new[] { "netstandard", "System.Runtime", "mscorlib" });
            Assert.That(v, Is.Empty);
        }
    }
}
