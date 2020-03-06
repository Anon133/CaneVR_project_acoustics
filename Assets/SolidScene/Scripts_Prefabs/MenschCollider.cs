using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MenschCollider : MonoBehaviour
{

    public AudioClip Kollision;
    public GameObject player;
    private string current_object;
    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("MenschCollider gestartet!\n");
    }

    // Update is called once per frame
    void Update()
    {

    }
   
    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("Kollision des Menschen mit " + other.GetComponent<Renderer>().material.name.ToString() + ", aus " + other.material + " und tag " + other.tag);
        current_object = other.GetComponent<Renderer>().material.name.ToString();

        if (current_object.Contains("Player"))
        {
            GetComponent<AudioSource>().clip = Kollision; GetComponent<AudioSource>().Play();
        }



    }

}

