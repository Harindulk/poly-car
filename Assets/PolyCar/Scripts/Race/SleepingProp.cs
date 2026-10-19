using UnityEngine;

namespace PolyCar
{
    /// <summary>
    /// Roadside props (marker poles, hay bales) start asleep, like art of rally's markers that sleep until hit:
    /// no physics cost until a car touches them, and a prop resting against a slope doesn't jump at the start.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SleepingProp : MonoBehaviour
    {
        private void Start() => GetComponent<Rigidbody>().Sleep();
    }
}
