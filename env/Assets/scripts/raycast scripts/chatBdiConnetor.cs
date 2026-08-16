using UnityEngine;
using System.Collections.Concurrent;
using Newtonsoft.Json;
using WebSocketSharp;

public class chatBdiConnetor : MonoBehaviour
{
    #region Settings & References
    [Header("Network Settings")]
    [Tooltip("The address used by the shared WebSocket channel")]
    [SerializeField] private string serverIP = "127.0.0.1";
    
    [Tooltip("The port used by the shared WebSocket channel")]
    [SerializeField] private int serverPort = 8080;
    #endregion

    #region Internal State Variables
    private WebSocketChannel webSocketChannel;
    private ConcurrentQueue<string> messageQueue;
    #endregion

    #region Unity Lifecycle (Start, Update, OnApplicationQuit)
    void Start()
    {
        messageQueue = new ConcurrentQueue<string>();
        var connectionInfo = new WSConnectionInfoModel(
            $"ws://{serverIP}:{serverPort}", "CHATBDI", gameObject.name);
        webSocketChannel = new WebSocketChannel(connectionInfo, OnWebSocketMessage);
        try
        {
            webSocketChannel.StartServer();
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Unable to start ChatBDI WebSocket channel: " + ex.Message);
        }
    }

    void Update()
    {
        if (messageQueue.TryDequeue(out string currentMessage))
        {
            Debug.Log("Received from server: " + currentMessage);
            ArtifactMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<ArtifactMessage>(currentMessage);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Malformed message from ChatBDI: " + ex.Message);
                return;
            }

            if (message == null || message.MessageType != "chatResponse")
                return;

            string agent = message.AgentName;
            GameObject agentObj = GameObject.Find(agent);
            
            if (agentObj != null)
            {
                AgentScript agentScript = agentObj.GetComponent<AgentScript>();
                if (agentScript != null)
                {
                    agentScript.updateTextFromBDI(message.MessagePayload);
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        if (webSocketChannel != null)
            webSocketChannel.StopServer();
    }
    #endregion

    #region Core Network Logic
    private void OnWebSocketMessage(object sender, MessageEventArgs args)
    {
        if (args != null && !string.IsNullOrWhiteSpace(args.Data))
            messageQueue.Enqueue(args.Data);
    }

    public void SendMessageToServer(string agent, string message)
    {
        if (webSocketChannel == null || string.IsNullOrWhiteSpace(message))
            return;

        var chatMessage = new ArtifactMessage("chatMessage", message, null, agent, null);
        webSocketChannel.sendMessage(JsonConvert.SerializeObject(chatMessage));
        Debug.Log("Message sent to ChatBDI over WebSocket: " + agent);
    }
    #endregion
}
