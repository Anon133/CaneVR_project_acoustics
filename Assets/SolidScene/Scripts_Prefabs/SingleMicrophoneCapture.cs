using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]

public class SingleMicrophoneCapture : MonoBehaviour
{
    //A boolean that flags whether there's a connected microphone
    private bool micConnected = false;

    //The maximum and minimum available recording frequencies
    private int minFreq;
    private int maxFreq;

    //A handle to the attached AudioSource
    private AudioSource goAudioSource;

    //Use this for initialization
    void Start()
    {
        AudioSource audio = GetComponent<AudioSource>();

        if (Microphone.devices.Length <= 0)
        {
            Debug.LogWarning("Microphone not connected!");
        }
        else
        {
            if (minFreq == 0 && maxFreq == 0)
            {
                //...meaning 44100 Hz can be used as the recording sampling rate
                maxFreq = 44100;
            }
            audio.clip = Microphone.Start(null, true, 1, 22050);
            audio.loop = true;
        }
       
        
        while (!(Microphone.GetPosition(null) > 0))
        {
            Debug.Log("start playing... position is " + Microphone.GetPosition(null));
            audio.Play();
        }
        
    }

}