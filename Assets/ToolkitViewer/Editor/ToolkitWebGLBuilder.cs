using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ToolkitWebGLBuilder
{
    private const string BuildFolder = "Builds/WebGL";

    [MenuItem("Toolkit Viewer/Build WebGL")]
    public static void BuildWebGL()
    {
        List<string> enabledScenes = new List<string>();

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                enabledScenes.Add(scene.path);
            }
        }

        if (enabledScenes.Count == 0)
        {
            Debug.LogError("WebGL build stopped because no scenes are enabled in Build Profiles.");
            return;
        }

        // Start with uncompressed output so a basic static server can host the
        // first build without special gzip or Brotli response headers.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        // Use the project's minimal page instead of Unity's branded desktop frame.
        PlayerSettings.WebGL.template = "PROJECT:ToolkitMinimal";

        Directory.CreateDirectory(BuildFolder);

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = enabledScenes.ToArray(),
            locationPathName = BuildFolder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        Debug.Log("Building Toolkit Viewer WebGL player to: " + Path.GetFullPath(BuildFolder));
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log(
                "Toolkit Viewer WebGL build succeeded. Size: " +
                report.summary.totalSize + " bytes."
            );
            EditorUtility.RevealInFinder(Path.GetFullPath(BuildFolder));
        }
        else
        {
            Debug.LogError("Toolkit Viewer WebGL build failed: " + report.summary.result);
        }
    }
}
