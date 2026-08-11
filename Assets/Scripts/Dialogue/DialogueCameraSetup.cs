using UnityEngine;

public class DialogueCameraSetup : MonoBehaviour
{
    private Camera dialogueCamera;

    public Canvas canvas;


    private void Awake()
    {
        dialogueCamera = GetComponent<Camera>();

        if (Camera.main != null && Camera.main != dialogueCamera)
        {
            dialogueCamera.gameObject.SetActive(false);

            canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                canvas.worldCamera = Camera.main;
            }
        }
        else
        {
            dialogueCamera.gameObject.SetActive(true);
        }
    }
}