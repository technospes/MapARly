using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class GoogleTTSManager : MonoBehaviour
{
    public static GoogleTTSManager Instance;

    [SerializeField]
    private string apiKey;
    private AudioSource audioSource;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void Speak(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (audioSource.isPlaying) audioSource.Stop();
        StartCoroutine(SynthesizeSpeech(text));
    }

    private IEnumerator SynthesizeSpeech(string text)
    {
        string url = $"https://texttospeech.googleapis.com/v1/text:synthesize?key={apiKey}";

        TTSRequest requestData = new TTSRequest
        {
            input = new InputData() { text = text },
            voice = new VoiceData() { languageCode = "en-US", ssmlGender = "NEUTRAL" },
            audioConfig = new AudioConfig() { audioEncoding = "MP3" }
        };

        string jsonData = JsonUtility.ToJson(requestData);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"❌ Google TTS Error: {request.error}");
                Debug.LogError($"Response: {request.downloadHandler.text}");
            }
            else
            {
                // --- THE FIX IS HERE ---

                byte[] audioBytes = null; // 1. Declare here so it's accessible later

                try
                {
                    // 2. The 'try' block now ONLY handles the risky parsing/decoding
                    TTSResponse response = JsonUtility.FromJson<TTSResponse>(request.downloadHandler.text);
                    if (string.IsNullOrEmpty(response.audioContent))
                    {
                        Debug.LogError("❌ Google TTS: No audio content in response!");
                        Debug.LogError($"Response: {request.downloadHandler.text}");
                        yield break;
                    }
                    audioBytes = System.Convert.FromBase64String(response.audioContent);
                    Debug.Log($"✅ Decoded {audioBytes.Length} bytes of audio data");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"❌ Google TTS Exception: {ex.Message}");
                    Debug.LogError($"Response: {request.downloadHandler.text}");
                }

                // 3. This is now OUTSIDE the try...catch block.
                // If the decoding was successful, play the audio.
                if (audioBytes != null)
                {
                    yield return StartCoroutine(PlayAudioFromData(audioBytes));
                }
            }
        }
    }

    private IEnumerator PlayAudioFromData(byte[] audioData)
    {
        string tempPath = System.IO.Path.Combine(Application.temporaryCachePath, "tts_audio.mp3");

        try
        {
            System.IO.File.WriteAllBytes(tempPath, audioData);

            using (var www = UnityWebRequestMultimedia.GetAudioClip("file://" + tempPath, AudioType.MPEG))
            {
                yield return www.SendWebRequest();

                if (www.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                    if (clip != null)
                    {
                        audioSource.clip = clip;
                        audioSource.Play();
                        Debug.Log("✅ Playing Google TTS audio");
                    }
                }
                else
                {
                    Debug.LogError($"❌ Failed to load audio: {www.error}");
                }
            }
        }
        finally
        {
            // --- BEST PRACTICE ---
            // Clean up the temporary file whether the audio played or not.
            if (System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(tempPath);
            }
        }
    }

    [System.Serializable]
    public class TTSResponse
    {
        public string audioContent;
    }

    [System.Serializable] private class TTSRequest { public InputData input; public VoiceData voice; public AudioConfig audioConfig; }
    [System.Serializable] private class InputData { public string text; }
    [System.Serializable] private class VoiceData { public string languageCode; public string ssmlGender; }
    [System.Serializable] private class AudioConfig { public string audioEncoding; }
}