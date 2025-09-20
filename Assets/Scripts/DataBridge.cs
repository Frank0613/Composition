using UnityEngine;

public class DataBridge : MonoBehaviour
{
    public Camera TargetCamera;
    [Header("Camera Position (X, Y, Z)")]
    public Vector3 cameraPosition;

    [Header("Camera Rotation (EulerAngles)")]
    public Vector3 cameraRotation;

    private CameraController cameraController;

    void Start()
    {
        cameraController = TargetCamera.GetComponent<CameraController>();

        if (cameraController == null)
        {
            Debug.LogError("CameraController not found");
            return;
        }

        cameraController.SetCameraPosition(cameraPosition);
        cameraController.SetCameraEulerAngles(cameraRotation);
    }

    void Update()
    {
        cameraController.SetCameraPosition(cameraPosition);
        cameraController.SetCameraEulerAngles(cameraRotation);

        Debug.Log("Cam position：" + cameraController.GetCameraPosition());
        Debug.Log("Cam rotation：" + cameraController.GetCameraEulerAngles());
    }
}
