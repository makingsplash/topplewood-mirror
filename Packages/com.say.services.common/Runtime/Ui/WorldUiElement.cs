using SGS.UnityExtensions;
using UnityEngine;

namespace SGS.Services.Ui
{
    [RequireComponent(typeof(Canvas))]
    public class WorldUiElement : MonoBehaviour
    {
        private const int LayerUi = 5; 
        
        [SerializeField]
        private float _canvasUnitsInWorldUnit;
        
        private RectTransform _canvasTransform;

        private void Start()
        {
            _canvasTransform = GetComponent<RectTransform>();
            _canvasTransform.sizeDelta = new Vector2(Screen.width, Screen.height);

            float targetCanvasScale = 1 / _canvasUnitsInWorldUnit;
            _canvasTransform.localScale = Vector3.one * targetCanvasScale;

            // _canvasTransform.eulerAngles = Data.Configs.SceneConfig.CameraRotation;
            
            _canvasTransform.SetLayer(LayerUi);
        }
    }
}