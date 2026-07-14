using UnityEngine;

public class BunnyMovementAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float movementThreshold = 0.001f;

    private Vector3 lastPosition;

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 movementThisFrame = transform.position - lastPosition;
        bool isMoving = movementThisFrame.sqrMagnitude > movementThreshold;

        if (animator != null)
        {
            animator.SetBool("IsMoving", isMoving);
        }

        lastPosition = transform.position;
    }
}