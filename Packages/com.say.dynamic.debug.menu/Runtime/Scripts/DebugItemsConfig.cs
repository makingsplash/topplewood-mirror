using UnityEngine;

namespace DynamicDebugMenu
{
    [CreateAssetMenu(fileName = "DebugItemsConfig", menuName = "Configs/DebugItemsConfig")]
    public class DebugItemsConfig: ScriptableObject
    {
        public GameObject MainPrefab;
        public GameObject PagePrefab;
        public GameObject SubmenuPrefab;
        public GameObject LabelPrefab;
        public GameObject MultilineLabelPrefab;
        public GameObject CheckboxPrefab;
        public GameObject InputPrefab;
        public GameObject DropdownPrefab;
        public GameObject DropdownSavePrefab;
        public GameObject ButtonPrefab;
        public GameObject InputButtonPrefab;
        public GameObject DoubleInputButtonPrefab;
        public GameObject CheckboxSavePrefab;
        public GameObject DropdownInput;
        public GameObject DropdownInputFilter;
        public GameObject DropdownButton;
        public GameObject LabelButtonPrefab;
        public GameObject HoldButtonPrefab;
        public GameObject SliderPrefab;
        public GameObject ListSelectorPrefab;
    }
}