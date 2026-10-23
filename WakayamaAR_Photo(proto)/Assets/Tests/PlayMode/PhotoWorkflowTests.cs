using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class PhotoWorkflowTests
{
    private GameObject owner;
    private GameObject canvasObject;
    private ScreenshotManager manager;
    private FakeGallery gallery;

    private sealed class FakeGallery : IPhotoGallery
    {
        public int Requests;
        public Action<bool, string> Completed;
        public bool Throw;
        public void Save(Texture2D texture, Action<bool, string> completed)
        {
            Requests++;
            if (Throw) throw new InvalidOperationException("Simulated save exception");
            Completed = completed;
        }
    }

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        canvasObject = new GameObject("TestCanvas", typeof(Canvas));
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        owner = new GameObject("TestManager");
        manager = owner.AddComponent<ScreenshotManager>();
        manager.titlePanel = Panel("Title");
        manager.photoPanel = Panel("Photo");
        manager.resultPanel = Panel("Result");
        var preview = new GameObject("Preview", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        preview.transform.SetParent(manager.resultPanel.transform, false);
        manager.previewImage = preview.GetComponent<UnityEngine.UI.RawImage>();
        gallery = new FakeGallery();
        manager.Gallery = gallery;
        yield return null;
    }

    private GameObject Panel(string name)
    {
        var panel = new GameObject(name, typeof(RectTransform));
        panel.transform.SetParent(canvasObject.transform, false);
        return panel;
    }

    private Texture2D PreparePhoto()
    {
        manager.OnClickStart();
        var photo = new Texture2D(3, 4);
        manager.AcceptCapture(photo); // Supply a frame; GPU readback is intentionally not mocked as verified.
        return photo;
    }

    private void BeginCaptureWithoutReadingPixels()
    {
        RectTransform panel = (RectTransform)manager.photoPanel.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(150, 300);
        CameraMaskController masks = manager.photoPanel.AddComponent<CameraMaskController>();
        masks.topMargin = 20;
        manager.OnClickStart();
        manager.OnClickShutter();
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Capturing));
        // Disable before the first WaitForEndOfFrame resumes; no GPU capture is claimed.
    }

    [UnityTest]
    public IEnumerator ObjectReactivationRecoversInterruptedCapture()
    {
        BeginCaptureWithoutReadingPixels();
        owner.SetActive(false);
        owner.SetActive(true);
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
        Assert.That(manager.photoPanel.activeSelf, Is.True);
        Assert.That(manager.previewImage.texture, Is.Null);
        yield return null;
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
    }

    [UnityTest]
    public IEnumerator ComponentReactivationRecoversInterruptedCapture()
    {
        BeginCaptureWithoutReadingPixels();
        manager.enabled = false;
        manager.enabled = true;
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
        Assert.That(manager.photoPanel.activeSelf, Is.True);
        yield return null;
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
    }

    [UnityTest]
    public IEnumerator RepeatedRetakeAndSaveReleaseAllFramesWithoutAddingStatusObjects()
    {
        Texture2D warmup = PreparePhoto();
        manager.OnClickSave();
        gallery.Completed(true, "warmup");
        yield return null;
        Assert.That(warmup == null, Is.True);
        int childCount = canvasObject.GetComponentsInChildren<Transform>(true).Length;
        for (int i = 0; i < 100; ++i)
        {
            Texture2D photo = PreparePhoto();
            if (i % 2 == 0) manager.OnClickRetake();
            else
            {
                manager.OnClickSave();
                gallery.Completed(true, "frame");
            }
            yield return null;
            Assert.That(photo == null, Is.True, "Frame " + i + " was not released");
            Assert.That(manager.previewImage.texture, Is.Null);
            Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
            Assert.That(canvasObject.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(childCount));
        }
    }

    [UnityTest]
    public IEnumerator ReactivationKeepsPendingSaveUntilTheCallbackArrives()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        owner.SetActive(false);
        yield return null;
        owner.SetActive(true);
        manager.OnClickSave();
        manager.OnClickRetake();
        Assert.That(gallery.Requests, Is.EqualTo(1));
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Saving));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        LogAssert.Expect(LogType.Warning, new Regex("Save failed"));
        gallery.Completed(false, "Denied");
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Result));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        manager.OnClickSave();
        gallery.Completed(true, "retry");
        yield return null;
        Assert.That(photo == null, Is.True);
    }

    [UnityTest]
    public IEnumerator SaveCallbackWhileInactiveStillReleasesTheFrame()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        owner.SetActive(false);
        gallery.Completed(true, "saved while inactive");
        yield return null;
        owner.SetActive(true);
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
        Assert.That(photo == null, Is.True);
    }

    [UnityTest]
    public IEnumerator DisabledComponentRejectsButtonOperations()
    {
        Texture2D photo = PreparePhoto();
        manager.enabled = false;
        manager.OnClickSave();
        manager.OnClickRetake();
        Assert.That(gallery.Requests, Is.Zero);
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        manager.enabled = true;
        manager.OnClickRetake();
        yield return null;
        Assert.That(photo == null, Is.True);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        UnityEngine.Object.Destroy(owner);
        yield return null;
        UnityEngine.Object.Destroy(canvasObject);
        yield return null;
    }

    [UnityTest]
    public IEnumerator StartAndRetakeReleaseTheImage()
    {
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Title));
        Texture2D photo = PreparePhoto();
        Assert.That(manager.resultPanel.activeSelf, Is.True);
        manager.OnClickRetake();
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
        Assert.That(manager.previewImage.texture, Is.Null);
        yield return null;
        Assert.That(photo == null, Is.True);
    }

    [UnityTest]
    public IEnumerator SaveFailureKeepsImageAndAllowsRetry()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        LogAssert.Expect(LogType.Warning, new Regex("Save failed"));
        gallery.Completed(false, "Denied");
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Result));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        Assert.That(manager.LastStatus, Does.Contain("Save failed"));
        manager.OnClickSave();
        Assert.That(gallery.Requests, Is.EqualTo(2));
        gallery.Completed(true, "device/path.png");
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Photo));
        Assert.That(manager.LastStatus, Is.EqualTo("Photo saved."));
        yield return null;
        Assert.That(photo == null, Is.True);
    }

    [UnityTest]
    public IEnumerator PendingSaveRejectsDuplicateSaveAndRetake()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        manager.OnClickSave();
        manager.OnClickRetake();
        manager.OnClickShutter();
        Assert.That(gallery.Requests, Is.EqualTo(1));
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Saving));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        gallery.Completed(true, "device/path.png");
        yield return null;
        Assert.That(photo == null, Is.True);
    }

    [UnityTest]
    public IEnumerator SaveExceptionKeepsImage()
    {
        Texture2D photo = PreparePhoto();
        gallery.Throw = true;
        LogAssert.Expect(LogType.Warning, new Regex("Save failed"));
        manager.OnClickSave();
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Result));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        yield return null;
    }

    [UnityTest]
    public IEnumerator StaleCallbackCannotCompleteANewSave()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        Action<bool, string> first = gallery.Completed;
        LogAssert.Expect(LogType.Warning, new Regex("Save failed"));
        first(false, "Denied");
        manager.OnClickSave();
        first(true, "late result");
        Assert.That(manager.State, Is.EqualTo(ScreenshotManager.PhotoState.Saving));
        Assert.That(manager.previewImage.texture, Is.SameAs(photo));
        gallery.Completed(true, "new result");
        yield return null;
    }

    [UnityTest]
    public IEnumerator DestroyReleasesPhotoAndIgnoresDelayedCallback()
    {
        Texture2D photo = PreparePhoto();
        manager.OnClickSave();
        Action<bool, string> callback = gallery.Completed;
        UnityEngine.Object.Destroy(owner);
        yield return null;
        yield return null;
        Assert.That(photo == null, Is.True);
        Assert.DoesNotThrow(() => callback(true, "late result"));
    }

    [UnityTest]
    public IEnumerator MasksAndPixelCaptureShareTheSameScaledViewport()
    {
        RectTransform panel = (RectTransform)manager.photoPanel.transform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(300, 600);
        panel.localScale = Vector3.one * 0.5f;
        CameraMaskController masks = manager.photoPanel.AddComponent<CameraMaskController>();
        masks.topMargin = 50;
        masks.photoTopMask = (RectTransform)Panel("TopMask").transform;
        masks.photoBottomMask = (RectTransform)Panel("BottomMask").transform;
        masks.photoTopMask.SetParent(panel, false);
        masks.photoBottomMask.SetParent(panel, false);
        manager.OnClickStart();
        yield return null;
        Assert.That(masks.TryGetCaptureRect(out RectInt capture), Is.True);
        Assert.That(capture.width, Is.EqualTo(150));
        Assert.That(capture.height, Is.EqualTo(200));
        Assert.That(masks.photoTopMask.rect.height, Is.EqualTo(50).Within(0.01f));
        Assert.That(masks.photoBottomMask.rect.height, Is.EqualTo(150).Within(0.01f));
        Assert.That(masks.photoTopMask.rect.width, Is.EqualTo(300).Within(0.01f));
    }
}
