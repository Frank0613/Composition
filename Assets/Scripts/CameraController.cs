using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    // Cache
    private RenderTexture _rt;
    private Texture2D _tex;
    private int _cachedW, _cachedH;

    private Camera Cam;
    void Start()
    {
        Cam = GetComponent<Camera>();
    }

    public string GetCameraScreen(int width, int height, int jpegQuality = 70)
    {
        if (!Cam) return null;

        if (_rt == null || _cachedW != width || _cachedH != height)
        {
            ReleaseCapture();
            _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
            _tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            _cachedW = width; _cachedH = height;
        }

        var prev = Cam.targetTexture;
        Cam.targetTexture = _rt;
        Cam.Render();

        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
        _tex.Apply(false, false);
        RenderTexture.active = null;

        Cam.targetTexture = prev;

        byte[] jpg = _tex.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
        return Convert.ToBase64String(jpg);
    }
    void ReleaseCapture()
    {
        if (_rt) { _rt.Release(); _rt = null; }
        if (_tex) { Destroy(_tex); _tex = null; }
    }
    void OnDestroy() => ReleaseCapture();

    public Vector3 GetCameraPosition() { return transform.position; }
    public void SetCameraPosition(Vector3 pos) { transform.position = pos; }
    public Vector3 GetCameraEulerAngles() { return transform.eulerAngles; }
    public void SetCameraEulerAngles(Vector3 euler) { transform.eulerAngles = euler; }

}
