using UnityEngine;
using System.Collections;

public class CameraController : CameraScreen
{
    [Header("Smoothing")]
    [SerializeField] private float moveDuration = 0.25f;
    [SerializeField] private float rotDuration = 0.25f;

    private Coroutine _posRoutine;
    private Coroutine _rotRoutine;

    public float GetCameraFOV() => Cam.fieldOfView;
    public void SetCameraFOV(float fov) => Cam.fieldOfView = fov;
    public Vector3 GetCameraPosition() => transform.position;
    public void SetCameraPosition(Vector3 pos) => transform.position = pos;
    public Vector3 GetCameraEulerAngles() => transform.eulerAngles;
    public void SetCameraEulerAngles(Vector3 euler) => transform.eulerAngles = euler;

    // ===== 新增：隨機設定 =====
    [Header("Randomize Position Settings")]

    // 位置範圍（世界座標，長方體邊界）
    [SerializeField] private Vector3 posMin = new Vector3(-1f, 1f, -1f);
    [SerializeField] private Vector3 posMax = new Vector3(1f, 2f, 1f);

    [Header("Randomize Rotation Settings")]
    // 歐拉角範圍（度）
    [SerializeField] private Vector2 XRotaRange = new Vector2(-10f, 10f);
    [SerializeField] private Vector2 YRotaRange = new Vector2(0f, 360f);
    [SerializeField] private Vector2 ZRotaRange = new Vector2(-5f, 5f);
    void Start()
    {
        RandomizeCamera();
    }
    public void RandomizeCamera()
    {
        Vector3 randPos = new Vector3(
            Random.Range(posMin.x, posMax.x),
            Random.Range(posMin.y, posMax.y),
            Random.Range(posMin.z, posMax.z)
        );

        Vector3 randEuler = new Vector3(
            Random.Range(XRotaRange.x, XRotaRange.y),
            Random.Range(YRotaRange.x, YRotaRange.y),
            Random.Range(ZRotaRange.x, ZRotaRange.y)
        );

        SetCameraPosition(randPos, ApplySpace.Absolute, LerpMode.Instant);
        SetCameraEulerAngles(randEuler, ApplySpace.Absolute, LerpMode.Instant);
    }


    // ApplySpace + LerpMode
    public void SetCameraPosition(Vector3 value, ApplySpace space, LerpMode lerp)
    {
        Vector3 target = (space == ApplySpace.Absolute) ? value
                                                        : transform.position + value;

        if (_posRoutine != null) { StopCoroutine(_posRoutine); _posRoutine = null; }

        if (lerp == LerpMode.Instant || moveDuration <= 0f)
        {
            transform.position = target;
            return;
        }

        _posRoutine = StartCoroutine(LerpPosition(target, moveDuration));
    }

    public void SetCameraEulerAngles(Vector3 valueDeg, ApplySpace space, LerpMode lerp)
    {
        Quaternion target = (space == ApplySpace.Absolute)
            ? Quaternion.Euler(valueDeg)
            : Quaternion.Euler(valueDeg) * transform.rotation;

        if (_rotRoutine != null) { StopCoroutine(_rotRoutine); _rotRoutine = null; }

        if (lerp == LerpMode.Instant || rotDuration <= 0f)
        {
            transform.rotation = target;
            return;
        }

        _rotRoutine = StartCoroutine(SlerpRotation(target, rotDuration));
    }

    private IEnumerator LerpPosition(Vector3 target, float duration)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.position = target;
        _posRoutine = null;
    }

    private IEnumerator SlerpRotation(Quaternion target, float duration)
    {
        Quaternion start = transform.rotation;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / duration;
            transform.rotation = Quaternion.Slerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.rotation = target;
        _rotRoutine = null;
    }
}
