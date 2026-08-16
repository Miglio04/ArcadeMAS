using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using Oculus.Voice;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

public class VRGazeInteraction : MonoBehaviour
{
    [Header("Voice SDK")]
    public AppVoiceExperience appVoiceExperience;

    #region Internal State Variables
    private AgentScript talkingAgentScript;
    private bool imInConversation;

    private string[] deactivationWords = { "goodbye", "bye", "see you", "farewell", "later" };
    private string[] cancelRequestWords = { "cancel", "stop", "nevermind", "forget it", "that's wrong", "wrong", "i didn't mean that" };
    #endregion

    void Start()
    {
        imInConversation = false;

        #if PLATFORM_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
        #endif

        if (appVoiceExperience != null)
        {
            appVoiceExperience.VoiceEvents.OnPartialTranscription.AddListener(OnTranscriptionDetected);
            appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnFullTranscriptionDetected);
            appVoiceExperience.VoiceEvents.OnError.AddListener((error, message) => Debug.LogError($"[VRGaze] Wit.ai Error: {error} - {message}"));
        }
        else
        {
            Debug.LogError("[VRGaze] appVoiceExperience non assegnato in Inspector!");
        }
    }

    private void OnDestroy()
    {
        if (appVoiceExperience != null && appVoiceExperience.VoiceEvents != null)
        {
            appVoiceExperience.VoiceEvents.OnPartialTranscription.RemoveListener(OnTranscriptionDetected);
            appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnFullTranscriptionDetected);
            appVoiceExperience.VoiceEvents.OnError.RemoveAllListeners();
        }
    }

    #region Interazione con Agente (Tramite SelectEntered)
    public void ToggleConversation(SelectEnterEventArgs args)
    {
        AgentScript clickedAgent = args.interactableObject.transform.GetComponentInParent<AgentScript>();

        if (clickedAgent == null)
        {
            Debug.LogWarning("[VRGaze] Nessun AgentScript trovato sull'oggetto cliccato o sui suoi parent.");
            return;
        }

        if (!imInConversation || talkingAgentScript != clickedAgent)
        {
            if (imInConversation && talkingAgentScript != null)
            {
                DeactivateAgent();
            }

            talkingAgentScript = clickedAgent;
            ActivateAgent();
        }
        else
        {
            DeactivateAgent();
        }
    }

    private void ActivateAgent()
    {
        if (talkingAgentScript != null && !imInConversation)
        {
            imInConversation = true;
            talkingAgentScript.call();

            if (appVoiceExperience != null && !appVoiceExperience.Active)
            {
                appVoiceExperience.Activate();
            }
        }
    }

    private void DeactivateAgent()
    {
        if (talkingAgentScript != null && imInConversation)
        {
            imInConversation = false;
            talkingAgentScript.endCall();

            if (appVoiceExperience != null && appVoiceExperience.Active)
            {
                appVoiceExperience.Deactivate();
            }

            talkingAgentScript = null;
        }
    }

    /// <summary>
    /// Ferma solo l'ascolto del microfono (senza chiudere la conversazione con l'agente),
    /// da chiamare quando la frase dell'utente è terminata.
    /// </summary>
    private void StopListening()
    {
        if (appVoiceExperience != null && appVoiceExperience.Active)
        {
            appVoiceExperience.Deactivate();
        }
    }
    #endregion

    #region Logica Vocale e Keywords
    public void OnTranscriptionDetected(string text)
    {
        if (!imInConversation || talkingAgentScript == null) return;

        if (VerifyEndKeyword(text))
        {
            DeactivateAgent();
        }
        else if (VerifyCancelRequest(text))
        {
            talkingAgentScript.CancelText();
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            talkingAgentScript.updateText(text);
        }
    }

    /// <summary>
    /// Chiamato quando Wit.ai ha rilevato la fine della frase (trascrizione completa).
    /// Ferma l'ascolto automaticamente, senza chiudere la conversazione con l'agente.
    /// </summary>
    public void OnFullTranscriptionDetected(string text)
    {
        OnTranscriptionDetected(text);

        if (imInConversation && talkingAgentScript != null && !VerifyEndKeyword(text) && !VerifyCancelRequest(text))
        {
            talkingAgentScript.sendToBDI();
        }

        if (imInConversation && !VerifyEndKeyword(text))
        {
            StopListening();
        }
    }

    private bool VerifyEndKeyword(string text)
    {
        string normalizedText = text.ToLower().Trim();
        foreach (string keyword in deactivationWords)
        {
            if (normalizedText.EndsWith(keyword)) return true;
        }
        return false;
    }

    private bool VerifyCancelRequest(string text)
    {
        string normalizedText = text.ToLower().Trim();
        foreach (string keyword in cancelRequestWords)
        {
            if (normalizedText.Contains(keyword)) return true;
        }
        return false;
    }
    #endregion
}