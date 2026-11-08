using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ToolkitWebGLBuilder
{
    private const string ViewerScene = "Assets/Scenes/SampleScene.unity";
    private const string EditorScene = "Assets/Scenes/ToolkitEditor.unity";

    [MenuItem("Toolkit Viewer/Build WebGL")]
    public static void BuildViewerWebGL()
    {
        BuildWebGL(
            "Toolkit Viewer",
            ViewerScene,
            "Builds/WebGL",
            "PROJECT:ToolkitMinimal"
        );
    }

    [MenuItem("Toolkit Viewer/Build Editor WebGL")]
    public static void BuildEditorWebGL()
    {
        BuildWebGL(
            "Toolkit Web Editor",
            EditorScene,
            "Builds/EditorWebGL",
            "PROJECT:ToolkitEditor"
        );
    }

    private static void BuildWebGL(
        string productName,
        string scenePath,
        string buildFolder,
        string webGlTemplate
    )
    {
        if (!File.Exists(scenePath))
        {
            Debug.LogError(productName + " build stopped because its scene is missing: " + scenePath);
            return;
        }

        // Start with uncompressed output so a basic static server can host the
        // first build without special gzip or Brotli response headers.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        // Use the project's minimal page instead of Unity's branded desktop frame.
        PlayerSettings.WebGL.template = webGlTemplate;

        Directory.CreateDirectory(buildFolder);

        BuildPlayerOptions buildOptions = new BuildPlayerOptions
        {
            scenes = new[] { scenePath },
            locationPathName = buildFolder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        Debug.Log("Building " + productName + " to: " + Path.GetFullPath(buildFolder));
        BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log(
                productName + " build succeeded. Size: " +
                report.summary.totalSize + " bytes."
            );
            EditorUtility.RevealInFinder(Path.GetFullPath(buildFolder));
        }
        else
        {
            Debug.LogError(productName + " build failed: " + report.summary.result);
        }
    }
}
