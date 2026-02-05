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
        // 自分のパネルの本当の大きさを取得する
        myRect = GetComponent<RectTransform>();
        AdjustMasks();
    }

    void AdjustMasks()
    {
        // Screen.width ではなく、このパネルの幅(rect.width)を使うのが正解
        float uiWidth = myRect.rect.width;
        float uiHeight = myRect.rect.height;

        // 4:3 の高さを計算
        float visibleHeight = uiWidth * 4f / 3f;

        // 下の余白を計算
        float bottomMaskHeight = uiHeight - visibleHeight - topMargin;
        if (bottomMaskHeight < 0) bottomMaskHeight = 0;

        // 設定値を計算
        Vector2 topSize = new Vector2(0, topMargin);
        Vector2 topPos = new Vector2(0, -topMargin / 2f);
        Vector2 bottomSize = new Vector2(0, bottomMaskHeight);
        Vector2 bottomPos = new Vector2(0, bottomMaskHeight / 2f);

        // 適用
        if (photoTopMask) ApplySettings(photoTopMask, topSize, topPos, true);
        if (photoBottomMask) ApplySettings(photoBottomMask, bottomSize, bottomPos, false);
        if (resultTopMask) ApplySettings(resultTopMask, topSize, topPos, true);
        if (resultBottomMask) ApplySettings(resultBottomMask, bottomSize, bottomPos, false);
    }

    void ApplySettings(RectTransform mask, Vector2 size, Vector2 pos, bool isTop)
    {
        // 強制的にアンカーを中心にリセットしてから設定する（ズレ防止）
        mask.anchorMin = new Vector2(0.5f, 0.5f);
        mask.anchorMax = new Vector2(0.5f, 0.5f);
        mask.pivot = new Vector2(0.5f, 0.5f);
        
        // 横幅はパネルいっぱいにする
        mask.sizeDelta = new Vector2(myRect.rect.width + 100, size.y); // +100は隙間埋め
        
        if (isTop) // 上の帯
        {
            // パネルの上端を基準に配置
            mask.anchorMin = new Vector2(0, 1);
            mask.anchorMax = new Vector2(1, 1);
            mask.anchoredPosition = pos;
        }
        else // 下の帯
        {
            // パネルの下端を基準に配置
            mask.anchorMin = new Vector2(0, 0);
            mask.anchorMax = new Vector2(1, 0);
            mask.anchoredPosition = pos;
        }
    }
}