using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BunnyMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 0.75f;
    [SerializeField] private float runSpeed = 1.5f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float lowJumpMultiplier = 2.5f;
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.05f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Transform ceilingCheck;
    [SerializeField] private float ceilingCheckRadius = 0.05f;
    [SerializeField] private float stuckTimeThreshold = 2f;
    private float airborneTimer = 0f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string isMovingParameterName = "IsMoving";
    [SerializeField] private string isRunningParameterName = "IsRunning";
    [SerializeField] private string isCrouchingParameterName = "IsCrouching";
    [SerializeField] private string isJumpingParameterName = "IsJumping";
    [SerializeField] private string isFallingParameterName = "IsFalling";
    [SerializeField] private string isLandingParameterName = "IsLanding";

    [Header("Facing Direction")]
    [SerializeField] private Transform bunnyScaleRoot;
    [SerializeField] private bool bunnyFacesLeftByDefault = true;

    [Header("Debug")]
    [SerializeField] private bool logPositionOnFlip = true;

    private Rigidbody rb;
    private bool facingRight;
    private bool hasInitializedFacing = false;
    private Vector3 moveInput;

    private bool isGrounded;
    private bool wasGrounded;
    private bool isCrouching;
    private bool jumpQueued;
    private bool jumpHeld;
    private bool isRunning;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.freezeRotation = true;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        SetFacing(!bunnyFacesLeftByDefault);
    }

    private void Update()
    {
        // Horizontal / depth input
        float x = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x = 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x = -1f;

        float z = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) z = -1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) z = 1f;

        moveInput = new Vector3(x, 0f, z);
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        isRunning = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && moveInput.sqrMagnitude > 0f;

        if (x != 0f)
            SetFacing(x > 0f);

        UpdateAnimatorParameters();

        // Jump input
        jumpHeld = Input.GetKey(KeyCode.Space);
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded && !isCrouching)
        {
            isCrouching = true;
            jumpQueued = true;
            Debug.Log("Space pressed - crouching triggered");
        }
    }

    private void FixedUpdate()
    {
        wasGrounded = isGrounded;
        isGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer);

        // ADD: ceiling check
        bool hitCeiling = Physics.CheckSphere(ceilingCheck.position, ceilingCheckRadius, groundLayer);
        if (hitCeiling && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        }

        if (!isGrounded)
        {
            airborneTimer += Time.fixedDeltaTime;
            if (airborneTimer > stuckTimeThreshold)
            {
                animator.SetTrigger("ForceReset");
                airborneTimer = 0f;
            }
        }
        else
        {
            airborneTimer = 0f;
        }

        float currentSpeed = isRunning ? runSpeed : moveSpeed;
        Vector3 horizontalVelocity = moveInput * currentSpeed;
        rb.linearVelocity = new Vector3(horizontalVelocity.x, rb.linearVelocity.y, horizontalVelocity.z);

        if (rb.linearVelocity.y > 0 && !jumpHeld)
        {
            rb.linearVelocity += Vector3.up * Physics.gravity.y * (lowJumpMultiplier - 1) * Time.fixedDeltaTime;
        }

        if (isGrounded && !wasGrounded)
        {
            OnLanded();
        }
    }

    // Call this from an Animation Event at the exact frame the crouch ends
    // and the bunny should leave the ground. If you don't want to deal with
    // Animation Events yet, it's already being called immediately in FixedUpdate above.
    public void PerformJump()
    {
        jumpQueued = false;
        isCrouching = false;
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
        Debug.Log("PerformJump called - jumpForce: " + jumpForce);
    }

    private void OnLanded()
    {
        animator.SetTrigger(isLandingParameterName);
        // Trigger landing animation state; landing animation itself
        // should transition back to Idle/Move on its own via the Animator
    }

    private void UpdateAnimatorParameters()
    {
        if (animator == null) return;

        animator.SetBool(isMovingParameterName, moveInput.sqrMagnitude > 0f && isGrounded);
        animator.SetBool(isRunningParameterName, isRunning && isGrounded);
        animator.SetBool(isCrouchingParameterName, isCrouching);
        animator.SetBool(isJumpingParameterName, !isGrounded && rb.linearVelocity.y > 0f);
        animator.SetBool(isFallingParameterName, !isGrounded && rb.linearVelocity.y <= 0f);
        animator.SetBool("IsGrounded", isGrounded);

    }

    private Vector3 GetVisualCenterWorld()
    {
        SpriteRenderer[] renderers = bunnyScaleRoot.GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0) return bunnyScaleRoot.position;

        Bounds bounds = renderers[0].bounds;
        foreach (SpriteRenderer r in renderers)
            bounds.Encapsulate(r.bounds);

        return bounds.center;
    }

    private void OnDrawGizmos()
    {
        if (groundCheck != null)
        {
            Gizmos.color = isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
        }

        if (ceilingCheck != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(ceilingCheck.position, ceilingCheckRadius);
        }
    }

    private void SetFacing(bool shouldFaceRight)
    {
        if (!hasInitializedFacing)
        {
            hasInitializedFacing = true;
        }
        else if (shouldFaceRight == facingRight)
        {
            return;
        }

        facingRight = shouldFaceRight;
        if (bunnyScaleRoot == null) return;

        Vector3 worldCenterBefore = GetVisualCenterWorld();

        bool flip = bunnyFacesLeftByDefault ? shouldFaceRight : !shouldFaceRight;
        Vector3 scale = bunnyScaleRoot.localScale;
        scale.x = flip ? -Mathf.Abs(scale.x) : Mathf.Abs(scale.x);
        bunnyScaleRoot.localScale = scale;

        Vector3 worldCenterAfter = GetVisualCenterWorld();
        Vector3 correction = worldCenterBefore - worldCenterAfter;
        correction.y = 0f;
        correction.z = 0f;

        bunnyScaleRoot.position += correction;

    }
}