using UnityEngine;
using TMPro;

public class AgentScript : MonoBehaviour
{
    #region Settings & References
    [Header("Visual Settings")]
    [SerializeField] private Color colorHighlight = Color.red; 
    
    [Header("User Interface")]
    public Canvas canvas;
    
    [Tooltip("The text component inside the Canvas. If left empty, it will be found automatically at start.")]
    [SerializeField] private TextMeshProUGUI textComponent;

    //[SerializeField] private chatBdiConnetor chatConnector; // Riferimento al connettore BDI
    #endregion

    #region Internal State Variables
    private Color originalColor;
    private Renderer myRenderer;
    private bool imObserved;
    private bool inConversation;
    private bool imWaithingForResponse;
    private string messageToSend; // Variabile per memorizzare il messaggio da inviare al BDI
    
    // Testo di base, reso readonly perché non cambia
    private readonly string startingText = "Hello, I am listening!"; 
    #endregion

    #region Unity Lifecycle (Awake)
    void Awake()
    {
        myRenderer = GetComponent<Renderer>();
        imObserved = false;
        inConversation = false;
        
        // Setup in sicurezza del Canvas
        if (canvas != null)
        {
            canvas.enabled = false;
            
            // Cerchiamo il componente di testo SOLO UNA VOLTA all'avvio
            if (textComponent == null)
            {
                textComponent = canvas.GetComponentInChildren<TextMeshProUGUI>();
            }
        }
        else
        {
            Debug.LogWarning("Warning: No Canvas assigned on " + gameObject.name);
        }
        
        // Setup del colore
        if (myRenderer != null)
        {
            originalColor = myRenderer.material.color;
        }
        else
        {
            Debug.LogWarning("Warning: No Renderer component found on " + gameObject.name);
        }

        textComponent.text = startingText;
    }
    #endregion

    #region Visual & Gaze Logic
    // Funzione chiamata dal raggio VR quando lo sguardo si posa sull'agente
    public void ActivateVisualClue()
    {
        imObserved = true;
        if (myRenderer != null && !inConversation)
        {
            myRenderer.material.color = colorHighlight;
        }
    }

    // Funzione chiamata dal raggio VR quando lo sguardo si sposta altrove
    public void DeactivateVisualClue()
    {
        imObserved = false;
        if (myRenderer != null && !inConversation)
        {
            myRenderer.material.color = originalColor;
        }
    }

    private void ResetState()
    {
        imObserved = false;
        inConversation = false;
        if (myRenderer != null)
        {
            myRenderer.material.color = originalColor;
        }
    }
    #endregion

    #region Conversation Logic (Called by VRGazeInteraction)
    public void call()
    {
        if (imObserved || !inConversation)
        {
            inConversation = true;
            Debug.Log("Agent activated: " + gameObject.name);
            
            if (myRenderer != null)
            {
                myRenderer.material.color = Color.green;
            }
            
            if (canvas != null && textComponent != null)
            {
                canvas.enabled = true;
                if (imWaithingForResponse)
                {
                    textComponent.text = "still thinking...";
                }
            }
        } 
        else
        {
            Debug.Log("Agent called but not currently observed");   
        }    
    }

    public void endCall()
    {
        ResetState();
        if (canvas != null)
        {
            canvas.enabled = false;
        }
        Debug.Log("Agent call ended: " + gameObject.name);
    }

    public void updateText(string text)
    {
        // Evitiamo crash se il testo non esiste
        if(inConversation && !imWaithingForResponse && textComponent != null)
        {
            messageToSend += text + " "; // Aggiorna il messaggio da inviare al BDI
            textComponent.text = "you said: " + messageToSend;
        }
    }

    public void sendToBDI()
    {
        if(messageToSend == null)
        {
            Debug.Log("No message to send to BDI");
            return;
        }
        if (messageToSend.Trim() == "")
        {
            Debug.Log("Empty message, not sending to BDI");
            return;
        }
        //chatConnector.SendMessageToServer(gameObject.name, messageToSend); // Invia il messaggio al server BDI
        messageToSend = ""; // Resetta il messaggio dopo l'invio
        imWaithingForResponse = true; // Indica che stiamo aspettando una risposta dal BDI
        if (textComponent != null)
        {
            textComponent.text = "im thinking..."; // Aggiorna il testo per indicare che stiamo aspettando una risposta
        }
    }

    public void updateTextFromBDI(string text)
    {
        // Evitiamo crash se il testo non esiste
        if (textComponent == null) return; 
        if (imWaithingForResponse)
        {
            textComponent.text = "BDI says: " + text; // Aggiorna il testo visualizzato con il messaggio ricevuto dal BDI
            imWaithingForResponse = false; // Indica che non stiamo più aspettando una risposta dal BDI
        }
        
    }

    public void CancelText()
    {
        messageToSend = ""; // Resetta il messaggio da inviare al BDI
        if (textComponent != null)
        {
            textComponent.text = "you said: "; // Resetta il testo visualizzato
        }
    }
    #endregion
}