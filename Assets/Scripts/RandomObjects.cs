using UnityEngine;

public class RandomObjects : MonoBehaviour
{
    [Header("Object List")]
    public GameObject[] targetObjects;
    public int spawnCount = 1;

    [Header("Object Spawn Range")]
    public Vector3 spawnPosition = Vector3.zero;
    public Vector2 rangeSize = new Vector2(10f, 10f);
    public Transform targetParent;
    void Start()
    {
        RandomPlaceObj();
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
        Vector3 center = spawnPosition;
        Vector3 size = new Vector3(rangeSize.x, 0.01f, rangeSize.y);

        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Gizmos.DrawCube(center, size);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, new Vector3(rangeSize.x, 0f, rangeSize.y));

        Gizmos.DrawSphere(center, Mathf.Min(rangeSize.x, rangeSize.y) * 0.01f + 0.05f);
    }
}
