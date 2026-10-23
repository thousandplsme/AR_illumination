using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PhotoProjectValidation : IPreprocessBuildWithReport
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => ValidateOrThrow();

    [MenuItem("Wakayama AR/Validate Project")]
    public static void ValidateOrThrow()
    {
        List<string> errors = Validate();
        if (errors.Count > 0) throw new BuildFailedException(string.Join("\n", errors));
        Debug.Log("Wakayama AR: project validation passed (not a device verification).");
    }

    public static List<string> Validate()
    {
        var errors = new List<string>();
        if (!EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).SequenceEqual(new[] { ScenePath }))
            errors.Add("Enabled build scene must be " + ScenePath);

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (!opened && scene.isDirty)
        {
            errors.Add("Save or revert the production Scene before validation/build.");
            return errors;
        }
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (Transform transform in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    errors.Add("Missing script: " + transform.name);

            ScreenshotManager[] managers = roots.SelectMany(r => r.GetComponentsInChildren<ScreenshotManager>(true)).ToArray();
            if (managers.Length != 1)
            {
                errors.Add("Expected exactly one ScreenshotManager.");
                return errors;
            }
            ScreenshotManager manager = managers[0];
            if (!manager.titlePanel || !manager.photoPanel || !manager.resultPanel || !manager.previewImage)
                errors.Add("ScreenshotManager has missing required UI references.");
            CameraMaskController masks = manager.photoPanel ? manager.photoPanel.GetComponent<CameraMaskController>() : null;
            if (!masks || !masks.photoTopMask || !masks.photoBottomMask)
                errors.Add("PhotoPanel needs CameraMaskController and both masks.");
            if (masks && (float.IsNaN(masks.topMargin) || float.IsInfinity(masks.topMargin) || masks.topMargin < 0))
                errors.Add("Invalid camera top margin.");
            if (manager.previewImage)
            {
                var fitter = manager.previewImage.GetComponent<UnityEngine.UI.AspectRatioFitter>();
                if (!fitter || !Mathf.Approximately(fitter.aspectRatio, PhotoCaptureGeometry.AspectRatio))
                    errors.Add("Preview aspect ratio must match capture (3:4).");
            }
            var expected = new HashSet<string> { "OnClickStart", "OnClickShutter", "OnClickRetake", "OnClickSave" };
            var counts = expected.ToDictionary(name => name, name => 0);
            foreach (var button in roots.SelectMany(r => r.GetComponentsInChildren<UnityEngine.UI.Button>(true)))
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); ++i)
                {
                    string method = button.onClick.GetPersistentMethodName(i);
                    if (!expected.Contains(method)) continue;
                    if (button.onClick.GetPersistentTarget(i) != manager)
                        errors.Add(button.name + ": wrong target for " + method);
                    else counts[method]++;
                }
            foreach (var item in counts)
                if (item.Value != 1) errors.Add(item.Key + ": expected one button connection.");
        }
        finally
        {
            if (opened)
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        return errors;
    }
}
