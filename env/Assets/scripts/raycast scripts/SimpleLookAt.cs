using UnityEngine;

public class SimpleLookAt : MonoBehaviour
{
    [Tooltip("How fast the object turns to face the player")]
    public float rotationSpeed = 5f;

    private Transform mainCameraTransform;

    void Start()
    {
        if (Camera.main != null)
        {
            mainCameraTransform = Camera.main.transform;
        }
    }

    void Update()
    {
        if (mainCameraTransform != null)
        {
            Vector3 directionAwayFromCamera = transform.position - mainCameraTransform.position;
            
            directionAwayFromCamera.y = 0; 

            if (directionAwayFromCamera != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionAwayFromCamera);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            }
        }
    }
}