using UnityEngine;

public class BunnyKeyboardTestMover : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2f;

    private void Update()
    {
        Vector3 movement = Vector3.zero;

        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
        {
            movement.x += 1f;
        }

        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
        {
            movement.x -= 1f; 
        }

        transform.position += movement * moveSpeed * Time.deltaTime;
    }
}