// Define an interface named IDamageable
using System;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

public abstract class AbstractMasElement : MonoBehaviour
{
    public GameObject objInUse;
    public string port;
    protected WebSocketChannel wsChannel;
    public WebSocketChannel WsChannel => wsChannel;
    public string Port => port;

    protected void initializeWebSocketConnection(System.EventHandler<WebSocketSharp.MessageEventArgs> OnMessage)
    {
        Debug.Log($"[AbstractMasElement] initializeWebSocketConnection chiamato, handler null: {OnMessage == null}, tipo: {this.GetType().Name}");
        // Initialize new web socket connection
        string url = "ws://localhost:" + port;
        WSConnectionInfoModel wSConnectionInfoModel = new WSConnectionInfoModel(url, "AGENT", objInUse.name);
        wsChannel = new WebSocketChannel(wSConnectionInfoModel, OnMessage);
    }

    public async Task startServer() {
    if (wsChannel == null) return;  // non inizializzato, salta
    if (wsChannel.IsServerRunning) return;  // già aperto, salta
    await Task.Run(() =>
    {
        wsChannel.StartServer();
        return Task.CompletedTask;
    });
}
    
    public string convertObjectIntoJson<T>(T objToConvert)
    {
        // Convert any object to JSON
        return JsonConvert.SerializeObject(objToConvert);
    }

    public string EscapeJson(string json)
    {
        // Escape double quotes and backslashes
        return json.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}