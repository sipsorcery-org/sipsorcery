using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

public class PlayerController : MonoBehaviour
{
    public float speed = 0f;
    public TextMeshProUGUI countText;
    public GameObject winTextObject;

    private Rigidbody rb;
    private int count;
    private float movementX;
    private float movementY;

#pragma warning disable 0649
    [SerializeField] private Camera cam;
#pragma warning restore 0649

    private RenderTexture _mainCamDupRenderTexture;
    private Texture2D _mainCamTexture2D;

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        count = 0;

        SetCountText();
        winTextObject.SetActive(false);

        var texture = cam.targetTexture;
        _mainCamDupRenderTexture = texture;
        _mainCamTexture2D = new Texture2D(texture.width, texture.height);
    }

    void OnMove(InputValue movementValue)
    {
        Vector2 movementVector = movementValue.Get<Vector2>();

        movementX = movementVector.x;
        movementY = movementVector.y;
    }

    void SetCountText()
    {
        countText.text = "Count: " + count.ToString();

        if (count >= 10)
        {
            winTextObject.SetActive(true);
        }
    }

    void FixedUpdate()
    {
        Vector3 movement = new Vector3(movementX, 0.0f, movementY);
        rb.AddForce(movement * speed);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("PickUp"))
        {
            other.gameObject.SetActive(false);
            count++;

            SetCountText();
        }
    }

    //private void OnRenderObject()
    //{
    //    RenderTexture.active = _mainCamDupRenderTexture;
    //    _mainCamTexture2D.ReadPixels(new Rect(0, 0, _mainCamTexture2D.width, _mainCamTexture2D.height), 0, 0);
    //    _mainCamTexture2D.Apply();
    //    RenderTexture.active = null;

    //    // This call to get the raw pixels seems to be the biggest performance hit. On my Win10 i7 machine
    //    // frame rate reduces from approx. 200 fps to around 20fps with this call.
    //    var arr = _mainCamTexture2D.GetRawTextureData();
    //    byte[] flipped = new byte[arr.Length];

    //    int width = _mainCamTexture2D.width;
    //    int height = _mainCamTexture2D.height;
    //    int pixelSize = 4;
    //    int stride = width * pixelSize;
    //    for (int row = height - 1; row >= 0; row--)
    //    {
    //        Buffer.BlockCopy(arr, row * stride, flipped, (height - row - 1) * stride, stride);
    //    }
    //}
}
