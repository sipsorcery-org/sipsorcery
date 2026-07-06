using UnityEngine;
using TMPro;
using System; // Required for Type handling

public class UpdateCollectibleCount : MonoBehaviour
{
    private TextMeshProUGUI collectibleText; // Reference to the TextMeshProUGUI component

    void Start()
    {
        collectibleText = GetComponent<TextMeshProUGUI>();
        if (collectibleText == null)
        {
            Debug.LogError("UpdateCollectibleCount script requires a TextMeshProUGUI component on the same GameObject.");
            return;
        }
        UpdateCollectibleDisplay(); // Initial update on start
    }

    void Update()
    {
        UpdateCollectibleDisplay();
    }

    private void UpdateCollectibleDisplay()
    {
        int totalCollectibles = 0;

        // Check and count objects of type Collectible
        Type collectibleType = Type.GetType("Collectible");
        if (collectibleType != null)
        {
#if UNITY_6000_3_OR_NEWER
            totalCollectibles += FindObjectsByType(collectibleType).Length;
#else
            totalCollectibles += FindObjectsByType(collectibleType, FindObjectsSortMode.None).Length;
#endif
        }

        // Optionally, check and count objects of type Collectible2D as well if needed
        Type collectible2DType = Type.GetType("Collectible2D");
        if (collectible2DType != null)
        {
#if UNITY_6000_3_OR_NEWER
            totalCollectibles += FindObjectsByType(collectible2DType).Length;
#else
            totalCollectibles += FindObjectsByType(collectible2DType, FindObjectsSortMode.None).Length;
#endif
        }

        // Update the collectible count display
        collectibleText.text = $"Collectibles remaining: {totalCollectibles}";
    }
}
