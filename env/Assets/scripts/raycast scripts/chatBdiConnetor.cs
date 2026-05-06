using UnityEngine;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;

public class chatBdiConnetor : MonoBehaviour
{
    #region Settings & References
    [Header("Network Settings")]
    [Tooltip("The IP address of the Java ChatBDI Server")]
    [SerializeField] private string serverIP = "127.0.0.1";
    
    [Tooltip("The port used for the TCP connection")]
    [SerializeField] private int serverPort = 8080;
    #endregion

    #region Internal State Variables
    private TcpClient client;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread clientThread;
    private ConcurrentQueue<string> messageQueue;
    #endregion

    #region Unity Lifecycle (Start, Update, OnApplicationQuit)
    void Start()
    {
        messageQueue = new ConcurrentQueue<string>();
        ConnectToServer();
    }

    void Update()
    {
        if (messageQueue.TryDequeue(out string currentMessage))
        {
            Debug.Log("Received from server: " + currentMessage);
            string agent = currentMessage.Split('|')[0];
            string message = currentMessage.Split('|')[1];
            GameObject agentObj = GameObject.Find(agent);
            
            if (agentObj != null)
            {
                AgentScript agentScript = agentObj.GetComponent<AgentScript>();
                if (agentScript != null)
                {
                    agentScript.updateTextFromBDI(message);
                }
            }
        }
    }

    void OnApplicationQuit()
    {
        if (clientThread != null && clientThread.IsAlive)
        {
            clientThread.Abort();
        }
        if (writer != null)
        {
            writer.Close();
        }
        if (reader != null)
        {
            reader.Close();
        }
        if (client != null)
        {
            client.Close();
        }
    }
    #endregion

    #region Core Network Logic
    private void ConnectToServer()
    {
        try
        {
            Debug.Log("Connecting to server at " + serverIP + ":" + serverPort);
            client = new TcpClient(serverIP, serverPort);
            reader = new StreamReader(client.GetStream());
            writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

            clientThread = new Thread(ReceiveMessages);
            clientThread.IsBackground = true;
            clientThread.Start();
        }
        catch (SocketException ex)
        {
            Debug.LogError("SocketException: " + ex.Message);
        }
    }

    private void ReceiveMessages()
    {
        try
        {
            while (client != null && client.Connected)
            {
                string recivedMessage = reader.ReadLine();
                if (recivedMessage != null)
                {
                    messageQueue.Enqueue(recivedMessage);
                    Debug.Log("Message received: " + recivedMessage);
                }
            }
        }
        catch (IOException ex)
        {
            Debug.LogError("interrupted reading: " + ex.Message);
        }
    }

    public void SendMessageToServer(string agent, string message)
    {
        if (writer != null)
        {
            writer.WriteLine(agent + "|" + message);
            Debug.Log("Message sent to server: " + agent + "|" + message);
        }
    }
    #endregion
}