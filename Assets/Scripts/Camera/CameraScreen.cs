using System;
using UnityEngine;

public class CameraScreen : MonoBehaviour
{
    protected RenderTexture _rt;
    protected Texture2D _tex;
    protected int _cachedW, _cachedH;
    protected Camera Cam;
    protected void Awake()
    {
        Cam = GetComponent<Camera>();
    }
    // CameraScreen API
    public string GetCameraScreen(int width, int height, int jpegQuality = 70)
    {
        if (!Cam) return null;

        ResetBuffer(width, height);

        // Camera -> RenderTexture
        var prev = Cam.targetTexture;
        Cam.targetTexture = _rt;
        Cam.Render();

        // RenderTexture -> Texture2D
        RenderTexture.active = _rt;
        _tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
        _tex.Apply(false, false);
        RenderTexture.active = null;

        // Restore the camera's original render target
        Cam.targetTexture = prev;

        // Convert to JPG and return Base64 string
        byte[] jpg = _tex.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100));
        return Convert.ToBase64String(jpg);
    }
    // If the width or height changes or no RenderTexture, reset the buffer
    protected void ResetBuffer(int width, int height)
    {

        if (_rt == null || _cachedW != width || _cachedH != height)
        {
            ReleaseCapture();
            _rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32);
            _tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            _cachedW = width; _cachedH = height;
        }
    }

    // Release old RenderTexture and Texture2D to avoid occupying memory
    protected void ReleaseCapture()
    {
        if (_rt) { _rt.Release(); _rt = null; }
        if (_tex) { Destroy(_tex); _tex = null; }
    }

    protected void OnDestroy() => ReleaseCapture();
}
