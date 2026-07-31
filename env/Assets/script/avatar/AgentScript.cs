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
    private string messageToSend = ""; // Variabile per memorizzare il messaggio da inviare al BDI
    
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

        if (textComponent != null)
        {
            textComponent.text = startingText;
        }
        else
        {
            Debug.LogWarning("Warning: No TextMeshProUGUI assigned or found for " + gameObject.name + ". Assign it in the Inspector or add one as a child of the Canvas.");
        }
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
        inConversation = true;
        Debug.Log("Agent activated: " + gameObject.name);

        if (myRenderer != null)
        {
            myRenderer.material.color = Color.green; // Colore di conversazione attiva
        }

        if (canvas != null && textComponent != null)
        {
            canvas.enabled = true;
            // All'inizio della conversazione, il testo di partenza è sempre lo stesso.
            // Se si sta aspettando una risposta, il testo verrà aggiornato da un'altra funzione.
            textComponent.text = imWaithingForResponse ? "still thinking..." : startingText;
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
        if (inConversation && !imWaithingForResponse && textComponent != null)
        {
            messageToSend = text; // sostituisce, non accumula
            textComponent.text = "you said: " + messageToSend.Trim();
        }
    }

    public void sendToBDI()
    {
        if (string.IsNullOrWhiteSpace(messageToSend))
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
            textComponent.text = startingText; // Resetta il testo visualizzato a quello iniziale
        }
    }
    #endregion
}