using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using SGS.Configs;
using SGS.Data;
using SGS.Services.Pools;
using UnityEngine;
using Random = UnityEngine.Random;


namespace SGS.Services.Ui
{
    public class FlyingMoney
    {
        private readonly int _coinIdleAnimHash = Animator.StringToHash("Idle");
        
        private readonly List<Sequence> _activeSequences = new();
        private readonly List<IPoolItem> _flyObjects = new();

        private Vector3 _from;
        private Vector3 _to;
        private readonly int _coinsCount = 1;
        private readonly string _poolItem;
        private readonly string _soundOnSpawn;
        private float _fromZOffset;

        public FlyingMoney(string poolItem, string soundName, int count, Vector3 from, Vector3 to, float fromZOffset = 0f, float toZOffset = 0f)
        {
            _poolItem = poolItem;
            _soundOnSpawn = soundName;
            _coinsCount = count;
            
            Init(from, to, fromZOffset, toZOffset);
        }

        public FlyingMoney(string poolItem, string soundName, int count, RectTransform from, Transform to, float fromZOffset = 0f, float toZOffset = 0f)
        {
            _poolItem = poolItem;
            _soundOnSpawn = soundName;
            _coinsCount = count;
            Vector3[] worldCorners = new Vector3[4];
            from.GetWorldCorners(worldCorners);
            Vector3 worldCenter = worldCorners[0] + (worldCorners[2] - worldCorners[0]) * 0.5f;

            Init(worldCenter, to.position, fromZOffset, toZOffset);
        }

        private void Init(Vector3 worldCenter, Vector3 to, float fromZOffset = 0f, float toZOffset = 0f)
        {
            RectTransform canvasTransform = Service.UI.RootTransform.GetComponent<RectTransform>();
            _from = canvasTransform.InverseTransformPoint(worldCenter);
            _from.z += fromZOffset;
            _fromZOffset = fromZOffset;

            _to = Service.UI.RootTransform.InverseTransformPoint(to);
            _to.z += toZOffset;
        }

        public void StartFly(Action onAnimationComplete = null)
        {
            var settings = SayData.Configs.Animations.FlyingResource;
            for (int i = 0; i < _coinsCount; i++)
            {
                float delay = i * settings.SpawnDelay;
                int index = i;
                Sequence delaySequence = DOTween.Sequence().SetUpdate(false);
                _activeSequences.Add(delaySequence);
                delaySequence.AppendInterval(delay).SetUpdate(false);
                delaySequence.AppendCallback(() =>
                {
                    Action action = index == _coinsCount - 1 ? onAnimationComplete : null;
                    CreateFlyObject(settings, action);
                });
            }

            Sequence soundsSequence = DOTween.Sequence().SetUpdate(false);
            for (int i = 0; i < _coinsCount; i++)
            {
                soundsSequence.AppendCallback(TryPlaySoundOnSpawn);
                soundsSequence.AppendInterval(settings.SoundDelay).SetUpdate(false);
            }
        }

        private async void CreateFlyObject(AnimationsConfigBase.FlyingResourceAnimationSettings settings, Action onAnimationComplete = null)
        {
            IPoolItem flyMoneyObject = await Service.Pool.SpawnObject(_poolItem, Service.UI.RootTransform);

            var flyMoneyGo = flyMoneyObject.GetGameObject();
            _flyObjects.Add(flyMoneyObject);
            Transform flyMoneyTransform = flyMoneyGo.transform;
            flyMoneyTransform.localPosition = new Vector3(_from.x, _from.y, _fromZOffset);
            flyMoneyTransform.localScale = Vector3.one;

            Animator animator = flyMoneyGo.GetComponentInChildren<Animator>();
            if (animator != null && animator.HasState(0, _coinIdleAnimHash))
            {
                animator.Play(_coinIdleAnimHash, 0);
            }

            Transform flyMoneyChild = flyMoneyTransform.GetChild(0);
            flyMoneyChild.transform.localPosition = Vector3.zero;
            Vector3 flyChildVector = Random.insideUnitCircle;
            Vector3 spawnMovePosition = flyMoneyTransform.localPosition + flyChildVector * settings.SpawnRadius;

            Sequence moneySpawnSequence = DOTween.Sequence().SetUpdate(false);
            _activeSequences.Add(moneySpawnSequence);
            moneySpawnSequence.Append(flyMoneyTransform.DOLocalMove(spawnMovePosition, settings.SpawnMoveTime));
            moneySpawnSequence.AppendCallback(() =>
            {
                Sequence moneyFlySequence = DOTween.Sequence().SetUpdate(false);
                _activeSequences.Add(moneyFlySequence);
                moneyFlySequence.AppendInterval(settings.TotalDuration * settings.DelayAndFlyTime).SetUpdate(false);
                moneyFlySequence.Append(flyMoneyTransform
                    .DOLocalMove(_to, settings.TotalDuration * (1 - settings.DelayAndFlyTime)).SetEase(Ease.InSine)
                    .SetUpdate(false));

                Sequence moneyScaleSequence = DOTween.Sequence().SetUpdate(false);
                _activeSequences.Add(moneyScaleSequence);
                moneyScaleSequence.AppendInterval(settings.TotalDuration * settings.ScaleTime).SetUpdate(false);
                moneyScaleSequence.Append(flyMoneyTransform
                    .DOScale(Vector3.one * settings.TargetScale, settings.TotalDuration * (1 - settings.ScaleTime))
                    .SetEase(Ease.InOutSine).SetUpdate(false));

                Sequence moneyChildFlySequence = DOTween.Sequence().SetUpdate(false);
                _activeSequences.Add(moneyChildFlySequence);
                moneyChildFlySequence.Append(DOVirtual.Float(0, 1, settings.TotalDuration,
                    value =>
                    {
                        flyMoneyChild.transform.localPosition = flyChildVector *
                                                                (settings.SpawnRadius *
                                                                 settings.ChildCurve.Evaluate(value));
                    }));

                moneyFlySequence.onComplete = () =>
                {
                    _activeSequences.Remove(moneySpawnSequence);
                    _activeSequences.Remove(moneyFlySequence);
                    _activeSequences.Remove(moneyScaleSequence);
                    _activeSequences.Remove(moneyChildFlySequence);

                    flyMoneyGo.transform.DOKill();
                    flyMoneyChild.DOKill();

                    _flyObjects.Remove(flyMoneyObject);
                    Service.Pool.ReturnToPool(flyMoneyObject);

                    onAnimationComplete?.Invoke();
                };
            });
        }

        public void Destroy()
        {
            foreach (var seq in _activeSequences)
            {
                seq.onComplete = null;
                seq.Complete();
            }
            _activeSequences.Clear();
        
            foreach (var obj in _flyObjects)
            {
                if (obj != null)
                {
                    Service.Pool.ReturnToPool(obj);
                }
            }
            _flyObjects.Clear();
        }

        private void TryPlaySoundOnSpawn()
        {
            if (string.IsNullOrEmpty(_soundOnSpawn))
            {
                return;
            }
            
            GlobalService.AudioService.Play(_soundOnSpawn).Forget();
        }
    }
}
