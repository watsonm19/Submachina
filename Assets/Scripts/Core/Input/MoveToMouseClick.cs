using UnityAtoms.BaseAtoms;
using UnityEngine;

public class MoveToMouseClick : MonoBehaviour
{
    [SerializeField] private Vector3Event clickEvent;
    [SerializeField] private Camera targetCamera;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    private void Update()
    {
        if (targetCamera == null) return;

        if (!Input.GetMouseButtonDown(0)) return;

        Vector3 screenPosition = Input.mousePosition;
        Vector3 worldPosition = targetCamera.ScreenToWorldPoint(screenPosition);

        // Let our event know where we clicked
        clickEvent.Raise(worldPosition);
    }
}