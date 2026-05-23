using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ScreenshotManager : MonoBehaviour
{
    [Header("UIパネル")]
    public GameObject titlePanel;
    public GameObject photoPanel;
    public GameObject resultPanel;
    public GameObject saveCompleteMessage; // 「保存しました」という表示（あれば）

    [Header("プレビュー表示用")]
    public RawImage previewImage;

    [Header("設定")]
    public float topMargin = 150f; // MaskControllerと同じ値にする
    public AudioSource audioSource;
    public AudioClip shutterSE;
    public Toggle soundToggle; // 音のON/OFF切り替え用スイッチ

    private Texture2D cachedTexture; // 撮った写真を一時保存する変数

    void Start()
    {
        titlePanel.SetActive(true);
        photoPanel.SetActive(false);
        resultPanel.SetActive(false);
        if(saveCompleteMessage) saveCompleteMessage.SetActive(false);
    }

    public void OnClickStart()
    {
        titlePanel.SetActive(false);
        photoPanel.SetActive(true);
    }

    // シャッターボタン（撮影のみ行う）
    public void OnClickShutter()
    {
        bool shouldPlay = (soundToggle == null) || soundToggle.isOn;

        // if (shouldPlay && audioSource != null && shutterSE != null)
        // {
        //     // 音源をセットして、強制的に音量MAXで鳴らす
        //     audioSource.clip = shutterSE;
        //     audioSource.volume = 1.0f; // 音量最大
        //     audioSource.Play();        // PlayOneShotではなくPlayを使う
        // }
            
        StartCoroutine(CaptureScreen());
    }

    // 撮影コルーチン
    IEnumerator CaptureScreen()
    {
        photoPanel.SetActive(false);
        resultPanel.SetActive(false);
        titlePanel.SetActive(false);

        yield return new WaitForEndOfFrame();

        int width = Screen.width;
        int height = Mathf.RoundToInt(width * 4f / 3f);
        int startY = Mathf.RoundToInt(Screen.height - topMargin - height);
        if (startY < 0) startY = 0;

        // 写真を撮影してメモリに保存（まだスマホには保存しない）
        cachedTexture = new Texture2D(width, height, TextureFormat.RGB24, false);
        cachedTexture.ReadPixels(new Rect(0, startY, width, height), 0, 0);
        cachedTexture.Apply();

        // プレビュー表示
        previewImage.texture = cachedTexture;
        previewImage.color = Color.white;

        resultPanel.SetActive(true); // 確認画面を出す
        photoPanel.SetActive(false); // 撮影画面は隠したまま
    }

    // 【新規】保存ボタンを押した時の処理
    public void OnClickSave()
    {
        if (cachedTexture != null)
        {
            NativeGallery.SaveImageToGallery(cachedTexture, "WakayamaAR", "AR_Photo_{0}.png");
            
            // 「保存しました」を一瞬出す演出などがあればここに
            if(saveCompleteMessage) 
            {
                saveCompleteMessage.SetActive(true);
                Invoke("HideSaveMessage", 2.0f); // 2秒後に消す
            }
            // リテイク処理（メモリ解放＆画面遷移）をそのまま流用して呼ぶ
            OnClickRetake();
        }
    }

    void HideSaveMessage()
    {
        if(saveCompleteMessage) saveCompleteMessage.SetActive(false);
    }

    // キャンセル（撮り直す）ボタン
    public void OnClickRetake()
    {
        // メモリ解放（重要）
        if (cachedTexture != null) Destroy(cachedTexture);

        resultPanel.SetActive(false);
        photoPanel.SetActive(true);
    }
}