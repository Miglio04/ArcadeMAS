using UnityEngine;

public class length_setter : MonoBehaviour
{
    [SerializeField] private float length;
    void Update()
    {
        Vector3 newScale = transform.localScale;
        Vector3 newPosition = transform.localPosition;

        newScale.y = length;
        transform.localScale = newScale;
        newPosition.z = length;
        transform.localPosition = newPosition;
    }
}
