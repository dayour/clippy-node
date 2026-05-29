namespace OpenClaw.Tray.Tests;

public sealed class ReleaseSigningWorkflowTests
{
    [Fact]
    public void ReleaseWorkflow_SignsOnlyOpenClawOwnedPayloadExecutables()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "ci.yml"));

        Assert.Contains("Stage OpenClaw Executables for Signing", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input\OpenClaw.Tray.WinUI.exe -Target publish\OpenClaw.Tray.WinUI.exe", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input\OpenClaw.SetupEngine.exe -Target publish\SetupEngine\OpenClaw.SetupEngine.exe", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input\OpenClaw.SetupEngine.UI.exe -Target publish\SetupEngine\OpenClaw.SetupEngine.UI.exe", workflow);
        Assert.Contains("Sign OpenClaw Executables", workflow);
        Assert.Contains("files-folder: signing-input", workflow);
        Assert.Contains("Stage ARM64 OpenClaw Executables for Signing", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input-arm64\OpenClaw.Tray.WinUI.exe -Target artifacts\tray-win-arm64\OpenClaw.Tray.WinUI.exe", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input-arm64\OpenClaw.SetupEngine.exe -Target artifacts\tray-win-arm64\SetupEngine\OpenClaw.SetupEngine.exe", workflow);
        Assert.Contains(@"New-Item -ItemType HardLink -Path signing-input-arm64\OpenClaw.SetupEngine.UI.exe -Target artifacts\tray-win-arm64\SetupEngine\OpenClaw.SetupEngine.UI.exe", workflow);
        Assert.Contains("Sign ARM64 OpenClaw Executables", workflow);
        Assert.Contains("files-folder: signing-input-arm64", workflow);
        Assert.Contains("files-folder-filter: exe", workflow);
        Assert.DoesNotContain("files-folder-recurse: true", workflow);
    }

    [Fact]
    public void ReleaseWorkflow_VerifiesExecutableSigningPolicy()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "ci.yml"));
        var verifier = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "scripts", "Test-ReleaseExecutableSignatures.ps1"));

        Assert.Contains("Test-ReleaseExecutableSignatures.ps1 -PayloadPath publish -RequireSignedOpenClaw", workflow);
        Assert.Contains("Test-ReleaseExecutableSignatures.ps1 -PayloadPath artifacts/tray-win-arm64 -RequireSignedOpenClaw", workflow);
        Assert.Contains(@"^OpenClaw\.Tray\.WinUI\.exe$", verifier);
        Assert.Contains(@"^SetupEngine\\OpenClaw\.SetupEngine\.exe$", verifier);
        Assert.Contains(@"^SetupEngine\\OpenClaw\.SetupEngine\.UI\.exe$", verifier);
        Assert.Contains(@"(^|\\)createdump\.exe$", verifier);
        Assert.Contains(@"(^|\\)RestartAgent\.exe$", verifier);
        Assert.Contains(@"^tools\\mxc\\[^\\]+\\wxc-exec\.exe$", verifier);
        Assert.Contains("Unknown executable in release payload", verifier);
    }

    [Fact]
    public void ReleaseWorkflow_PausesMsixForAlpha()
    {
        var workflow = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".github", "workflows", "ci.yml"));

        Assert.Contains("if: false # Paused for alpha.4; ship Inno setup and portable ZIP artifacts only.", workflow);
        Assert.Contains("needs: [repo-hygiene, test, e2etests, build]", workflow);
        Assert.DoesNotContain("Download win-x64 MSIX artifact", workflow);
        Assert.DoesNotContain("Download win-arm64 MSIX artifact", workflow);
        Assert.DoesNotContain("Sign Release MSIX Packages", workflow);
        Assert.DoesNotContain(".msix", ExtractReleaseStep(workflow));
    }

    private static string ExtractReleaseStep(string workflow)
    {
        var start = workflow.IndexOf("    - name: Create Release", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find Create Release step.");
        return workflow[start..];
    }

    private static string GetRepositoryRoot()
    {
        var env = Environment.GetEnvironmentVariable("OPENCLAW_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "openclaw-windows-node.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
