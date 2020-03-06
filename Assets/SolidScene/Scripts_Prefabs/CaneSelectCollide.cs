//using HTC.UnityPlugin.Vive;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
//using Valve.VR;
//using Valve.VR.InteractionSystem;

public class CaneSelectCollide : MonoBehaviour
{
    public AudioClip beton, fußboden, glas, holz, heizung, Geländer, fußboden_streichen;
    public bool wasPlayed;

   

    //public Hand hand;
    private string material;

    void Start()
    {
        
        print("CaneSelectCollide gestartet");
    }

    void Update()
    {
       
    }


   

    private void OnTriggerEnter(Collider other)
    {
           

            Debug.Log("Kollision mit " + other.GetComponent<Renderer>().material.name.ToString() + ", aus " + other.material + " und tag " + other.tag);
            material = other.GetComponent<Renderer>().material.name.ToString();
            print(material);
            
            if(material.Contains("Concrete") == true)
            {
                print("Concrete");
                
                    GetComponent<AudioSource>().clip = beton; GetComponent<AudioSource>().Play(); wasPlayed = true;
                
            
            }

            else if (material.Contains("Door") == true)
            {      
                GetComponent<AudioSource>().clip = holz; GetComponent<AudioSource>().Play(); wasPlayed = true;
            }

            else if (material.Contains("Heizung") == true)
            {

                GetComponent<AudioSource>().clip = heizung; GetComponent<AudioSource>().Play(); wasPlayed = true;

            }

            else if (material.Contains("Fußboden") == true)
            {
               

                GetComponent<AudioSource>().clip = fußboden; GetComponent<AudioSource>().Play(); wasPlayed = true;

            }

            else if (material.Contains("Glas") == true)
            {

                GetComponent<AudioSource>().clip = glas; GetComponent<AudioSource>().Play(); wasPlayed = true;

            }

            else if (material.Contains("Geländer") == true)
            {

                GetComponent<AudioSource>().clip = Geländer; GetComponent<AudioSource>().Play(); wasPlayed = true;

            }
                    
           
    
        else if (wasPlayed == true)
        {
            gameObject.SetActive(false);
            gameObject.SetActive(true);
            wasPlayed = false;
        }
    }


    private void OnTriggerExit(Collider other)
    {
        
    }
}