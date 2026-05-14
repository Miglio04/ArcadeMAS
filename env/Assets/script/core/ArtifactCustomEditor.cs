using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

[CustomEditor(typeof(Artifact))]
public class ArtifactCustomEditor : Editor
{
    private readonly string javaFolderPath = Path.Combine(Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName, "mind", "src", "env", "artifact");

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        Artifact myArtifact = (Artifact)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Interazione Dinamica JaCaMo", EditorStyles.boldLabel);

        string artifactTypeString = myArtifact.GetArtifactTypeName();
        string javaClassName = artifactTypeString + "Artifact";

        string[] extractedActions = ExtractFunctionsFromJava(javaClassName);

        if (extractedActions.Length > 0)
        {
            List<string> optionsList = new List<string> { "None" };
            optionsList.AddRange(extractedActions);
            string[] availableActions = optionsList.ToArray();

            EditorGUILayout.Space();

            int primaryIndex = Mathf.Max(0, System.Array.IndexOf(availableActions, myArtifact.primaryActionToTrigger));
            primaryIndex = EditorGUILayout.Popup("Azione Primaria", primaryIndex, availableActions);
            myArtifact.primaryActionToTrigger = availableActions[primaryIndex];

            int secondaryIndex = Mathf.Max(0, System.Array.IndexOf(availableActions, myArtifact.secondaryActionToTrigger));
            secondaryIndex = EditorGUILayout.Popup("Azione Secondaria", secondaryIndex, availableActions);
            myArtifact.secondaryActionToTrigger = availableActions[secondaryIndex];

            int tertiaryIndex = Mathf.Max(0, System.Array.IndexOf(availableActions, myArtifact.tertiaryActionToTrigger));
            tertiaryIndex = EditorGUILayout.Popup("Azione Terziaria", tertiaryIndex, availableActions);
            myArtifact.tertiaryActionToTrigger = availableActions[tertiaryIndex];
        }
        else
        {
            EditorGUILayout.HelpBox($"Impossibile trovare funzioni. Controlla che il file {javaClassName}.java esista nel percorso: {javaFolderPath} e contenga metodi public/@OPERATION.", MessageType.Warning);
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(myArtifact);
        }
    }

    private string[] ExtractFunctionsFromJava(string javaClassName)
    {
        string fullPath = Path.Combine(javaFolderPath, javaClassName + ".java");

        if (!File.Exists(fullPath))
        {
            return new string[0];
        }

        List<string> foundFunctions = new List<string>();

        string fileContent = File.ReadAllText(fullPath);

        Regex functionRegex = new Regex(@"@(?:INTERNAL_)?OPERATION\s+public\s+void\s+([a-zA-Z0-9_]+)\s*\(");

        MatchCollection matches = functionRegex.Matches(fileContent);

        foreach (Match match in matches)
        {
            string functionName = match.Groups[1].Value;

            if (functionName != "init")
            {
                if (!foundFunctions.Contains(functionName))
                {
                    foundFunctions.Add(functionName);
                }
            }
        }

        return foundFunctions.ToArray();
    }
}