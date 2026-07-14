using System;

using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMSubmenu : MonoBehaviour
    {
        [SerializeField]
        private Button _button;
        
        [SerializeField]
        private Text _label;

        private Action _action;

        public static void Create(DDMContext context, string label, Action action)
        {
            GameObject prefab = Instantiate(context.Config.SubmenuPrefab, context.Parent);
            DDMSubmenu component = prefab.GetComponent<DDMSubmenu>();

            component._label.text = "> " + label;
            component._action = action;
        }

        private void Start()
        {
            _button.onClick.AddListener(OnButtonClick);
        }

        private void OnButtonClick()
        {
            _action.Invoke();
        }
    }
}