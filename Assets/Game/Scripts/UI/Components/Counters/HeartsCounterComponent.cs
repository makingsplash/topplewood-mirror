using System;
using DataStorage;
using Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Ui.Components.Counters
{
    [MovedFrom(sourceNamespace: "UI.Components.Counters")]
    public sealed class HeartsCounterComponent : BaseCounterComponent
    {
        [SerializeField] private Image _icon;

        [SerializeField] private Sprite _normalHeart;
        [SerializeField] private Sprite _disabledHeart;

        [SerializeField] private TMP_Text _timerTxt;
        [SerializeField] private GameObject _infinityIcon;

        [SerializeField] private GameObject _fullState;
        [SerializeField] private GameObject _counterState;

        private bool IsMaxHearts => GlobalService.Hearts.IsMaxHearts();
        private bool IsInfinityHearts => GlobalService.Hearts.IsInfinityHearts();

        protected override bool IsAddButtonAvailable() => !IsMaxHearts && !IsInfinityHearts;

        private void OnEnable()
        {
            if (!GlobalService.Hearts.EnableHearts())
            {
                gameObject.SetActive(false);
                return;
            }

            Button.onClick.AddListener(OnButtonClick);
            Data.Meta.HeartsAmount.OnChanged += OnChangedValue;
            Data.Meta.InfiniteHeartsExpireDate.OnChanged += HandleOnInfiniteHeartsChanged;

            if (IsInfinityHearts)
                GlobalService.Timer.Update.AddListener(UpdateInfinityTimer);
            else if (!IsMaxHearts)
                GlobalService.Timer.Update.AddListener(UpdateTime);

            RefreshHearts();
        }

        private void OnDisable()
        {
            Data.Meta.HeartsAmount.OnChanged -= OnChangedValue;
            Data.Meta.InfiniteHeartsExpireDate.OnChanged -= HandleOnInfiniteHeartsChanged;
            Button.onClick.RemoveListener(OnButtonClick);

            GlobalService.Timer.Update.RemoveListener(UpdateTime);
            GlobalService.Timer.Update.RemoveListener(UpdateInfinityTimer);
        }

        private void OnButtonClick()
        {
            Service.UI.Show(ScreenNames.BuyHeartPopup);
        }

        private void HandleOnInfiniteHeartsChanged(DateTime expireDate)
        {
            if (IsInfinityHearts)
                GlobalService.Timer.Update.AddListener(UpdateInfinityTimer);

            RefreshHearts();
        }

        private void OnChangedValue(int count)
        {
            RefreshHearts();
        }

        private void RefreshHearts()
        {
            bool isInfinity = IsInfinityHearts;
            bool isMax = !isInfinity && IsMaxHearts;

            _infinityIcon.SetActive(isInfinity);
            _fullState.SetActive(isMax);
            _counterState.SetActive(!isMax);
            _countTxt.gameObject.SetActive(!isInfinity);

            if (isInfinity)
                _icon.sprite = _normalHeart;
            else
                SetCount(Data.Meta.HeartsAmount.Value);

            RefreshAddButton();
        }

        private void UpdateInfinityTimer()
        {
            TimeSpan timeLeft = GlobalService.Hearts.InfinityHeartsTimeLeft();
            if (timeLeft <= TimeSpan.Zero)
            {
                if (!IsMaxHearts)
                    GlobalService.Timer.Update.AddListener(UpdateTime);

                GlobalService.Timer.Update.RemoveListener(UpdateInfinityTimer);
                RefreshHearts();
                return;
            }

            var timeText = GlobalService.Localization.GetLocalizedTimeInterval(timeLeft);
            if (_timerTxt.text != timeText)
                _timerTxt.text = timeText;
        }

        private void UpdateTime()
        {
            if (IsInfinityHearts)
            {
                GlobalService.Timer.Update.RemoveListener(UpdateTime);
                return;
            }

            if (IsMaxHearts)
            {
                GlobalService.Timer.Update.RemoveListener(UpdateTime);
                RefreshHearts();
                return;
            }

            TimeSpan timeToNextUpdate = GlobalService.Hearts.TimeToNextUpdate();
            var timeText = GlobalService.Localization.GetLocalizedTimeInterval(timeToNextUpdate);
            if (_timerTxt.text != timeText)
                _timerTxt.text = timeText;
        }

        private void SetCount(int hearts)
        {
            _icon.sprite = hearts > 0 ? _normalHeart : _disabledHeart;
            SetCountText(hearts);
        }
    }
}
