// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using UnityEngine;

public class AcousticsDemoSource : MonoBehaviour
{
    public TextMesh SourceText;
    public AudioClip[] Clips;
    public int BounceSpeed = 3;

    private AudioSource m_source;
    private int m_currentClip = 0;
    private Rigidbody m_rigidBody;
    private bool m_selected = false;

    // Use this for initialization
    void Start()
    {
        m_source = GetComponent<AudioSource>();
        m_rigidBody = GetComponent<Rigidbody>();

        m_currentClip = 0;
        m_source.clip = Clips[m_currentClip];
        if (m_source.playOnAwake)
        {
            PlayClip();
        }
        UpdateColor();
    }

    public void NextClip()
    {
        bool isPlaying = m_source.isPlaying;
        m_currentClip = (m_currentClip + 1) % Clips.Length;
        m_source.clip = Clips[m_currentClip];
        if (isPlaying)
        {
            PlayClip();
        }
        SetSourceUIText();
    }

    void PlayClip()
    {
        m_source.Play();
    }

    void SetSourceUIText()
    {
        SourceText.text = "Audio source: " + Clips[m_currentClip].name;
    }

    private void OnCollisionEnter(Collision collision)
    {
        Vector3 dir = collision.contacts[0].point - transform.position;
        // We then get the opposite (-Vector3) and normalize it
        dir = -dir.normalized;
        // And finally we add force in the direction of dir and multiply it by force. 
        // This will push back the player
        m_rigidBody.AddForce(dir * BounceSpeed, ForceMode.VelocityChange);
    }

    public void Select()
    {
        m_selected = true;
        UpdateColor();
        SetSourceUIText();
    }

    public void Deselect()
    {
        m_selected = false;
        UpdateColor();
    }

    public void PlayPause()
    {
        if (m_source.isPlaying)
        {
            m_source.Pause();
        }
        else
        {
            m_source.Play();
        }
        UpdateColor();
    }

    private void UpdateColor()
    {
        // Script startup ordering sometimes means m_source is null when we get here
        if (m_source != null)
        {
            if (m_selected)
            {
                if (m_source.isPlaying)
                {
                    GetComponent<MeshRenderer>().material.color = Color.blue;
                }
                else
                {
                    GetComponent<MeshRenderer>().material.color = new Color(0, 0.5f, 1.0f);
                }
            }
            else
            {
                if (m_source.isPlaying)
                {
                    GetComponent<MeshRenderer>().material.color = Color.green;
                }
                else
                {
                    GetComponent<MeshRenderer>().material.color = Color.red;
                }
            }
        }
    }
}
