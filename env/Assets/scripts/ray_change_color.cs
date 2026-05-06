using UnityEngine;

// È una buona pratica rinominare lo script in qualcosa che descriva cosa fa
// per esempio: AgentFeedbackGaze
public class ray_change_color : MonoBehaviour
{
    [SerializeField] private Color colorHighlight = Color.red; // Colore quando guardato
    private Color originalColor;
    private Renderer myRenderer;

    void Awake()
    {
        myRenderer = GetComponent<Renderer>();
        if (myRenderer != null)
        {
            originalColor = myRenderer.material.color;
        }
        else
        {
            Debug.LogError("Lo script change_color su " + gameObject.name + " richiede un componente Renderer!");
        }
    }

    // --- NUOVO METODO: Chiamato quando il visore VR guarda questo agente ---
    public void StartLookingAtMe()
    {
        if (myRenderer != null)
        {
            myRenderer.material.color = colorHighlight;
            // Esempio: Debug.Log(gameObject.name + ": Mi stanno guardando!");
        }
    }

    // --- NUOVO METODO: Chiamato quando il visore VR smette di guardare questo agente ---
    public void StopLookingAtMe()
    {
        if (myRenderer != null)
        {
            myRenderer.material.color = originalColor;
            // Esempio: Debug.Log(gameObject.name + ": Non mi guardano più.");
        }
    }
}