using UnityAtoms.BaseAtoms;
using UnityEngine;

public class OneButtonInputEvent : MonoBehaviour
{
    [SerializeField] private VoidEvent inputEvent;


    private void Update()
    {

        if (!Input.anyKeyDown) return;

        // Let our event know where we clicked
        inputEvent.Raise();
    }
}