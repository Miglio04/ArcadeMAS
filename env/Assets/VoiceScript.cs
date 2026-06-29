using UnityEngine;
using TMPro;
using Oculus.Voice;
using System.Collections;

public class VoiceScript : MonoBehaviour
{    
    [SerializeField] public TextMeshProUGUI textComponent;
    [SerializeField] public AppVoiceExperience appVoiceExperience;

    private bool voiceActivated;

    void Start()
    {
        if (textComponent == null || appVoiceExperience == null)
        {
            Debug.LogError("VoiceScript is missing a TextMeshProUGUI or AppVoiceExperience reference.");
            enabled = false;
            return;
        }

        textComponent.text = "Hello, I am listening!";

        appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(UpdateText);
        appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(UpdateText);
        appVoiceExperience.VoiceEvents.OnError.AddListener(OnVoiceError);

        appVoiceExperience.OnInitialized += OnVoiceInitialized;
        StartCoroutine(ActivateVoiceWhenReady());
    }

    IEnumerator ActivateVoiceWhenReady()
    {
        yield return null;

        if (!voiceActivated && appVoiceExperience != null)
        {
            appVoiceExperience.Activate();
            voiceActivated = true;
            Debug.Log("Voice recognition activated.");
        }
    }

    void OnVoiceInitialized()
    {
        if (voiceActivated || appVoiceExperience == null)
        {
            return;
        }

        appVoiceExperience.Activate();
        voiceActivated = true;
        Debug.Log("Voice recognition activated after initialization.");
    }

    void OnDisable()
    {
        if (appVoiceExperience == null || appVoiceExperience.VoiceEvents == null)
        {
            return;
        }

        appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(UpdateText);
        appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(UpdateText);
        appVoiceExperience.VoiceEvents.OnError.RemoveListener(OnVoiceError);
        appVoiceExperience.OnInitialized -= OnVoiceInitialized;

        if (appVoiceExperience.Active)
        {
            appVoiceExperience.Deactivate();
        }

        voiceActivated = false;
    }

    void UpdateText(string newText)
    {
        if (string.IsNullOrWhiteSpace(newText) || textComponent == null)
        {
            return;
        }

        textComponent.text = newText;
    }

    void OnVoiceError(string errorType, string errorMessage)
    {
        Debug.LogError($"Voice recognition error: {errorType} - {errorMessage}");
    }
}
