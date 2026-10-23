using System;
using System.Collections;
using UnityEngine;

public class ScreenshotManager : MonoBehaviour
{
    [Header("UIパネル")]
    public GameObject titlePanel;
    public GameObject photoPanel;
    public GameObject resultPanel;
    public GameObject saveCompleteMessage;

    [Header("プレビュー表示用")]
    public UnityEngine.UI.RawImage previewImage;

    [Header("設定（撮影音は従来どおり無効）")]
    public AudioSource audioSource;
    public AudioClip shutterSE;
    public UnityEngine.UI.Toggle soundToggle;

    public enum PhotoState { Title, Photo, Capturing, Result, Saving }
    public PhotoState State { get; private set; } = PhotoState.Title;
    public string LastStatus { get; private set; } = "";

    // Runtime dependency; tests replace only the external save, never the workflow.
    public IPhotoGallery Gallery { get; set; }

    private Texture2D cachedTexture;
    private UnityEngine.UI.Text statusLabel;
    private GameObject statusPanel;
    private int saveRequest;
    private bool captureInterrupted;

    void OnDisable()
    {
        if (State != PhotoState.Capturing) return;
        StopAllCoroutines();
        captureInterrupted = true;
    }

    void OnEnable()
    {
        if (!captureInterrupted) return;
        captureInterrupted = false;
        ShowPanels(PhotoState.Photo);
        SetStatus("Capture interrupted. Please retry.");
    }

    void Start()
    {
        if (!titlePanel || !photoPanel || !resultPanel || !previewImage)
        {
            Debug.LogError("ScreenshotManager: 必須UI参照が未設定です。設定検査を実行してください。", this);
            enabled = false;
            return;
        }
        Gallery = Gallery ?? new NativePhotoGallery();
        ShowPanels(PhotoState.Title);
        if (saveCompleteMessage) saveCompleteMessage.SetActive(false);
    }

    public void OnClickStart()
    {
        if (!isActiveAndEnabled || State != PhotoState.Title) return;
        SetStatus("");
        ShowPanels(PhotoState.Photo);
    }

    public void OnClickShutter()
    {
        if (!isActiveAndEnabled || State != PhotoState.Photo) return;
        CameraMaskController masks = photoPanel.GetComponent<CameraMaskController>();
        if (!masks || !masks.TryGetCaptureRect(out RectInt captureRect))
        {
            ReportFailure("Capture needs a portrait view with room for the photo frame.");
            return;
        }
        SetStatus("");
        StartCoroutine(CaptureScreen(captureRect, new Vector2Int(Screen.width, Screen.height)));
    }

    IEnumerator CaptureScreen(RectInt captureRect, Vector2Int screenSize)
    {
        ShowPanels(PhotoState.Capturing);
        yield return new WaitForEndOfFrame();

        Texture2D captured = null;
        try
        {
            if (Screen.width != screenSize.x || Screen.height != screenSize.y)
                throw new InvalidOperationException("Screen size changed. Please take the photo again.");
            captured = new Texture2D(captureRect.width, captureRect.height, TextureFormat.RGB24, false);
            captured.ReadPixels(new Rect(captureRect.x, captureRect.y, captureRect.width, captureRect.height), 0, 0);
            captured.Apply();
            AcceptCapture(captured);
        }
        catch (Exception exception)
        {
            if (captured && captured != cachedTexture) Destroy(captured);
            ShowPanels(PhotoState.Photo);
            ReportFailure("Capture failed. Please retry. " + exception.Message);
        }
    }

    internal void AcceptCapture(Texture2D captured)
    {
        if (!captured) throw new ArgumentNullException(nameof(captured));
        ReleaseCapture();
        cachedTexture = captured;
        previewImage.texture = captured;
        previewImage.color = Color.white;
        ShowPanels(PhotoState.Result);
    }

    public void OnClickSave()
    {
        if (!isActiveAndEnabled || State != PhotoState.Result || !cachedTexture) return;
        State = PhotoState.Saving;
        SetStatus("Saving photo...");
        int request = ++saveRequest;
        try
        {
            (Gallery ?? (Gallery = new NativePhotoGallery())).Save(cachedTexture, (success, detail) =>
            {
                if (!this || request != saveRequest || State != PhotoState.Saving) return;
                CompleteSave(success, detail);
            });
        }
        catch (Exception exception)
        {
            if (State == PhotoState.Saving) CompleteSave(false, exception.Message);
        }
    }

    private void CompleteSave(bool success, string detail)
    {
        if (!success)
        {
            State = PhotoState.Result;
            ReportFailure("Save failed. Check photo permission and retry. " + detail);
            return;
        }

        ReleaseCapture();
        ShowPanels(PhotoState.Photo);
        SetStatus("Photo saved.");
        if (saveCompleteMessage) saveCompleteMessage.SetActive(true);
        CancelInvoke(nameof(HideSaveMessage));
        Invoke(nameof(HideSaveMessage), 2f);
    }

    public void OnClickRetake()
    {
        if (!isActiveAndEnabled || State != PhotoState.Result) return;
        ReleaseCapture();
        SetStatus("");
        ShowPanels(PhotoState.Photo);
    }

    private void ShowPanels(PhotoState state)
    {
        State = state;
        titlePanel.SetActive(state == PhotoState.Title);
        photoPanel.SetActive(state == PhotoState.Photo);
        resultPanel.SetActive(state == PhotoState.Result || state == PhotoState.Saving);
    }

    private void ReportFailure(string message)
    {
        SetStatus(message);
        Debug.LogWarning(message, this);
    }

    private void SetStatus(string message)
    {
        CancelInvoke(nameof(HideSaveMessage));
        if (saveCompleteMessage) saveCompleteMessage.SetActive(false);
        LastStatus = message;
        if (!statusPanel && message.Length > 0) CreateStatusLabel();
        if (statusLabel) statusLabel.text = message;
        if (statusPanel) statusPanel.SetActive(message.Length > 0);
    }

    private void CreateStatusLabel()
    {
        Canvas canvas = previewImage.GetComponentInParent<Canvas>(true);
        if (!canvas) return;
        statusPanel = new GameObject("PhotoStatus", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        statusPanel.transform.SetParent(canvas.transform, false);
        RectTransform panelRect = (RectTransform)statusPanel.transform;
        panelRect.anchorMin = new Vector2(0.05f, 0.88f);
        panelRect.anchorMax = new Vector2(0.95f, 0.98f);
        panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        UnityEngine.UI.Image background = statusPanel.GetComponent<UnityEngine.UI.Image>();
        background.color = new Color(0, 0, 0, 0.85f);
        background.raycastTarget = false;
        var label = new GameObject("Message", typeof(RectTransform), typeof(UnityEngine.UI.Text));
        label.transform.SetParent(statusPanel.transform, false);
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12, 4);
        labelRect.offsetMax = new Vector2(-12, -4);
        statusLabel = label.GetComponent<UnityEngine.UI.Text>();
        statusLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusLabel.fontSize = 28;
        statusLabel.alignment = TextAnchor.MiddleCenter;
        statusLabel.color = Color.white;
        statusLabel.raycastTarget = false;
    }

    private void HideSaveMessage() => SetStatus("");

    private void ReleaseCapture()
    {
        if (previewImage) previewImage.texture = null;
        if (cachedTexture) Destroy(cachedTexture);
        cachedTexture = null;
    }

    void OnDestroy()
    {
        ++saveRequest;
        ReleaseCapture();
        if (statusPanel) Destroy(statusPanel);
    }
}
