using System.Collections;
using UnityEngine;

namespace script.environment
{
    public abstract class PlacesPositionFinder: MonoBehaviour
    {
        public abstract bool HasPlace(string placeName);

        public abstract bool TryGetPlacePosition(string placeName, out Vector3 position);
    }
}