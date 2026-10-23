using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PhotoBuild
{
    [MenuItem("Wakayama AR/Build Android Development APK")]
    public static void BuildAndroid()
    {
        PhotoProjectValidation.ValidateOrThrow();
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            throw new BuildFailedException("Install Android Build Support for Unity 6000.2.10f1.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new BuildFailedException("Select Android first, or use Tools/develop.ps1 -Task BuildAndroid.");

        string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string allowed = Path.GetFullPath(Path.Combine(project, "..", "Artifacts", "Builds"));
        string output = Path.Combine(allowed, "Android", "WakayamaAR.apk");
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-buildOutput") output = Path.GetFullPath(args[i + 1]);
        if (!output.StartsWith(allowed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("APK output must stay inside repository Artifacts/Builds.");
        Directory.CreateDirectory(Path.GetDirectoryName(output));

        bool wasBundle = EditorUserBuildSettings.buildAppBundle;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { PhotoProjectValidation.ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException("Android build failed: " + report.summary.result);
            Debug.Log("Wakayama AR APK: " + output + " (device behavior unverified)");
        }
        finally { EditorUserBuildSettings.buildAppBundle = wasBundle; }
    }
}
