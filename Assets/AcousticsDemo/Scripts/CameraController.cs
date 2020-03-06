// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Assets.AcousticsDemo.Scripts;
using UnityEngine;

[DisallowMultipleComponent]
public class CameraController : MonoBehaviour
{
    public float MovementSpeed = 8.0f;
    public float RotationSpeed = 3.0f;

    Vector2 currentRotation = new Vector2(0.0f, 0.0f);

    private void Start()
    {
        // Prevent bounces when walking into walls
        GetComponentInParent<Rigidbody>().maxAngularVelocity = 0;
    }

    void Update ()
    {
        if (!AcousticsDemoControls.IsGameFocused())
        {
            transform.parent.transform.rotation = Quaternion.AngleAxis(currentRotation.x, Vector3.up);
            transform.parent.transform.rotation *= Quaternion.AngleAxis(currentRotation.y, Vector3.left);
            return;
        }
        // Only allow mouse rotation on desktop
#if !UNITY_ANDROID
        // Mouse-based rotation
        currentRotation.x += Input.GetAxis("Mouse X") * RotationSpeed;
        currentRotation.y += Input.GetAxis("Mouse Y") * RotationSpeed;

        currentRotation.y = Mathf.Clamp(currentRotation.y, -90, 90);
#endif
        transform.parent.transform.rotation = Quaternion.AngleAxis(currentRotation.x, Vector3.up);
        transform.parent.transform.rotation *= Quaternion.AngleAxis(currentRotation.y, Vector3.left);

        var verticalAxis = Input.GetAxis("Vertical");
        var horizontalAxis = Input.GetAxis("Horizontal");

        var jumping = Input.GetButtonDown("Jump");
        var position = transform.parent.transform.position;
        if (jumping)
        {
            GetComponentInParent<Rigidbody>().velocity = new Vector3(0, 15, 0);
        }
        float runSpeed = Input.GetKey(KeyCode.LeftShift) ? 2 : 1;

        // Eliminate any vertical offset before moving in that direction
        var forward = transform.forward;
        forward.y = 0;
        position += forward * verticalAxis * MovementSpeed * Time.deltaTime * runSpeed;
        position += transform.right * horizontalAxis * MovementSpeed * Time.deltaTime * runSpeed;
        
        // Assign to the cameraholder
        transform.parent.transform.position = new Vector3(position.x, position.y, position.z);
    }
    
    public void SetRotation(Vector3 newRotation)
    {
        // currentRotation is based on the mouse perspective
        // However, Mouse-x actually rotates about the y axis, so x and y are flipped
        currentRotation.x = newRotation.y;
        currentRotation.y = newRotation.x;
    }
}
