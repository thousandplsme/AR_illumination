using System;
using UnityEngine;

public interface IPhotoGallery
{
    void Save(Texture2D texture, Action<bool, string> completed);
}

/// <summary>The plugin already handles platform permission requests and native setup.</summary>
public sealed class NativePhotoGallery : IPhotoGallery
{
    public void Save(Texture2D texture, Action<bool, string> completed)
    {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        NativeGallery.SaveImageToGallery(texture, "WakayamaAR", "AR_Photo_{0}.png",
            (success, path) => completed(success, path));
#else
        // Editor/desktop success cannot prove a mobile gallery save.
        completed(false, "Gallery save requires an Android or iOS device.");
#endif
    }
}
