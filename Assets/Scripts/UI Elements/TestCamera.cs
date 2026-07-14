using UnityEngine;

public class TestCamera : MonoBehaviour
{
    [SerializeField] private float dragSpeed = 0.05f;
    [SerializeField] private float zoomSpeed = 10f;

    private Vector3 lastMousePosition;

    private void Update()
    {
        // Middle mouse button drag to pan
        if (Input.GetMouseButtonDown(2))
        {
            lastMousePosition = Input.mousePosition;
        }
        else if (Input.GetMouseButton(2))
        {
            Vector3 delta = Input.mousePosition - lastMousePosition;

            // Convert screen-space mouse movement into world-space camera movement
            Vector3 move = new Vector3(-delta.x, 0f, -delta.y) * dragSpeed;
            transform.position += move;

            lastMousePosition = Input.mousePosition;
        }

        // Scroll wheel zoom (orthographic camera)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        Camera cam = GetComponent<Camera>();
        if (cam != null && cam.orthographic && scroll != 0f)
        {
            cam.orthographicSize -= scroll * zoomSpeed;
            cam.orthographicSize = Mathf.Max(1f, cam.orthographicSize);
        }
    }
}