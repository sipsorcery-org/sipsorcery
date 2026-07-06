using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class MotionAudioController : MonoBehaviour
{
    [SerializeField] private Transform targetTransform;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Vector3 lastPosition;

    [SerializeField, Tooltip("Threshold for movement detection. Adjust as needed.")]
    private float movementThreshold = 0.0001f;
    
    [SerializeField, Tooltip("Duration of the fade-out effect in seconds")]
    private float fadeOutDuration = 0.5f;

    private bool wasMoving = false;
    private Coroutine fadeCoroutine;

    void Start()
    {
        // Get the AudioSource attached to this GameObject
        audioSource = GetComponent<AudioSource>();

        // Find the Animator in sibling or child objects
        animator = GetComponentInParent<Animator>();

        if (animator != null)
        {
            targetTransform = animator.transform;
        }
        else
        {
            Debug.LogError("No Animator component found in parent or its children.");
        }

        // Initialize last position
        if (targetTransform != null)
        {
            lastPosition = targetTransform.position;
        }
    }

    void Update()
    {
        if (targetTransform == null)
        {
            return;
        }

        // Check if the object has moved significantly
        float movement = Vector3.Distance(targetTransform.position, lastPosition);
        bool isMoving = movement > movementThreshold;

        if (isMoving && !wasMoving)
        {
            // If there's a fade-out in progress, stop it
            if (fadeCoroutine != null)
            {
                StopCoroutine(fadeCoroutine);
                fadeCoroutine = null;
            }
            
            // Reset volume to full
            audioSource.volume = 1f;
            
            // Start playing audio only when movement starts
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }
        else if (!isMoving && wasMoving)
        {
            // Start fade-out when movement stops
            if (audioSource.isPlaying)
            {
                // If there's already a fade-out in progress, stop it
                if (fadeCoroutine != null)
                {
                    StopCoroutine(fadeCoroutine);
                }
                fadeCoroutine = StartCoroutine(FadeOut());
            }
        }

        // Update movement state
        wasMoving = isMoving;

        // Update last position for the next frame
        lastPosition = targetTransform.position;
    }

    private IEnumerator FadeOut()
    {
        float startVolume = audioSource.volume;
        float currentTime = 0;

        while (currentTime < fadeOutDuration)
        {
            currentTime += Time.deltaTime;
            audioSource.volume = Mathf.Lerp(startVolume, 0, currentTime / fadeOutDuration);
            yield return null;
        }

        // Ensure volume is zero and stop the audio
        audioSource.volume = 0;
        audioSource.Stop();
        fadeCoroutine = null;
    }
}