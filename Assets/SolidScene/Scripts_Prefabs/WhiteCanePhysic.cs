using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WhiteCanePhysic : MonoBehaviour
{
    // Start is called before the first frame update

    public Rigidbody rb;
    public GameObject clipping_cane;
    private bool isHit = false;
    private Vector3 last_position;
    private Vector3 new_position;


    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Update is called once per frame
    void Update()
    {
        rb.velocity = new Vector3(0, 0, 0);
        //Debug.DrawRay(transform.position, transform.up, Color.green);
      //  Debug.Log("Cane position" + this.transform.position);
        
    }


    private void OnTriggerEnter(Collider other)
    {
        
        /*
        RaycastHit hit;
        if(Physics.Raycast(transform.position, transform.up, out hit))
        {
            Debug.DrawLine(transform.position, hit.point, Color.red);
            Debug.Log("Point of Hit: " + hit.point);
           
            
        }
        */
        
    }

    private void OnTriggerExit(Collider other)
    {
        
        isHit = false;
    }

    private void OnTriggerStay(Collider other)
    {
        RaycastHit hit;

        if (Physics.Raycast(transform.position, transform.up, out hit))
        {
            Debug.DrawRay(transform.position, hit.point, Color.red);
            Debug.Log("Point of Hit: " + hit.transform);


        }
    }
}
