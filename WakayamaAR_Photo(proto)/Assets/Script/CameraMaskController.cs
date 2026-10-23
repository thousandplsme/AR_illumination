using UnityEngine;

public class CameraMaskController : MonoBehaviour
{
    [Header("撮影画面の黒帯")]
    public RectTransform photoTopMask;
    public RectTransform photoBottomMask;

    [Header("結果画面の黒帯")]
    public RectTransform resultTopMask;
    public RectTransform resultBottomMask;

    // ノッチ回避用の余白（Canvas上のピクセル数）
    public float topMargin = 150f; 

    private RectTransform myRect;

    void OnEnable()
    {
        myRect = GetComponent<RectTransform>();
        AdjustMasks();
    }

    void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled) AdjustMasks();
    }

    // 撮影直前にレイアウトを確定させ、表示枠を画面ピクセルへ変換する。
    public bool TryGetCaptureRect(out RectInt pixels)
    {
        pixels = default;
        Canvas.ForceUpdateCanvases();
        if (!myRect) myRect = GetComponent<RectTransform>();
        if (!myRect || !PhotoCaptureGeometry.TryGetVisibleRect(
                myRect.rect.width, myRect.rect.height, topMargin, out Rect visible))
            return false;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (!canvas) return false;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector3 lowerWorld = myRect.TransformPoint(new Vector3(
            myRect.rect.xMin + visible.xMin, myRect.rect.yMin + visible.yMin));
        Vector3 upperWorld = myRect.TransformPoint(new Vector3(
            myRect.rect.xMin + visible.xMax, myRect.rect.yMin + visible.yMax));
        Vector2 lower = RectTransformUtility.WorldToScreenPoint(uiCamera, lowerWorld);
        Vector2 upper = RectTransformUtility.WorldToScreenPoint(uiCamera, upperWorld);
        return PhotoCaptureGeometry.TryGetPixelRect(
            Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y), Screen.width, Screen.height, out pixels);
    }

    void AdjustMasks()
    {
        if (!myRect) myRect = GetComponent<RectTransform>();
        if (!myRect) return;
        float height = myRect.rect.height;
        float safeMargin = Mathf.Clamp(topMargin, 0, Mathf.Max(0, height));
        bool valid = PhotoCaptureGeometry.TryGetVisibleRect(myRect.rect.width, height, topMargin, out Rect visible);
        float bottomHeight = valid ? visible.yMin : 0;
        if (photoTopMask) ApplySettings(photoTopMask, safeMargin, true);
        if (photoBottomMask) ApplySettings(photoBottomMask, bottomHeight, false);
        if (resultTopMask) ApplySettings(resultTopMask, safeMargin, true);
        if (resultBottomMask) ApplySettings(resultBottomMask, bottomHeight, false);
    }

    void ApplySettings(RectTransform mask, float height, bool isTop)
    {
        float edge = isTop ? 1 : 0;
        mask.anchorMin = new Vector2(0, edge);
        mask.anchorMax = new Vector2(1, edge);
        mask.pivot = new Vector2(0.5f, edge);
        mask.sizeDelta = new Vector2(0, height);
        mask.anchoredPosition = Vector2.zero;
    }
}
