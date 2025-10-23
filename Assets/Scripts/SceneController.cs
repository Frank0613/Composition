using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    [Header("Test mode")]
    public bool testMode = false;

    public static event Action OnSceneReset;

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
        OnSceneReset?.Invoke();

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }
}
