using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[AddComponentMenu("Camera/First Person Camera Controller")]
[RequireComponent(typeof(Camera))]
public class SimpleFirstPersonCamera : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float sprintMultiplier = 2f;
    public float mouseSensitivity = 0.2f;

    [Header("Camera Settings")]
    public float minPitch = -90f;
    public float maxPitch = 90f;

    private float yaw = 0f;
    private float pitch = 0f;

    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool sprintInput;
    private bool upInput;
    private bool downInput;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Vector3 euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    void Update()
    {
        ReadInput();
        HandleMouseLook();
        HandleMovement();
    }

    void ReadInput()
    {
#if ENABLE_INPUT_SYSTEM
        // Use new Input System if available
        if (Keyboard.current == null || Mouse.current == null)
            return;

        moveInput = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) moveInput.y += 1;
        if (Keyboard.current.sKey.isPressed) moveInput.y -= 1;
        if (Keyboard.current.dKey.isPressed) moveInput.x += 1;
        if (Keyboard.current.aKey.isPressed) moveInput.x -= 1;

        sprintInput = Keyboard.current.leftShiftKey.isPressed;
        upInput = Keyboard.current.spaceKey.isPressed;
        downInput = Keyboard.current.leftCtrlKey.isPressed;

        lookInput = Mouse.current.delta.ReadValue() * mouseSensitivity;
#else
        // Fallback to old Input Manager
        moveInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        sprintInput = Input.GetKey(KeyCode.LeftShift);
        upInput = Input.GetKey(KeyCode.Space);
        downInput = Input.GetKey(KeyCode.LeftControl);

        lookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * mouseSensitivity;
#endif
    }

    void HandleMouseLook()
    {
        yaw += lookInput.x;
        pitch -= lookInput.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void HandleMovement()
    {
        float speed = sprintInput ? moveSpeed * sprintMultiplier : moveSpeed;

        Vector3 move = transform.right * moveInput.x + transform.forward * moveInput.y;
        transform.position += move * speed * Time.deltaTime;

        if (upInput)
            transform.position += Vector3.up * speed * Time.deltaTime;

        if (downInput)
            transform.position += Vector3.down * speed * Time.deltaTime;
    }
}
