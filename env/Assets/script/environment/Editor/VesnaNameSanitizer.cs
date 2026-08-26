#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class VesnaNameSanitizer
{
    [MenuItem("VESNA/Sanitizza nomi porte")]
    static void Sanitize()
    {
        int n = 0;
        foreach (var d in Object.FindObjectsOfType<DoorVarcoScript>(true))
        {
            if (Clean(d)) n++;
        }
        Debug.Log($"[Sanitizer] Rinominate {n} porte/varchi.");
    }

    [MenuItem("VESNA/Sanitizza nomi oggetti")]
    static void SanitizeObjects()
    {
        int n = 0;
        foreach (var a in Object.FindObjectsOfType<Artifact>(true))
        {
            if (Clean(a)) n++;
        }
        Debug.Log($"[Sanitizer] Rinominati {n} oggetti/artefatti.");
    }

    static bool Clean(MonoBehaviour a)
    {
        string clean = a.gameObject.name.Replace(' ', '_').Replace('-', '_');
        if (clean != a.gameObject.name)
        {
            Undo.RecordObject(a.gameObject, "Sanitize object name");
            a.gameObject.name = clean;
            return true;
        }

        return false;
    }
}
#endif