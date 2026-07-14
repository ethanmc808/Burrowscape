using UnityEngine;

public class BunnyTestMoverAndAnimator : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.75f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isMovingParameterName = "IsMoving";

    [Header("Facing Direction")]
    [SerializeField] private Transform bunnyScaleRoot;
    [SerializeField] private bool bunnyFacesLeftByDefault = true;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        float inputDirection = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) inputDirection = 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) inputDirection = -1f;

        Vector3 movement = new Vector3(inputDirection, 0f, 0f);
        transform.position += movement * moveSpeed * Time.deltaTime;

        bool isMoving = inputDirection != 0f;
        if (animator != null)
            animator.SetBool(isMovingParameterName, isMoving);

        if (isMoving)
            UpdateFacingDirection(inputDirection);
    }

    private void UpdateFacingDirection(float inputDirection)
    {
        if (bunnyScaleRoot == null) return;

        bool shouldFaceRight = inputDirection > 0f;
        bool flip = bunnyFacesLeftByDefault ? shouldFaceRight : !shouldFaceRight;

        Vector3 scale = bunnyScaleRoot.localScale;
        scale.x = flip ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        bunnyScaleRoot.localScale = scale;
    }
}