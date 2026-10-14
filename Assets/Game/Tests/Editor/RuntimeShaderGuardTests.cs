using System.Linq;
using Game.Editor;
using NUnit.Framework;

namespace Gameplay.Tests
{
    /// <summary>
    /// TASK-554: the project-level check — every shader that runtime code builds a material from is guaranteed in the
    /// player (<see cref="RuntimeShaderGuard"/>; the same check fails the player build). The scanner tests prove the
    /// guard actually sees a call, so the green project check is not green by a blind regex.
    /// </summary>
    [TestFixture]
    public sealed class RuntimeShaderGuardTests
    {
        [Test]
        public void Project_RuntimeShadersGuaranteedInPlayer()
        {
            var problems = RuntimeShaderGuard.Validate();
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Scanner_FindsCall_WithLineNumber()
        {
            const string source = "class A {\n  void M() {\n    var s = Shader.Find(\"Game/Wood Block\");\n  }\n}";
            CollectionAssert.AreEqual(new[] { 3 }, RuntimeShaderGuard.FindCallLines(source).ToArray());
        }

        [Test]
        public void Scanner_FindsCall_InFallbackChainContinuation()
        {
            const string source = "var s = X\n    ?? Shader . Find(\"Sprites/Default\");";
            CollectionAssert.AreEqual(new[] { 2 }, RuntimeShaderGuard.FindCallLines(source).ToArray());
        }

        [Test]
        public void Scanner_IgnoresLineComments()
        {
            const string source = "// Shader.Find(\"x\") is banned\n/// <c>Shader.Find(</c>\nvar a = 1;";
            Assert.IsEmpty(RuntimeShaderGuard.FindCallLines(source).ToArray());
        }
    }
}
