using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PocketBump.Editor
{
    public static class PocketBumpBuild
    {
        // Batch entry point. Explicit output is required; existing versions are never overwritten.
        public static void BuildWindowsDevelopment()
        {
            BuildWindows(true);
        }

        public static void BuildWindowsRelease()
        {
            if (PlayerSettings.bundleVersion != PocketBumpReleaseIdentity.CurrentVersion || PlayerSettings.companyName != "Fnega" ||
                PlayerSettings.productName != "PocketBump")
                throw new BuildFailedException("Prepare and review the " + PocketBumpReleaseIdentity.CurrentVersion + " identity before building Release.");
            BuildWindows(false);
        }

        public static void BuildDemoReleaseAndDiagnostics()
        {
            PocketBumpReleaseIdentity.RebuildIcon();
            BuildWindowsRelease();
            BuildWindows(true, "-pocketBumpDiagnosticsOutput");
        }

        static void BuildWindows(bool development, string outputOption = "-pocketBumpOutput")
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, outputOption);
            if (index < 0 || index + 1 >= args.Length)
                throw new BuildFailedException("Missing " + outputOption + " <new-version-folder>.");

            string directory = Path.GetFullPath(args[index + 1]);
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new BuildFailedException("Refusing to overwrite existing build: " + directory);

            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0) throw new BuildFailedException("No enabled build scenes.");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(directory, development ? "PhysicscollGame.exe" : "PocketBump.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Windows " + (development ? "Development" : "Release") + " build failed.");
            if (!development)
                PocketBumpWindowsVersion.StampRelease(Path.Combine(directory, "PocketBump.exe"), Application.unityVersion, PlayerSettings.bundleVersion);
            Debug.Log($"PocketBump build: {report.summary.result}; errors={report.summary.totalErrors}; " +
                      $"warnings={report.summary.totalWarnings}; output={report.summary.outputPath}");
            File.WriteAllText(Path.Combine(directory, "build-info.json"), JsonUtility.ToJson(new BuildIdentity
            {
                game = "射球大战 / PocketBump", developer = PlayerSettings.companyName,
                version = PlayerSettings.bundleVersion, development = development,
                build_guid = report.summary.guid.ToString(), unity_version = Application.unityVersion,
                built_utc = DateTime.UtcNow.ToString("o"),
                platform = "Windows x64", protocol = DashNetcode.Version,
                rules_hash = ArenaRulesProfile.CreateProductionV1().ContentHash,
                physical_two_computer_validated = false
            }, true));
        }

        [Serializable] sealed class BuildIdentity
        {
            public string game, developer, version, build_guid, unity_version, built_utc, platform, rules_hash;
            public bool development, physical_two_computer_validated;
            public int protocol;
        }
    }
}
