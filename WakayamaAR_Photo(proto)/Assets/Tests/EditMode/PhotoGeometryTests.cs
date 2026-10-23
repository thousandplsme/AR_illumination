using NUnit.Framework;
using UnityEngine;

public class PhotoGeometryTests
{
    [TestCase(1080, 1920, 150, 330)]
    [TestCase(720, 1280, 100, 220)]
    [TestCase(1080, 2400, 150, 810)]
    [TestCase(300, 450, 50, 0)]
    public void PortraitFrameMatchesMargin(float width, float height, float margin, float bottom)
    {
        Assert.That(PhotoCaptureGeometry.TryGetVisibleRect(width, height, margin, out Rect visible), Is.True);
        Assert.That(visible.yMin, Is.EqualTo(bottom).Within(0.01f));
        Assert.That(visible.width / visible.height, Is.EqualTo(0.75f).Within(0.0001f));
        Assert.That(height - visible.yMax, Is.EqualTo(margin).Within(0.01f));
    }

    [TestCase(1920, 1080, 150)]
    [TestCase(800, 600, 150)]
    [TestCase(0, 1920, 150)]
    [TestCase(1080, 0, 150)]
    [TestCase(1080, 1920, -1)]
    [TestCase(1080, 1920, 1920)]
    public void InvalidFrameIsRejected(float width, float height, float margin)
    {
        Assert.That(PhotoCaptureGeometry.TryGetVisibleRect(width, height, margin, out _), Is.False);
    }

    [Test]
    public void NonFiniteSizeIsRejected()
    {
        Assert.That(PhotoCaptureGeometry.TryGetVisibleRect(float.NaN, 1920, 150, out _), Is.False);
        Assert.That(PhotoCaptureGeometry.TryGetVisibleRect(1080, float.PositiveInfinity, 150, out _), Is.False);
    }

    [Test]
    public void PixelRectRoundsEdgesWithoutLeavingScreen()
    {
        Assert.That(PhotoCaptureGeometry.TryGetPixelRect(new Rect(0.1f, 330.1f, 1079.8f, 1440),
            1080, 1920, out RectInt pixels), Is.True);
        Assert.That(pixels, Is.EqualTo(new RectInt(0, 330, 1080, 1440)));
    }

    [TestCase(-2, 0, 300, 400)]
    [TestCase(0, -2, 300, 400)]
    [TestCase(0, 0, 1200, 400)]
    [TestCase(0, 0, 300, 2000)]
    [TestCase(0, 0, 0, 400)]
    public void OutOfScreenReadIsRejected(float x, float y, float width, float height)
    {
        Assert.That(PhotoCaptureGeometry.TryGetPixelRect(new Rect(x, y, width, height), 1080, 1920, out _), Is.False);
    }

    [Test]
    public void SavedProductionSceneHasValidUnityReferences()
    {
        Assert.That(PhotoProjectValidation.Validate(), Is.Empty);
    }
}
