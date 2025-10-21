using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    [Header("Test mode")]
    public bool testMode = false;

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
        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.name);
    }
}
