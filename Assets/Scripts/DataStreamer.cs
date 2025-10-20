using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;


[Serializable]
public class CameraData
{
    public PoseData pose;
    public float fov;
    public string image_b64;
    public int width;
    public int height;

    [Serializable]
    public class PoseData
    {
        public float px, py, pz;
        public float rx, ry, rz;
    }
}

[Serializable]
public class ResponseData
{
    public bool apply; // whether to apply
    public float? fov;
    public ResponsePose pose;

    [Serializable]
    public class ResponsePose
    {
        public float? px, py, pz;
        public float? rx, ry, rz;
    }
}

public class DataStreamer : MonoBehaviour
{
    private static DataStreamer instance;

    [Header("Refs")]
    public CameraController cameraController;

    [Header("Server")]

    public string serverBaseUrl = "http://127.0.0.1:8000";
    public string telemetryEndpoint = "/telemetry";
    public float sendInterval = 0.1f; // 10Hz

    [Header("Image")]
    public int imageWidth = 640;
    public int imageHeight = 360;
    [Range(1, 100)]
    public int jpegQuality = 70;

    [Header("Debug")]
    public bool logEveryResponse = false;

    private string _telemetryUrl;

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;


        if (cameraController == null)
        {
            cameraController = GetComponent<CameraController>();
        }
        _telemetryUrl = serverBaseUrl.TrimEnd('/') + telemetryEndpoint;
    }

    void OnEnable()
    {
        StartCoroutine(StreamLoop());
    }

    IEnumerator StreamLoop()
    {
        var wait = new WaitForSeconds(sendInterval);
        while (true)
        {
            yield return StartCoroutine(SendOnce());
            yield return wait;
        }
    }

    IEnumerator SendOnce()
    {
        if (cameraController == null) yield break;

        // Get datas from camera
        var pos = cameraController.GetCameraPosition();
        var eul = cameraController.GetCameraEulerAngles();
        var fov = cameraController.GetCameraFOV();
        var b64 = cameraController.GetCameraScreen(imageWidth, imageHeight, jpegQuality);

        // Put the datas into CameraData
        var camdata = new CameraData
        {
            pose = new CameraData.PoseData
            {
                px = pos.x,
                py = pos.y,
                pz = pos.z,
                rx = eul.x,
                ry = eul.y,
                rz = eul.z
            },
            fov = fov,
            image_b64 = b64,
            width = imageWidth,
            height = imageHeight
        };

        string json = JsonUtility.ToJson(camdata);

        // 2) POST /telemetry
        using (var req = new UnityWebRequest(_telemetryUrl, "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            // Error judgment after transmission (only supported in 2020.3 or upper)
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[DataStreamer] Telemetry error ({req.responseCode}): {req.error}");
                yield break;
            }

            // Response logs
            var respText = req.downloadHandler.text;
            if (logEveryResponse)
                Debug.Log($"[DataStreamer] Resp: {respText}");

            // Response datas & Apply
            try
            {
                // respText -> Json
                var j = JObject.Parse(respText);

                // Response "apply" -> apply info
                bool apply = j.Value<bool?>("apply") == true;
                if (!apply) yield break;

                // Grasp the current camera pose as the basis
                var newPos = cameraController.GetCameraPosition();
                var newEul = cameraController.GetCameraEulerAngles();

                // pose apply
                var pose = j["pose"] as JObject;
                if (pose != null)
                {
                    if (pose.ContainsKey("px") && pose["px"]?.Type != JTokenType.Null)
                        newPos.x = pose.Value<float>("px");
                    if (pose.ContainsKey("py") && pose["py"]?.Type != JTokenType.Null)
                        newPos.y = pose.Value<float>("py");
                    if (pose.ContainsKey("pz") && pose["pz"]?.Type != JTokenType.Null)
                        newPos.z = pose.Value<float>("pz");

                    if (pose.ContainsKey("rx") && pose["rx"]?.Type != JTokenType.Null)
                        newEul.x = pose.Value<float>("rx");
                    if (pose.ContainsKey("ry") && pose["ry"]?.Type != JTokenType.Null)
                        newEul.y = pose.Value<float>("ry");
                    if (pose.ContainsKey("rz") && pose["rz"]?.Type != JTokenType.Null)
                        newEul.z = pose.Value<float>("rz");
                }

                // fov apply
                if (j.ContainsKey("fov") && j["fov"]?.Type != JTokenType.Null)
                {
                    var fovVal = j.Value<float>("fov");
                    cameraController.SetCameraFOV(fovVal);
                }

                // Set camera pose
                cameraController.SetCameraPosition(newPos);
                cameraController.SetCameraEulerAngles(newEul);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[DataStreamer] Parse/apply control failed (Newtonsoft): {ex.Message}");
            }
        }
    }
}
