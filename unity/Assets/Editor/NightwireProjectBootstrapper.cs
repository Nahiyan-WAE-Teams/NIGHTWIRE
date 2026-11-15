using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nightwire.Unity;
using Nightwire.Unity.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Nightwire.Unity.Editor
{
    /// <summary>
    /// Creates the initial runtime UI scene and bundled sample archive for a fresh checkout.
    /// The generated scene uses the offline development provider; replace it before production builds.
    /// </summary>
    [InitializeOnLoad]
    internal static class NightwireProjectBootstrapper
    {
        private const string ScenePath = "Assets/Scenes/NightwireMain.unity";
        private const string PanelPath = "Assets/Settings/NightwirePanelSettings.asset";
        private const string LibraryUxmlPath = "Assets/Nightwire/UI/EpisodeLibraryView.uxml";
        private const string MessengerUxmlPath = "Assets/Nightwire/UI/MessengerScreen.uxml";

        static NightwireProjectBootstrapper()
        {
            EditorApplication.delayCall += Bootstrap;
        }

        private static void Bootstrap()
        {
            try
            {
                EnsureFolders();
                EnsureBundledEpisodeArchive();
                EnsurePanelSettings();
                EnsureScene();
                ConfigurePlayerSettings();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Settings");
            Directory.CreateDirectory("Assets/StreamingAssets/Nightwire");
        }

        private static void EnsureBundledEpisodeArchive()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string source = Path.GetFullPath(Path.Combine(projectRoot, "..", "examples", "Episode_001"));
            if (!Directory.Exists(source))
            {
                Debug.LogWarning("NIGHTWIRE sample episode source was not found at " + source);
                return;
            }

            string output = Path.Combine(Application.dataPath, "StreamingAssets", "Nightwire", "Episode_001.zip");
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            if (files.Length == 0) return;
            DateTime newestSource = files.Max(File.GetLastWriteTimeUtc);
            if (File.Exists(output) && File.GetLastWriteTimeUtc(output) >= newestSource)
                return;

            using (var archiveFile = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(archiveFile, ZipArchiveMode.Create, false))
            {
                foreach (string filePath in files)
                {
                    string relative = filePath.Substring(source.Length)
                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                    using (var input = File.OpenRead(filePath))
                    using (var destination = entry.Open())
                        input.CopyTo(destination);
                }
            }

            File.SetLastWriteTimeUtc(output, DateTime.UtcNow);
            AssetDatabase.ImportAsset("Assets/StreamingAssets/Nightwire/Episode_001.zip",
                ImportAssetOptions.ForceUpdate);
        }

        private static PanelSettings EnsurePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel != null) return panel;

            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 0;
            AssetDatabase.CreateAsset(panel, PanelPath);
            AssetDatabase.SaveAssets();
            return panel;
        }

        private static void EnsureScene()
        {
            if (File.Exists(ScenePath))
            {
                EnsureBuildScene();
                return;
            }

            var libraryTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LibraryUxmlPath);
            var messengerTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MessengerUxmlPath);
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (libraryTree == null || messengerTree == null || panel == null)
            {
                Debug.LogError("NIGHTWIRE cannot generate its scene because a UXML document or Panel Settings asset is missing.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var applicationObject = new GameObject("NIGHTWIRE Application");
            var libraryDocument = applicationObject.AddComponent<UIDocument>();
            libraryDocument.panelSettings = panel;
            libraryDocument.visualTreeAsset = libraryTree;
            var libraryView = applicationObject.AddComponent<EpisodeLibraryViewController>();
            var platformServices = applicationObject.AddComponent<NightwireDevelopmentPlatformServices>();
            var episodeSeeder = applicationObject.AddComponent<NightwireBundledEpisodeSeeder>();
            var applicationRoot = applicationObject.AddComponent<NightwireApplicationRoot>();
            applicationObject.AddComponent<NightwireAtmosphereController>();

            var messengerObject = new GameObject("NIGHTWIRE Messenger");
            var messengerDocument = messengerObject.AddComponent<UIDocument>();
            messengerDocument.panelSettings = panel;
            messengerDocument.visualTreeAsset = messengerTree;
            var messengerView = messengerObject.AddComponent<MessengerScreenController>();
            messengerObject.AddComponent<NightwireAtmosphereController>();

            var serialized = new SerializedObject(applicationRoot);
            serialized.FindProperty("libraryView").objectReferenceValue = libraryView;
            serialized.FindProperty("messengerView").objectReferenceValue = messengerView;
            serialized.FindProperty("platformServices").objectReferenceValue = platformServices;
            serialized.FindProperty("bundledEpisodeSeeder").objectReferenceValue = episodeSeeder;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildScene();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureBuildScene()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(x => x.path == ScenePath && x.enabled)) return;
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "WAE";
            PlayerSettings.productName = "NIGHTWIRE";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.defaultScreenWidth = 1080;
            PlayerSettings.defaultScreenHeight = 1920;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.nightwire.game");
        }
    }
}
