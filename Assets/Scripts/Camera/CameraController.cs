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
