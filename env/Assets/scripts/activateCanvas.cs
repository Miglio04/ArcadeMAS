using UnityEngine;

public class TriggerCanvas : MonoBehaviour
{
    [SerializeField] private GameObject canvasObject;

    void Start()
    {
        if (canvasObject == null)
        {
            Debug.LogError("Canvas Object is not assigned in the inspector.");
            return;
        }

        canvasObject.SetActive(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            canvasObject.SetActive(true);
            Debug.Log("Player entered the trigger!");
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            canvasObject.SetActive(false);
            Debug.Log("Player exited the trigger!");
        }
    }
}