
using Newtonsoft.Json;
using System;
using UnityEngine;

public class ArtsData {

    [JsonProperty("names")]
    public string[] Names { get; set; }
    public string[] RoomNodes { get; set; }

    public ArtsData( string[] names ) {
        Names = names;
    }
    
    public ArtsData( string[] names, string[] roomNodes ) {
        Names = names;
        RoomNodes = roomNodes;
    }
}