using TMPro;
using UnityEngine;

namespace SGS.Services.Localization
{
    public class LocaleComponent : MonoBehaviour
    {
        [SerializeField]
        private string _id = "";
        
        private object[] _parameters;
        private TMP_Text _tmpText;

        public string Id => _id;

        public void SetId(string id)
        {
            _id = id;
            Localize();
        }
        public void SetParameters(params object[] parameters)
        {
            _parameters = parameters;
            Localize();
        }
        
        private void OnEnable()
        {
            _tmpText = GetComponent<TMP_Text>();
            Localize();
        }

        private void Localize()
        {
            if (string.IsNullOrEmpty(_id))
            {
                return;
            }

            string localizedString = GlobalService.Localization.GetLocalizedString(_id);
            if (_parameters != null)
            {
                localizedString = string.Format(localizedString, _parameters);
            }
            
            if (!string.IsNullOrEmpty(localizedString))
            {

                if (_tmpText != null)
                {
                    _tmpText.text = localizedString;
                }
            }
        }
    }
}