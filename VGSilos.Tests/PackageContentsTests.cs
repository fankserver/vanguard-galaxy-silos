using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace VGSilos.Tests;

/// <summary>
/// Release-blocker guards for the shipped package (issue #5): game 0.8.1's
/// Managed/ has no Newtonsoft.Json.dll, so VGSilos must ship its own — and
/// with CopyLocalLockFileAssemblies on, it must ship ONLY its own. The
/// deploy/archive file set is asserted exactly: {VGSilos.dll,
/// Newtonsoft.Json.dll}; a leaked compile-only dep (BepInEx, HarmonyX,
/// UnityEngine, game assemblies) fails here before `make deploy` or the
/// release workflow can carry it.
/// </summary>
public sealed class PackageContentsTests
{
    private static readonly string[] ExpectedDlls = { "Newtonsoft.Json.dll", "VGSilos.dll" };

    /// <summary>The plugin's own build output (same config as this test run).</summary>
    private static string PluginBuildDirectory()
    {
        // Test bin layout: <repo>/VGSilos.Tests/bin/<Config>/net8.0[/...].
        // Walk up from the running assembly until we sit directly under a
        // "bin" directory; that segment is the build configuration.
        string? config = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (dir.Parent?.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) == true)
            {
                config = dir.Name;
                break;
            }
        }
        var testProjDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (testProjDir.Name != "VGSilos.Tests")
        {
            var parent = testProjDir.Parent
                ?? throw new DirectoryNotFoundException(
                    "Could not locate the VGSilos repo root from " + AppContext.BaseDirectory);
            testProjDir = parent;
        }
        var repoRoot = testProjDir.Parent!.FullName;
        var candidate = Path.Combine(repoRoot, "VGSilos", "bin", config ?? "Debug", "netstandard2.1");
        if (!Directory.Exists(candidate))
            candidate = Path.Combine(repoRoot, "VGSilos", "bin", "Debug", "netstandard2.1");
        Assert.True(Directory.Exists(candidate),
            "Plugin build output not found at " + candidate + " — run `make build` (or build the solution) before the package tests.");
        return candidate;
    }

    [Fact]
    public void PluginOutputShipsItsOwnNewtonsoft()
    {
        var dir = PluginBuildDirectory();
        Assert.True(File.Exists(Path.Combine(dir, "Newtonsoft.Json.dll")),
            dir + " must contain Newtonsoft.Json.dll — the current game no longer provides it and Silos may not rely on another mod's copy.");
        Assert.True(File.Exists(Path.Combine(dir, "VGSilos.dll")));
    }

    [Fact]
    public void PluginOutputDllSetIsExact_NoCompileOnlyDepLeaks()
    {
        var dir = PluginBuildDirectory();
        var dlls = Directory.GetFiles(dir, "*.dll").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(ExpectedDlls, dlls);
    }

    [Fact]
    public void PluginAssemblyReferencesItsOwnNewtonsoftAndGameSideAssembliesStayCompileOnly()
    {
        var dir = PluginBuildDirectory();
        var name = Assembly.LoadFrom(Path.Combine(dir, "VGSilos.dll"));
        var refs = name.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Contains("Newtonsoft.Json", refs);
        // Compile-time-only references are fine to *reference*; the exact
        // file-set test above proves they are never copied into the output.
        Assert.Contains("0Harmony", refs);
        Assert.Contains("BepInEx", refs);
    }

    [Fact]
    public void DeployedFileSetMatchesExpectedArchiveMembers()
    {
        // What `make deploy` and the release workflow stage: every *.dll in
        // plugin output must be in the expected set, and the only other
        // artifacts are debugging/metadata sidecars.
        var dir = PluginBuildDirectory();
        var extras = Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => !ExpectedDlls.Contains(n, StringComparer.Ordinal)
                        && !n!.EndsWith(".pdb", StringComparison.Ordinal)
                        && !n.EndsWith(".deps.json", StringComparison.Ordinal)
                        && !n.EndsWith(".dll.config", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(extras);
    }
}
