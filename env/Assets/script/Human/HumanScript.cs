//using Oculus.Voice;
using UnityEditor.Experimental;
using UnityEngine;
using UnityEngine.InputSystem;

public class HumanScript : MonoBehaviour
{
    #region Settings & References
    [Header("VR Settings")]
    [Tooltip("Drag the Main Camera of your VR headset here")]
    public Transform vrHeadset;

    [Tooltip("Maximum distance to activate the agent")]
    public float gazeDistance = 5f;

    [Header("Input Actions")]
    [Tooltip("Input Action for the trigger click")]
    public InputActionReference triggerAction;
    [Tooltip("Input Action for the secondary trigger click")]
    public InputActionReference secondaryTriggerAction;

    [Header("Voice SDK")]
    //public AppVoiceExperience appVoiceExperience;
    #endregion

    #region Internal State Variables
    private int agentLayerIndex; // Ottimizzato: indice intero invece di chiamare NameToLayer ogni frame
    private int artifactLayerIndex;
    private GameObject currentAgent;
    private GameObject currentArtifact;
    private AgentScript currentAgentScript;
    private Artifact currentArtifactScript;
    private GameObject talkingAgent;
    private GameObject interactingArtifact;
    private AgentScript talkingAgentScript;
    private Artifact interactingArtifactScript;
    private bool imInConversation;
    private bool imInteractingWithArtifact;
    private string[] activationWords = { "hello", "hi", "hey", "greetings", "salutations", "yo", "good day" };
    private string[] deactivationWords = { "goodbye", "bye", "see you", "farewell", "later" };
    private string[] cancelRequestWords = { "cancel", "stop", "nevermind", "forget it", "that's wrong", "wrong", "i didn't mean that" };
    #endregion

    private bool lookingAtAgent; // Variabile per tracciare se stiamo guardando un agente durante la conversazione

    #region Unity Lifecycle (Start, Enable, Disable, Destroy)
    void Start()
    {
        // Salviamo l'indice del layer una volta sola all'avvio
        agentLayerIndex = LayerMask.NameToLayer("Agent");
        artifactLayerIndex = LayerMask.NameToLayer("Artifact");
        imInConversation = false;
        imInteractingWithArtifact = false;
        //appVoiceExperience.VoiceEvents.OnFullTranscription.AddListener(OnTranscriptionDetected);
    }

    private void OnEnable()
    {
        if (triggerAction != null) triggerAction.action.Enable();
        if (secondaryTriggerAction != null) secondaryTriggerAction.action.Enable();
    }

    private void OnDisable()
    {
        if (triggerAction != null) triggerAction.action.Disable();
        if (secondaryTriggerAction != null) secondaryTriggerAction.action.Disable();
    }

    private void OnDestroy()
    {
        /*if (appVoiceExperience != null && appVoiceExperience.VoiceEvents != null)
        {
            appVoiceExperience.VoiceEvents.OnFullTranscription.RemoveListener(OnTranscriptionDetected);
        }*/
    }
    #endregion

    #region Core Logic
    void Update()
    {
        if (!imInConversation && !imInteractingWithArtifact)
        {
            HandleSearchMode();
        }
        else if (imInConversation)
        {
            HandleConversationMode();
        }
        else if (imInteractingWithArtifact)
        {
            HandleInteractionMode();
        }
    }

    private void HandleSearchMode()
    {
        //Debug.DrawRay(vrHeadset.position, vrHeadset.forward * gazeDistance, Color.red);
        RaycastHit hitInfo;

        if (Physics.Raycast(vrHeadset.position, vrHeadset.forward, out hitInfo, gazeDistance))
        {
            GameObject hitObj = hitInfo.collider.gameObject;

            /*if (!appVoiceExperience.Active)
            {
                appVoiceExperience.Activate();
            }*/

            if (hitObj.layer != agentLayerIndex && currentAgent != null)
            {
                currentAgentScript.DeactivateVisualClue();
                currentAgent = null;
                currentAgentScript = null;

                /*if (appVoiceExperience.Active)
                {
                    appVoiceExperience.Deactivate();
                }*/

            }
            if (hitObj.layer != artifactLayerIndex && currentArtifact != null)
            {
                currentArtifact = null;
                currentArtifactScript = null;
            }

            if (hitObj.layer == agentLayerIndex)
            {
                if (currentAgent != hitObj && currentAgent != null)
                {
                    currentAgentScript.DeactivateVisualClue();
                }

                if (currentAgent != hitObj)
                {
                    currentAgent = hitObj;
                    currentAgentScript = hitObj.GetComponent<AgentScript>();
                    currentAgentScript.ActivateVisualClue();
                }

                if (triggerAction.action.WasPressedThisFrame())
                {
                    talkingAgent = currentAgent;
                    talkingAgentScript = currentAgentScript;
                    ActivateAgent();
                }
                //per presentazione da togliere
                lookingAtAgent = true;
            }
            else if (hitObj.layer == artifactLayerIndex)
            {
                if (currentArtifact != hitObj)
                {
                    currentArtifact = hitObj;
                    currentArtifactScript = hitObj.GetComponent<Artifact>();
                    //currentArtifactScript.ActivateVisualClue();
                }
                if (triggerAction.action.WasPressedThisFrame())
                {
                    interactingArtifact = currentArtifact;
                    interactingArtifactScript = currentArtifactScript;
                    ActivateArtifact();
                }
            }
        }
        else if (currentAgent != null)
        {
            /*if (appVoiceExperience.Active)
            {
                appVoiceExperience.Deactivate();
            }*/
            currentAgentScript.DeactivateVisualClue();
            currentAgent = null;
            currentAgentScript = null;
        }
        else if (currentArtifact != null)
        {
            currentArtifact = null;
            currentArtifactScript = null;
        }
    }

    private void HandleConversationMode()
    {
        RaycastHit hitInfo;

        // Anche qui rimosso agentLayerMask per bloccare l'uso del trigger attraverso i muri
        if (Physics.Raycast(vrHeadset.position, vrHeadset.forward, out hitInfo, gazeDistance))
        {
            GameObject hitAgent = hitInfo.collider.gameObject;

            // Assicuriamoci che l'oggetto colpito sia sul layer Agent
            if (hitAgent.layer == agentLayerIndex)
            {
                if (triggerAction.action.WasPressedThisFrame())
                {
                    if (hitAgent == talkingAgent)
                    {
                        DeactivateAgent();
                        currentAgent = null;
                        currentAgentScript = null;
                    }
                }
                else if (secondaryTriggerAction.action.WasPressedThisFrame())
                {
                    if (hitAgent == talkingAgent)
                    {
                        currentAgentScript.sendToBDI(); // Invia il messaggio al BDI quando si preme il secondo trigger
                    }
                }
            }

            ReactivateConversation();

            //per presentazione da togliere
            lookingAtAgent = true;
        }
        else
        {
            lookingAtAgent = false;
        }
    }

    private void HandleInteractionMode()
    {
        RaycastHit hitInfo;

        if (Physics.Raycast(vrHeadset.position, vrHeadset.forward, out hitInfo, gazeDistance))
        {
            GameObject hitArtifact = hitInfo.collider.gameObject;

            if (hitArtifact.layer == artifactLayerIndex)
            {
                if (triggerAction.action.WasPressedThisFrame())
                {
                    if (hitArtifact == interactingArtifact)
                    {
                        DeactivateArtifact();
                        currentArtifact = null;
                        currentArtifactScript = null;
                    }
                }
            }
        }
    }
    #endregion

    #region Actions (Agent & Voice Control)
    private void ActivateAgent()
    {
        if (talkingAgentScript != null && !imInConversation)
        {
            imInConversation = true;
            talkingAgentScript.call();
            Debug.Log("Activated voice");
            /*
            if (appVoiceExperience.Active)
            {
                appVoiceExperience.Deactivate();
            }
            appVoiceExperience.Activate();*/
        }
    }

    private void ReactivateConversation()
    {
        if (talkingAgentScript != null && imInConversation)
        {
            /*if (!appVoiceExperience.Active)
            {
                appVoiceExperience.Activate();
            }*/
        }
    }

    private void DeactivateAgent()
    {
        if (talkingAgentScript != null && imInConversation)
        {
            imInConversation = false;
            talkingAgentScript.endCall();
            Debug.Log("Deactivated voice");
            //appVoiceExperience.Deactivate();
        }
    }

    private void ActivateArtifact()
    {
        if (interactingArtifactScript != null && !imInteractingWithArtifact)
        {
            imInteractingWithArtifact = true;
            //interactingArtifactScript.Toggle();
            
            interactingArtifactScript.Play();
            imInteractingWithArtifact = true;
        }
    }
    private void DeactivateArtifact()
    {
        if (interactingArtifactScript != null && imInteractingWithArtifact)
        {
            imInteractingWithArtifact = false;
            //interactingArtifactScript.Untoggle();
        }
    }

    public void OnTranscriptionDetected(string text)
    {
        //per presentazione da togliere
        if (lookingAtAgent == false) return;

        if (VerifyEndKeyword(text) && imInConversation)
        {
            Debug.Log("deactivation keyword found, deactivating " + talkingAgent.name);
            DeactivateAgent();
        }
        else if (VerifyCancelRequest(text) && imInConversation)
        {
            Debug.Log("cancel request keyword found, sending cancel request to " + talkingAgent.name);
            talkingAgentScript.CancelText();
            ReactivateConversation();
        }
        else if (VerifyKeyword(text) && !imInConversation)
        {
            Debug.Log("activation keyword found in search phase, activating " + currentAgent.name);
            //appVoiceExperience.Deactivate();
            talkingAgent = currentAgent;
            talkingAgentScript = currentAgentScript;
            ActivateAgent();
        }
        else if (imInConversation)
        {
            if (text.Trim() != "")
            {
                Debug.Log("Updating text for " + talkingAgent.name + ": " + text);
                talkingAgentScript.updateText(text);
                ReactivateConversation();
            }
        }
    }
    #endregion

    #region Utility Methods
    private bool VerifyKeyword(string text)
    {
        string normalizedText = text.ToLower();
        foreach (string keyword in activationWords)
        {
            if (normalizedText.Contains(keyword))
            {
                Debug.Log("Keyword detected: " + keyword);
                return true;
            }
        }
        return false;
    }

    private bool VerifyEndKeyword(string text)
    {
        string normalizedText = text.ToLower();
        foreach (string keyword in deactivationWords)
        {
            if (normalizedText.EndsWith(keyword))
            {
                Debug.Log("Deactivation keyword detected: " + keyword);
                return true;
            }
        }
        return false;
    }

    private bool VerifyCancelRequest(string text)
    {
        string normalizedText = text.ToLower().Trim();
        foreach (string keyword in cancelRequestWords)
        {
            if (normalizedText.Contains(keyword))
            {
                Debug.Log("Cancel request keyword detected: " + keyword);
                return true;
            }
        }
        return false;

    }
    #endregion
}