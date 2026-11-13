using System.Security.Cryptography;
using JPCFIX.Optimizer.Desktop;
using Xunit;

namespace JPCFIX.Optimizer.Tests;

public sealed class RunnerPackageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "JPCFIX-package-tests-" + Guid.NewGuid().ToString("N"));
    private string Runner => Path.Combine(root, "Runner", "JPCFIX.Optimizer.Runner.exe");
    private string Hash => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Runner)));
    public RunnerPackageTests() { Directory.CreateDirectory(Path.GetDirectoryName(Runner)!); File.WriteAllText(Runner, "Synthetic test payload, never executed"); }
    [Fact] public void VerifiedRunnerStaysLockedUntilDisposed()
    {
        using (var held = RunnerPackage.OpenVerified(root, Hash))
        {
            Assert.Equal(Runner, held.Name);
            Assert.Throws<IOException>(() => File.WriteAllText(Runner, "tampered"));
            Assert.Throws<IOException>(() => File.Move(Runner, Runner + ".other"));
        }
        File.WriteAllText(Runner, "writes available after disposal");
    }
    [Fact] public void MismatchedRunnerIsRejectedAndHandleReleased()
    {
        var original = Hash; File.WriteAllText(Runner, "modified");
        Assert.Throws<InvalidOperationException>(() => RunnerPackage.OpenVerified(root, original));
        File.WriteAllText(Runner, "handle released");
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("not-a-hash")]
    public void MissingPublishIdentityCannotElevate(string? hash) => Assert.Throws<InvalidOperationException>(() => RunnerPackage.OpenVerified(root, hash));
    [Fact] public void MissingRunnerIsClearlyReported()
    { var hash = Hash; File.Delete(Runner); Assert.Throws<InvalidOperationException>(() => RunnerPackage.OpenVerified(root, hash)); }
    public void Dispose()
    {
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "JPCFIX-package-tests-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        Directory.Delete(root, true);
    }
}
