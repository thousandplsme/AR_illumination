using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI; // デバッグ表示用

public class ARContentChanger : MonoBehaviour
{
    [System.Serializable]
    public struct MarkerPrefabPair
    {
        public string imageName;
        public GameObject prefab;
    }

    public List<MarkerPrefabPair> markerContents;
    
    private ARTrackedImage trackedImage;
    private GameObject spawnedObject;

    void Awake()
    {
        trackedImage = GetComponent<ARTrackedImage>();
    }

    void OnEnable()
    {
        // 既に何か生成済みなら何もしない（重複防止）
        if (spawnedObject != null) return;

        string detectedName = trackedImage.referenceImage.name;
        
        // ログ出力（PC繋いでなくても確認できるよう、もしTextがあれば表示）
        Debug.Log($"【AR】検出: {detectedName}");

        foreach (var item in markerContents)
        {
            // 名前が一致（部分一致や空白削除も含めて柔軟に判定）
            if (item.imageName.Trim() == detectedName.Trim())
            {
                SpawnContent(item.prefab);
                return;
            }
        }
        
        // 名前が見つからなかった場合、リストの0番目を出す（保険）
        if (markerContents.Count > 0)
        {
            Debug.LogWarning($"【AR】一致なし。デフォルト表示: {markerContents[0].imageName}");
            SpawnContent(markerContents[0].prefab);
        }
    }

    void SpawnContent(GameObject prefabToSpawn)
    {
        if (prefabToSpawn == null) return;

        // 生成
        spawnedObject = Instantiate(prefabToSpawn, transform.position, transform.rotation);
        
        // 親子関係の設定（超重要）
        spawnedObject.transform.SetParent(transform);
        
        // 座標・回転・スケールの強制リセット（これが原因で消えることが多い）
        spawnedObject.transform.localPosition = Vector3.zero;
        spawnedObject.transform.localRotation = Quaternion.identity;
        
        // コンテナのスケール問題を無視して、強制的に「1」にする
        spawnedObject.transform.localScale = Vector3.one;

        Debug.Log($"【AR】生成完了: {spawnedObject.name}");
    }
}