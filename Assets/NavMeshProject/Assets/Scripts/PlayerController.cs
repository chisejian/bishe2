using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    private Animator anim;

    private GameObject cameraObject;
    
    public float forwardSpeed = 7.0f;
    public float runSpeed = 20f;
    public float rotateSpeed = 2.0f;
    
    public float rotationSpeed = 2.0f;

    public float jumpSpeed = 2.0f;
    
    private Rigidbody playerRigidbody;
    

    public Vector3 cameraOffset;
    
    // Start is called before the first frame update
    void Start()
    {
        anim = GetComponent<Animator>();
       
        cameraObject = GameObject.FindWithTag("MainCamera");
        playerRigidbody = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        float h = Input.GetAxis("Horizontal");				
        float v = Input.GetAxis("Vertical");				
        anim.SetFloat("Speed", v);							
        //anim.SetFloat("Direction", h); 						


        var isRun = false;
        if (Input.GetKey(KeyCode.LeftShift))
        {
            isRun = true;
            anim.SetBool("Run", true);
        }
        else
        {
            anim.SetBool("Run", false);
        }
        
		
        var velocity = new Vector3(0, 0, v);
        print(v);
        velocity = transform.TransformDirection(velocity);
        if (v > 0.1) {
            velocity *= (isRun ? runSpeed : forwardSpeed);	
        } 
        
        transform.localPosition += velocity * Time.fixedDeltaTime;
        transform.Rotate(0, h * rotateSpeed, 0);
        

        if (Input.GetKeyDown(KeyCode.Space))
        {
            playerRigidbody.linearVelocity = Vector3.up * jumpSpeed;
            anim.SetTrigger("Jump");
        }
        if (Input.GetKeyDown(KeyCode.Q))
        {
            anim.SetTrigger("Atk2");
        }
        if (Input.GetKeyDown(KeyCode.T))
        {
            anim.SetTrigger("StopAtk2");
        }
        if (Input.GetMouseButtonDown(0))
        {
            anim.SetTrigger("Atk1");
        }
    }

    private void FixedUpdate()
    {
       // var position = this.transform.position;
       // cameraObject.transform.position = position + cameraOffset;
       // cameraObject.transform.rotation = transform.rotation;
       // 检测右键点击
       if (Input.GetMouseButton(1))
       {
           // 获取鼠标移动的水平和垂直轴
           float mouseX = Input.GetAxis("Mouse X");
           float mouseY = Input.GetAxis("Mouse Y");

           // 旋转摄像机
           cameraObject.transform.Rotate(Vector3.up * (mouseX * rotationSpeed), Space.World);
           cameraObject.transform.Rotate(Vector3.left * (mouseY * rotationSpeed), Space.Self);
       }
    }
}
