using UnityEngine;

public class DeepSeekIntegrationHook : MonoBehaviour
{
    [SerializeField] private DeepSeekIntegration deepSeekIntegration;
    [SerializeField] private string prompt = "Say hello in one sentence.";
    [SerializeField] private bool sendOnStart = true;

    private void Awake()
    {
        if (deepSeekIntegration == null)
            deepSeekIntegration = GetComponent<DeepSeekIntegration>();

        if (deepSeekIntegration == null)
            deepSeekIntegration = gameObject.AddComponent<DeepSeekIntegration>();
    }

    private void Start()
    {
        if (sendOnStart)
            SendPrompt();
    }

    public void SendPrompt()
    {
        if (deepSeekIntegration == null)
            return;

        deepSeekIntegration.OnResponseReceived -= HandleResponse;
        deepSeekIntegration.OnResponseReceived += HandleResponse;
        deepSeekIntegration.AskDeepSeek(prompt);
    }

    private void HandleResponse(string response)
    {
        Debug.Log("DeepSeek Hook Response: " + response);
    }

    private void OnDisable()
    {
        if (deepSeekIntegration != null)
            deepSeekIntegration.OnResponseReceived -= HandleResponse;
    }
}
