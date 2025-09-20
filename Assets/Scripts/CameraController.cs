using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Vector3 GetCameraPosition() { return transform.position; }
    public void SetCameraPosition(Vector3 pos) { transform.position = pos; }
    public Vector3 GetCameraEulerAngles() { return transform.eulerAngles; }
    public void SetCameraEulerAngles(Vector3 euler) { transform.eulerAngles = euler; }
}
