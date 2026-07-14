using System;
using UnityEngine;
using UnityEngine.UI;


namespace DynamicDebugMenu.Elements
{
    public class DDMLabel : MonoBehaviour
    {
        [SerializeField]
        private Text _label;

        private Func<string> _initFunc;

        public string Label
        {
            get => _label.text;
            set => _label.text = value;
        }

        public static DDMLabel Create(DDMContext context, string text)
        {
            GameObject prefab = Instantiate(context.Config.LabelPrefab, context.Parent);
            DDMLabel component = prefab.GetComponent<DDMLabel>();

            component._label.text = text;
            return component;
        }

        public static DDMLabel CreateDynamic(DDMContext context, Func<string> textFunc)
        {
            GameObject prefab = Instantiate(context.Config.LabelPrefab, context.Parent);
            DDMLabel component = prefab.GetComponent<DDMLabel>();

            component._initFunc = textFunc;
            component._label.text = textFunc();
            return component;
        }

        public static DDMLabel CreateMultiline(DDMContext context, string text)
        {
            GameObject prefab = Instantiate(context.Config.MultilineLabelPrefab, context.Parent);
            DDMLabel component = prefab.GetComponent<DDMLabel>();

            component._label.text = text;
            return component;
        }

        public static DDMLabel CreateMultilineDynamic(DDMContext context, Func<string> textFunc)
        {
            GameObject prefab = Instantiate(context.Config.MultilineLabelPrefab, context.Parent);
            DDMLabel component = prefab.GetComponent<DDMLabel>();

            component._initFunc = textFunc;
            component._label.text = textFunc();
            return component;
        }

        private void Update()
        {
            if (_initFunc != null)
            {
                _label.text = _initFunc();
            }
        }
    }
}