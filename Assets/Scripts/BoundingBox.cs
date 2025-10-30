using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(CanvasGroup))] // This is essential for fading
public class BoundingBox : MonoBehaviour
{
    // --- References to be linked in the Inspector ---
    public TextMeshProUGUI labelText;
    public Image topBorder, bottomBorder, leftBorder, rightBorder;

    // --- Fading Logic ---
    private CanvasGroup canvasGroup;
    private float lifetime = 2.0f; // How long the box stays fully visible
    private float fadeDuration = 0.5f; // How long it takes to fade out
    private float timer;

    void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    // This is the main function we'll call to activate and style the box
    public void Show(string text, Color color)
    {
        gameObject.SetActive(true);
        labelText.text = text;
        // Apply the color to all visual elements of the box
        labelText.color = color;
        if (topBorder) topBorder.color = color;
        if (bottomBorder) bottomBorder.color = color;
        if (leftBorder) leftBorder.color = color;
        if (rightBorder) rightBorder.color = color;

        // Reset the timer and make sure it's fully visible
        timer = 0f;
        canvasGroup.alpha = 1f;
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Once the box has lived its life, start fading it out
        if (timer > lifetime)
        {
            float fadeProgress = Mathf.Clamp01((timer - lifetime) / fadeDuration);
            canvasGroup.alpha = 1f - fadeProgress; // Fade from 1 to 0
            // Once fully faded, deactivate it so it can be reused by the pool
            if (canvasGroup.alpha <= 0)
            {
                gameObject.SetActive(false);
            }
        }
    }
}