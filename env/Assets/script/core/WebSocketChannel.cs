using System;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

public class WebSocketChannel
{
    private WebSocketServer wss;
    private WSConnectionInfoModel connectionInfo;
    public WSConnectionInfoModel ConnectionInfoModel => connectionInfo;

    private EventHandler<MessageEventArgs> onMessageHandler;

    public WebSocketChannel(WSConnectionInfoModel connectionInfo)
    {
        this.connectionInfo = connectionInfo;
        string url = connectionInfo.getUrl(); // Es: "ws://localhost:8080"
        Uri uri = new Uri(url);
        wss = new WebSocketServer(uri.Port);
    }

    public WebSocketChannel( WSConnectionInfoModel connectionInfo, System.EventHandler<WebSocketSharp.MessageEventArgs> onMessage ) {
        this.connectionInfo = connectionInfo;
        this.onMessageHandler = onMessage;
        string url = connectionInfo.getUrl(); // Es: "ws://localhost:8080"
        Uri uri = new Uri(url);
        wss = new WebSocketServer(uri.Port);
    }

    public bool IsServerRunning { get; private set; } = false;

    public bool HasConnectedClients =>
        IsServerRunning &&
        wss.WebSocketServices["/"].Sessions.Count > 0;

    public void StartServer()
    {
        wss.KeepClean = false; // ← aggiunge questa riga
        wss.AddWebSocketService<CustomWebSocketBehavior>("/", () => new CustomWebSocketBehavior( onMessageHandler ) );
        wss.Start();
        IsServerRunning = true;
        Debug.Log($"WebSocket server started on port {wss.Port}");
    }

    public void StopServer()
    {
        if (IsServerRunning)
        {
            wss.Stop();
            IsServerRunning = false;
            Debug.Log("WebSocket server stopped.");
        }
    }

    // 🔸 Metodo per inviare messaggio a tutti i client connessi
    public void sendMessage(string message)
    {
        Debug.Log("Sending message to all clients: " + message);

        // Itera tra tutti i comportamenti (client connessi)
        wss.WebSocketServices["/"].Sessions.Broadcast(message);
    }

    public class CustomWebSocketBehavior : WebSocketBehavior
    {

    	private EventHandler<MessageEventArgs> onMessageHandler;

     	public CustomWebSocketBehavior( EventHandler<MessageEventArgs> onMessageHandler)
     	{
     		this.onMessageHandler = onMessageHandler;
     	}

      protected override void OnMessage(MessageEventArgs e)
{
    Debug.Log("Server received message: " + e.Data);
    Debug.Log($"[WS] handler null: {onMessageHandler == null}");
    try
    {
        onMessageHandler?.Invoke(this, e);
    }
    catch (Exception ex)
    {
        Debug.Log($"[WS] ECCEZIONE in handler: {ex.Message}\n{ex.StackTrace}");
    }
}

        protected override void OnOpen()
        {
            Debug.Log("Client connected.");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            Debug.Log($"Client disconnected with reason: {e.Reason}");  
        }

        protected override void OnError(ErrorEventArgs e)
        {
            Debug.LogError("WebSocket Server Error: " + e.Message);
        }
    }
}
