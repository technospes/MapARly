using UnityEngine;
using UnityEngine.SceneManagement;

public class SplashScreenController : MonoBehaviour
{
    // Set this to the total length of your animation (e.g., 3.5 seconds)
    public float delayBeforeLoad = 3.5f;
    public string sceneToLoad = "IntroScene";

    void Start()
    {
        StartCoroutine(LoadNextScene());
    }

    private System.Collections.IEnumerator LoadNextScene()
    {
        yield return new WaitForSeconds(delayBeforeLoad);
        SceneManager.LoadScene(sceneToLoad);
    }
}