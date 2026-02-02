using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScreenshotManager : MonoBehaviour
{
    [Header("UIパネル")]
    public GameObject photoPanel;  // 撮影中のUI（ボタンなど）
    public GameObject resultPanel; // 撮影後のUI（プレビュー画面）
    
    [Header("プレビュー表示用")]
    public RawImage previewImage;  // 撮った写真を表示する場所

    // シャッターボタンから呼ぶ関数
    public void OnClickShutter()
    {
        StartCoroutine(TakeScreenshot());
    }

    // 閉じるボタンから呼ぶ関数
    public void OnClickClose()
    {
        resultPanel.SetActive(false); // 結果画面を消す
        photoPanel.SetActive(true);   // 撮影画面を出す
    }

    // 撮影コルーチン（一連の流れ）
    IEnumerator TakeScreenshot()
    {
        // 1. UIを消す（ボタンなどが写り込まないように）
        photoPanel.SetActive(false);
        resultPanel.SetActive(false);

        // 2. 画面の描画が終わるまで待つ（超重要）
        yield return new WaitForEndOfFrame();

        // 3. 画面全体をテクスチャとして切り取る
        Texture2D ss = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        ss.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        ss.Apply();

        // 4. スマホのギャラリーに保存する (Native Gallery使用)
        // ※アルバム名は "WakayamaAR" に設定
        NativeGallery.SaveImageToGallery(ss, "WakayamaAR", "AR_Photo_{0}.png");

        // 5. プレビュー画面に撮った画像を表示する
        previewImage.texture = ss;
        previewImage.color = Color.white; // 元が透明だと見えないので白にする

        // 6. UIを復活させる（結果画面を出す）
        resultPanel.SetActive(true);
        
        // ※メモリ節約のため、ss変数の破棄はシーン遷移時などに行うのが理想ですが、
        // 簡易実装では上書きしていく形でOKです。
    }
}