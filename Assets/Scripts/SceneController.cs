using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    [Header("Test mode")]
    public bool testMode = false;


    [Header("Object List")]
    public GameObject[] targetObjects;

    [Header("Object Spawn Range")]
    public Vector3 spawnPosition = Vector3.zero;
    public Vector2 rangeSize = new Vector2(10f, 10f);
    public int spawnCount = 1;
    public Transform targetParent;
    void Start()
    {
        RandomPlaceObj();
    }


    void Update()
    {
        if (testMode)
        {
            if (Input.GetKeyDown(KeyCode.R))
                ResetScene();
        }
    }
    public void ResetScene()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }
    private void RandomPlaceObj()
    {
        if (targetObjects == null || targetObjects.Length == 0)
        {
            Debug.LogWarning("spawnObjects is empty");
            return;
        }

        for (int i = 0; i < spawnCount; i++)
        {
            GameObject prefab = targetObjects[UnityEngine.Random.Range(0, targetObjects.Length)];
            Vector3 pos = GetRandomPointInArea();
            Instantiate(prefab, pos, Quaternion.identity, targetParent);
        }
    }
    public Vector3 GetRandomPointInArea()
    {
        float halfX = rangeSize.x * 0.5f;
        float halfZ = rangeSize.y * 0.5f;

        float rx = UnityEngine.Random.Range(-halfX, halfX);
        float rz = UnityEngine.Random.Range(-halfZ, halfZ);

        return new Vector3(spawnPosition.x + rx, spawnPosition.y, spawnPosition.z + rz);
    }
    private void OnDrawGizmos()
    {
        // 平面
        Vector3 center = spawnPosition;
        Vector3 size = new Vector3(rangeSize.x, 0.01f, rangeSize.y);

        // 填色方形
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawCube(center, size);

        // 邊框
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, new Vector3(rangeSize.x, 0f, rangeSize.y));

        // 中心點
        Gizmos.DrawSphere(center, Mathf.Min(rangeSize.x, rangeSize.y) * 0.01f + 0.05f);
    }
}
