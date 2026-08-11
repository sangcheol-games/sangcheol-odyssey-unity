using UnityEngine;

public class DialogueCameraSetup : MonoBehaviour
{
    private Camera dialogueCamera;

    public Canvas canvas;


    private void Awake()
    {
        dialogueCamera = GetComponent<Camera>();

        dialogueCamera.gameObject.SetActive(false);


        if (Camera.main == null)
        {
            dialogueCamera.gameObject.SetActive(true);
        }


        if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            canvas.worldCamera = Camera.main;
        }
    }
}