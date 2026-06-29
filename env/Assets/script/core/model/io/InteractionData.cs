using Newtonsoft.Json;
using System;
using UnityEngine;

public class InteractionData {

    [JsonProperty("sender")]
    public string Sender { get; set; }

    public InteractionData( string sender ) {
        Sender = sender;
    }
}