using UnityEngine;
using UnityEngine.SceneManagement;

public class SplashScreen : MonoBehaviour
{
    public float delayBeforeLoad = 3.0f; // How long the animation plays
    public string sceneToLoad = "IntroScene"; // The name of your main UI scene

    void Start()
    {
        StartCoroutine(LoadNextScene());
    }

    private System.Collections.IEnumerator LoadNextScene()
    {
        // Wait for the animation to finish
        yield return new WaitForSeconds(delayBeforeLoad);

        // Load your IntroScene
        SceneManager.LoadScene(sceneToLoad);
    }
}