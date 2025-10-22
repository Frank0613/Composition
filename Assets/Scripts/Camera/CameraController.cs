using UnityEngine;

public class CameraController : CameraScreen
{

    // Camera API
    public float GetCameraFOV() => Cam.fieldOfView;
    public void SetCameraFOV(float fov) => Cam.fieldOfView = fov;

    // Position & Rotation API
    public Vector3 GetCameraPosition() { return transform.position; }
    public void SetCameraPosition(Vector3 pos) { transform.position = pos; }
    public Vector3 GetCameraEulerAngles() { return transform.eulerAngles; }
    public void SetCameraEulerAngles(Vector3 euler) { transform.eulerAngles = euler; }

}
