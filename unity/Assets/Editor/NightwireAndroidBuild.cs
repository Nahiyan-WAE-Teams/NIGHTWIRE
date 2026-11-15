using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Nightwire.Unity.Editor
{
    /// <summary>
    /// Produces a development APK for installation and device smoke testing.
    /// The development build symbol is required by the offline sample episode provider.
    /// This is not a production or Play Store signing pipeline.
    /// </summary>
    public static class NightwireAndroidBuild
    {
        public static void Build()
        {
            NightwireProjectBootstrapper.PrepareForBuild();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("NIGHTWIRE has no enabled scenes to build.");

            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string outputPath = Path.Combine(repositoryRoot, "build", "Android", "NIGHTWIRE.apk");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.CompressWithLz4
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException(
                    "NIGHTWIRE Android development build failed: " + report.summary.result +
                    " · errors=" + report.summary.totalErrors +
                    " · warnings=" + report.summary.totalWarnings);

            Debug.Log("NIGHTWIRE Android development APK created: " + outputPath +
                " (" + report.summary.totalSize + " bytes)");
        }
    }
}
