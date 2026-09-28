using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class DeepSeekIntegration : MonoBehaviour
{
    [SerializeField] private string apiKey = "sk-ea7bfa1fea834ced906e1f7a2a356fbf";
    [SerializeField] private string apiUrl = "https://api.deepseek.com/chat/completions";
    [SerializeField] private string model = "deepseek-chat";

    public event Action<string> OnResponseReceived;

    [Serializable]
    public class Message
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class RequestData
    {
        public string model;
        public Message[] messages;
    }

    [Serializable]
    public class ResponseData
    {
        public Choice[] choices;
    }

    [Serializable]
    public class Choice
    {
        public Message message;
    }

    public void AskDeepSeek(string prompt, Action<string> onComplete = null)
    {
        StartCoroutine(SendPrompt(prompt, onComplete));
    }

    private IEnumerator SendPrompt(string prompt, Action<string> onComplete)
    {
        RequestData data = new RequestData
        {
            model = model,
            messages = new[] { new Message { role = "user", content = prompt } }
        };

        string jsonPayload = JsonUtility.ToJson(data);

        using (UnityWebRequest request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();

            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    ResponseData response = JsonUtility.FromJson<ResponseData>(request.downloadHandler.text);
                    string responseText = response?.choices != null && response.choices.Length > 0
                        ? response.choices[0].message?.content
                        : string.Empty;

                    Debug.Log("DeepSeek Response: " + responseText);
                    OnResponseReceived?.Invoke(responseText);
                    onComplete?.Invoke(responseText);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Failed to parse DeepSeek response: " + ex.Message);
                    OnResponseReceived?.Invoke(string.Empty);
                    onComplete?.Invoke(string.Empty);
                }
            }
            else
            {
                Debug.LogError("DeepSeek request failed: " + request.error);
                OnResponseReceived?.Invoke(string.Empty);
                onComplete?.Invoke(string.Empty);
            }
        }
    }
}
