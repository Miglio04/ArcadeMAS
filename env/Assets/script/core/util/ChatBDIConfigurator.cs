using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Questo script è un esempio di come esporre campi dati nell'Inspector di Unity
/// per poterli configurare direttamente dall'editor.
/// </summary>
public class ChatBDIConfigurator : MonoBehaviour
{
    [Header("Generation Parameters")]
    [Tooltip("The provider for the generation model (e.g., 'openai', 'groq').")]
    [SerializeField]
    private string gen_provider;

    [Tooltip("The specific model to use for generation (e.g., 'llama-3.3-70b-versatile').")]
    [SerializeField]
    private string gen_model;

    [Tooltip("The API key for the generation service.")]
    [SerializeField]
    private string gen_key;

    [Tooltip("The base URL for the generation API endpoint.")]
    [SerializeField]
    private string gen_url;

    [Header("Embedding Parameters")]
    [Tooltip("The provider for the embedding model (e.g., 'ollama').")]
    [SerializeField]
    private string emb_provider;

    [Tooltip("The specific model to use for embeddings (e.g., 'all-minilm').")]
    [SerializeField]
    private string emb_model;

    [Tooltip("The URL for the embedding API endpoint.")]
    [SerializeField]
    private string emb_url;

    public string GenProvider => gen_provider;
    public string GenModel => gen_model;
    public string GenKey => gen_key;
    public string GenUrl => gen_url;

    public string EmbProvider => emb_provider;
    public string EmbModel => emb_model;
    public string EmbUrl => emb_url;

    public string JcmUserAgentConfiguration
    {
        get
        {
            return $@"	agent user : conversation.asl {{
			ag-arch: chatbdi.Interpreter

			// Percorsi allineati con la tua struttura directory attuale
			nl2log_prompt : ""src/agt/chatbdi/modelfiles/nl2logPrompt.txt""
			log2nl_prompt : ""src/agt/chatbdi/modelfiles/log2nlPrompt.txt""
			nl2log_model  : ""src/agt/chatbdi/modelfiles/nl2log.txt""
			log2nl_model  : ""src/agt/chatbdi/modelfiles/log2nl.txt""
			class_model   : ""src/agt/chatbdi/modelfiles/classifier.txt""

			// PARAMETRI DI GENERAZIONE
			gen_provider  : ""{GenProvider}""
			gen_model     : ""{GenModel}""
			gen_key       : ""{GenKey}""
			gen_url       : ""{GenUrl}""

			// PARAMETRI EMBEDDING
			emb_provider  : ""{EmbProvider}""
			emb_model     : ""{EmbModel}""
			emb_url       : ""{EmbUrl}""
		}}";
        }
    }
}
