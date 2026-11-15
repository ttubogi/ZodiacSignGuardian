using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ZodiacSignGuardian.Core.Tests")]

namespace ZodiacSignGuardian.Core
{
    /// <summary>
    /// 이 어셈블리를 가리키는 표식. 구조 테스트가 참조 방향을 검사할 때 쓴다.
    /// Core는 Data만 참조하고 Unity를 참조하지 않는다.
    /// </summary>
    public static class CoreAssembly
    {
        // Core → Data 참조가 실제 어셈블리 참조로 남도록 Data 타입을 한 번 가리킨다.
        // (C# 컴파일러는 쓰지 않는 참조를 어셈블리 메타데이터에서 빼기 때문)
        public static readonly System.Type DataMarker = typeof(Data.DataAssembly);
    }
}
