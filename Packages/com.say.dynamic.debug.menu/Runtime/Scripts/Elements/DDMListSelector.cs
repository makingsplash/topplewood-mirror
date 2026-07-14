using System;
using System.Collections.Generic;
using System.Linq;
using DynamicDebugMenu.Elements;
using UnityEngine;
using UnityEngine.UI;


namespace Client.Scripts.Tools.DebugTools.DynamicDebugMenu.Elements
{
    public class DDMListSelector : MonoBehaviour
    {
        [SerializeField]
        private GameObject _listContainer;

        [SerializeField]
        private GameObject _itemPrototype;

        [SerializeField]
        private InputField _filterInput;

        [SerializeField]
        private Text _label;
        
        [SerializeField]
        private Button _button;

        [SerializeField]
        private Text _buttonLabel;

        private const string _defaultButtonLabel = "Go"; 
        private const int _defaultMaxListOptions = 75;
        private const string _tooManyOptions = "Too many options, filter more...";
        
        private Func<List<string>> _getOptions;
        private int _initValue;
        private readonly List<Toggle> _listOptions = new();
        private readonly Dictionary<int, int> _listOptionsIndices = new();
        private int _selectedOptionIndex = -1;
        private int _maxListOptions;

        private Action<int> _onSelect;
        private Action<int> _onClick;

        
        public static DDMListSelector Create(DDMContext context, string label, Func<List<string>> optionsFunc, int initIndex, Action<int> onSelect, Action<int> onClick)
        {
            return Create(context, label,_defaultButtonLabel, _defaultMaxListOptions, optionsFunc, initIndex, onSelect, onClick);
        }

        public static DDMListSelector Create(DDMContext context, string label, string buttonLabel, int maxListOptions, Func<List<string>> optionsFunc, int initIndex, Action<int> onSelect, Action<int> onClick)
        {
            GameObject selectorObject = Instantiate(context.Config.ListSelectorPrefab, context.Parent);
            DDMListSelector selector = selectorObject.GetComponent<DDMListSelector>();
            selector._label.text = label;
            selector._buttonLabel.text = buttonLabel;
            selector._onSelect = onSelect;
            selector._onClick = onClick;
            selector._getOptions = optionsFunc;
            selector._initValue = initIndex;
            selector._maxListOptions = maxListOptions;
            return selector;
        }

        private void Start()
        {
            UpdateFilter("");
            SelectItem(_initValue);

            _button.onClick.AddListener(OnClick);
            _filterInput.onValueChanged.AddListener(UpdateFilter);
        }
        
        private void OnClick()
        {
            _onClick.Invoke(_selectedOptionIndex);
        }

        private void OnItemClick(bool on, int idx)
        {
            if (!on)
            {
                _listOptions[idx].SetIsOnWithoutNotify(true);
            }
            else
            {
                for (int i = 0; i < _listOptions.Count; ++i)
                {
                    if(i != idx)
                    {
                        _listOptions[i].SetIsOnWithoutNotify(false);
                    }
                }

            }

            if (_listOptionsIndices.TryGetValue(idx, out int optionIndex))
            {
                _onSelect(optionIndex);
                _selectedOptionIndex = optionIndex;
            }
        }

        private void SelectItem(int optionIndex)
        {
            _selectedOptionIndex = optionIndex;
            foreach (KeyValuePair<int, int> kv in _listOptionsIndices)
            {
                _listOptions[kv.Key].SetIsOnWithoutNotify(kv.Value == optionIndex);
            }
        }

        private void UpdateFilter(string filterString)
        {
            filterString = filterString.ToLowerInvariant();
            var options = _getOptions();

            bool MatchesFilter(string s) => s.ToLowerInvariant().IndexOf(filterString, StringComparison.Ordinal) >= 0;

            var numFiltered = options.Count(MatchesFilter);

            _listOptionsIndices.Clear();
            if (numFiltered > _maxListOptions)
            {
                EnsureActiveListElements(1);
                var itemText = _listOptions[0].GetComponentInChildren<Text>();
                var itemToggle = _listOptions[0].GetComponent<Toggle>();
                itemToggle.enabled = false;
                itemText.text = _tooManyOptions;
                return;
            }

            EnsureActiveListElements(numFiltered);

            int idx = 0;
            for (int i = 0; i < options.Count; i++)
            {
                if (!MatchesFilter(options[i]))
                {
                    continue;
                }
                var itemText = _listOptions[idx].GetComponentInChildren<Text>();
                var itemToggle = _listOptions[idx].GetComponent<Toggle>();
                itemToggle.enabled = true;
                itemText.text = options[i];
                _listOptionsIndices.Add(idx, i);
                _listOptions[idx].SetIsOnWithoutNotify(i == _selectedOptionIndex);
                idx += 1;
            }
        }

        private void EnsureActiveListElements(int activeElements)
        {
            var containerTransform = _listContainer.transform;
            while (_listOptions.Count < activeElements)
            {
                var idx = _listOptions.Count;
                var item = Instantiate(_itemPrototype, containerTransform);
                var itemToggle = item.GetComponent<Toggle>();
                itemToggle.onValueChanged.AddListener(v => OnItemClick(v, idx));  
                _listOptions.Add(itemToggle);
            }

            for (int i = 0; i < _listOptions.Count; i++)
            {
                _listOptions[i].gameObject.SetActive(i < activeElements);
            }
        }
    }
}