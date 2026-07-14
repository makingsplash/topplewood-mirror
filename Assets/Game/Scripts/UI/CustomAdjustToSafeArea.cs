using UnityEngine;
using Services;

namespace CopyPasteScripts
{
    public class CustomAdjustToSafeArea : MonoBehaviour
    {
        private enum SafeAreaAlign
        {
            Top,
            Bottom,
            Stretch
        }

        private const float _defaultSaykitBannerSize = 100.0f;
        
        [SerializeField] 
        private SafeAreaAlign align = SafeAreaAlign.Top;
        
        [SerializeField]
        private bool canHaveSaykitBanner;

        private Rect _lastSafeArea;

        private void Start()
        {
            Align();
        }

#if UNITY_EDITOR

        private void Update()
        {
            if (_lastSafeArea == Screen.safeArea)
            {
                return;
            }
            
            Align();            
        }

#endif

        
        private static Rect GetSafeArea()
        {
            Rect safeArea = Screen.safeArea;
            // if (Application.isEditor)
            // {
            //     Rect notSafeArea = new Rect(0f, 102f / 2436f, 1f, 2202f / 2436f);
            //     safeArea = new Rect(Screen.width * notSafeArea.x, Screen.height * notSafeArea.y, Screen.width * notSafeArea.width, Screen.height * notSafeArea.height);
            // }
            
            return safeArea;
        }

        private static float CalculateBannerSizeInPixels()
        {
            float bannerSizeDp = GetSayKitBannerSizeDp();
            return bannerSizeDp * (Screen.dpi / 160);
        }

        private static float GetSayKitBannerSizeDp()
        {
            try
            {
                var sayKit = GlobalService.SayKit;
                if (sayKit == null)
                {
                    return _defaultSaykitBannerSize;
                }

                float bannerSize = sayKit.GetBackgroundBannerSize();
                return bannerSize > 0 ? bannerSize : _defaultSaykitBannerSize;
            }
            catch
            {
                return _defaultSaykitBannerSize;
            }
        }

        private void Align()
        {
            Rect safeArea = GetSafeArea();
            RectTransform tr = (RectTransform)transform;
            if (align == SafeAreaAlign.Top)
            {
                float screenHeight = Screen.height;
                float screenHeightSafe = safeArea.y + safeArea.height;
                float percent = screenHeightSafe / screenHeight;

                tr.anchorMin = new Vector3(tr.anchorMin.x, percent);
                tr.anchorMax = new Vector3(tr.anchorMax.x, percent);
            }
            else if (align == SafeAreaAlign.Bottom)
            {
                //warning, didnt check it, may be buggy
                float screenHeight = Screen.height;
                float safeBottomHeight = safeArea.y;
                if (canHaveSaykitBanner)
                {
                    safeBottomHeight += CalculateBannerSizeInPixels();
                }
                float percent = safeBottomHeight / screenHeight;
                tr.anchorMin = new Vector3(tr.anchorMin.x, percent);
                tr.anchorMax = new Vector3(tr.anchorMax.x, percent);
            }
            else if (align == SafeAreaAlign.Stretch)
            {
                StretchToSafeArea(tr, safeArea);
            }

            _lastSafeArea = safeArea;
        }

        private void StretchToSafeArea(RectTransform rectTransform, Rect safeArea)
        {
            float safeBottomY = canHaveSaykitBanner ? safeArea.y + CalculateBannerSizeInPixels() : safeArea.y;
            float safeTopY = safeArea.y + safeArea.height;
            rectTransform.anchorMin = new Vector2(rectTransform.anchorMin.x, safeBottomY / Screen.height);
            rectTransform.anchorMax = new Vector2(rectTransform.anchorMax.x, safeTopY / Screen.height);
        }
    }
}
