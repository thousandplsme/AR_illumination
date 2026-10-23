using UnityEngine;

/// <summary>Shared by the visible masks and the captured image. Coordinates start at bottom-left.</summary>
public static class PhotoCaptureGeometry
{
    public const float AspectRatio = 3f / 4f;

    public static bool TryGetVisibleRect(float width, float height, float topMargin, out Rect visible)
    {
        visible = default;
        if (!IsFinite(width) || !IsFinite(height) || !IsFinite(topMargin) ||
            width <= 0 || height <= 0 || topMargin < 0)
            return false;

        float photoHeight = width / AspectRatio;
        float bottom = height - topMargin - photoHeight;
        if (bottom < -0.01f)
            return false;

        visible = new Rect(0, Mathf.Max(0, bottom), width, photoHeight);
        return true;
    }

    public static bool TryGetPixelRect(Rect screenRect, int screenWidth, int screenHeight, out RectInt pixels)
    {
        pixels = default;
        if (!IsFinite(screenRect.x) || !IsFinite(screenRect.y) ||
            !IsFinite(screenRect.width) || !IsFinite(screenRect.height) ||
            screenWidth <= 0 || screenHeight <= 0)
            return false;

        int left = Mathf.RoundToInt(screenRect.xMin);
        int bottom = Mathf.RoundToInt(screenRect.yMin);
        int right = Mathf.RoundToInt(screenRect.xMax);
        int top = Mathf.RoundToInt(screenRect.yMax);
        if (left < 0 || bottom < 0 || right > screenWidth || top > screenHeight ||
            right <= left || top <= bottom)
            return false;

        pixels = new RectInt(left, bottom, right - left, top - bottom);
        return true;
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
